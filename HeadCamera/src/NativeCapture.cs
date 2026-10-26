using System;
using System.Collections.Generic;
using System.Reflection;
using CullingSystem;
using Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace PreLuna.HeadCamera
{
    // A standalone world camera. Never clone FPSCamera/PlayerAgent or call their render callbacks:
    // FPSCamera.OnPreCull also moves the view and updates real flashlight/interaction state.
    internal sealed class NativeCapture : IDisposable
    {
        internal ControlView CapturedView(ControlView view) => view with { Position = camera.transform.position, Direction = camera.transform.forward };
        GameObject root;
        Camera camera;
        C_Camera culler;
        ClusteredRendering lighting;
        ClusteredLightCollector collector;
        PreLitVolume fog;
        IRF.InstancedRenderFeatureCamera instancing;
        CameraRenderCallbacks callbacks;
        RenderTexture texture;
        readonly CapturePresentation presentation = new();
        readonly CaptureDiagnostics diagnostics = new();
        CommandBuffer beforeGBuffer, beforeLighting;
        readonly List<UnityEngine.Object> ownedAssets = new();
        readonly List<(long handle, int instance)> hudCameraIdentities = new();
        void RegisterHudCamera(Camera value)
        {
            var identity = (value.Pointer.ToInt64(), value.GetInstanceID());
            hudCameraIdentities.Add(identity); HeadCameraApi.RegisterHudCamera(identity.Item1, identity.Item2);
        }
        static readonly HashSet<long> ownedClusters = new();
        static readonly OwnedFogLifetime ownedFog = new();
        internal static bool OwnsFog(PreLitVolume value) => value is not null && ownedFog.Contains(value.Pointer.ToInt64(), value.GetInstanceID());
        static readonly Dictionary<Type, PropertyInfo[]> gpuProperties = new();
        internal static bool OwnsCluster(ClusteredRendering value) => value != null && ownedClusters.Contains(value.Pointer.ToInt64());
        bool disposed, initialized;
        bool renderActive, transparencyActive;
        Exception callbackError;
        string lastCaptureState, lastLampState;
        float nextLampNotice;
        int callbackPhases;
        public RenderTexture Texture => initialized && !disposed ? presentation.Texture : null;

        public void Initialize(Camera main, int width)
        {
            if (initialized) return;
            if (disposed) throw new ObjectDisposedException(nameof(NativeCapture));
            var template = ClusteredRendering.Current;
            var templateFog = PreLitVolume.Current;
            if (main == null || template == null || templateFog == null || !RenderPipe.Initialized)
                throw new InvalidOperationException("原版场景渲染器尚未准备好");
            width = Math.Clamp(width, 160, 1920);
            int height = Math.Max(90, width * 9 / 16);
            using var globals = new RenderGlobals();
            try
            {
                root = new GameObject("PreLuna.HeadCamera.WorldCamera"); root.SetActive(false);
                camera = root.AddComponent<Camera>(); camera.CopyFrom(main);
                RegisterHudCamera(camera);
                camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.rect = new Rect(0, 0, 1, 1); camera.depth = -50; camera.useOcclusionCulling = false;
                camera.allowMSAA = false; camera.allowDynamicResolution = false;
                camera.depthTextureMode = DepthTextureMode.Depth;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf)
                { name = "HeadCamera.Feed", antiAliasing = 1, useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear };
                if (!texture.Create()) throw new InvalidOperationException("副相机 RenderTexture 创建失败");
                camera.targetTexture = texture;

                culler = root.AddComponent<C_Camera>(); culler.enabled = false;
                culler.m_camera = camera; culler.m_runOnUpdate = false;
                lighting = root.AddComponent<ClusteredRendering>(); lighting.enabled = false;
                ownedClusters.Add(lighting.Pointer.ToInt64());
                fog = root.AddComponent<PreLitVolume>(); fog.enabled = false;
                ownedFog.Register(fog.Pointer.ToInt64(), fog.GetInstanceID());
                CopyFogOptions(templateFog, fog);
                lighting.m_maxFarClip = Math.Min(template.m_maxFarClip, Plugin.ViewDistance.Value);
                // Serialized materials are cloned, while buffers/textures/cameras are never copied.
                lighting.m_backSideClusterMaterial = OwnMaterial(template.m_backSideClusterMaterial);
                lighting.m_frontSideClusterMaterial = OwnMaterial(template.m_frontSideClusterMaterial);
                lighting.m_deferredMaterial = OwnMaterial(template.m_deferredMaterial);
                lighting.m_depthRemapMaterial = OwnMaterial(template.m_depthRemapMaterial);
                lighting.m_downsampleMaterial = OwnMaterial(template.m_downsampleMaterial);
                lighting.UpdateShadows = template.UpdateShadows; lighting.UpdateLitVolume = true; lighting.UpdateCluster = true;
                var lightBuffer = new GameObject("HeadCamera.LightBuffer"); lightBuffer.transform.SetParent(root.transform, false);
                lighting.m_lightBufferCamera = lightBuffer.AddComponent<Camera>();
                RegisterHudCamera(lighting.m_lightBufferCamera);
                lighting.m_lightBufferCamera.CopyFrom(camera); lighting.m_lightBufferCamera.enabled = false;
                lighting.m_camera = camera;
                lighting.LitVolume = fog;
                instancing = root.AddComponent<IRF.InstancedRenderFeatureCamera>(); instancing.enabled = false;
                callbacks = root.AddComponent<CameraRenderCallbacks>(); callbacks.Owner = this;
                root.SetActive(true); // Native Awake only; all camera/update components remain disabled.
                camera.enabled = false;
                ClusteredRendering.Current = lighting; PreLitVolume.Current = fog; C_Camera.Current = culler;
                lighting.Setup();
                // Native Setup creates and assigns its own collector. Keep that exact
                // instance; a collector added before Setup is unused and owns no buffers.
                collector = lighting.LightCollector;
                if (collector == null || collector.Pointer == template.LightCollector?.Pointer)
                    throw new InvalidOperationException("副相机未获得独立的原生灯光收集器");
                collector.enabled = false;
                // Native setup uses screen size and registers a GUI callback: detach that callback
                // and use this camera's fixed texture dimensions instead.
                GuiManager.UnregisterOnResolutionChangeHandler(lighting.Cast<UI_IOnResolutionChange>());
                lighting.OnResolutionchange(width, height);
                ConfigureBuffers(); // Setup resets depthTextureMode to None; configure only AFTER native setup.
                fog.SetFogSettings(templateFog.GetFogSettings());
                beforeGBuffer = new CommandBuffer { name = "HeadCamera.PreRender" };
                beforeLighting = new CommandBuffer { name = "HeadCamera.PreLighting" };
                camera.AddCommandBuffer(CameraEvent.BeforeGBuffer, beforeGBuffer);
                camera.AddCommandBuffer(CameraEvent.BeforeLighting, beforeLighting);
                initialized = true;
                Plugin.Instance.Log.LogInfo("[头戴摄像头] 独立渲染器就绪：" + width + "×" + height + "；副相机无 AudioListener、FPS 控制器和玩家对象。");
            }
            catch { Dispose(); throw; }

        }

        Material OwnMaterial(Material original)
        {
            if (original == null) return null;
            var copy = UnityEngine.Object.Instantiate(original); ownedAssets.Add(copy); return copy;
        }
        void ConfigureBuffers() => camera.depthTextureMode = Plugin.BufferFix.Value
            ? DepthTextureMode.Depth | DepthTextureMode.MotionVectors : DepthTextureMode.None;
        void CopyFogOptions(PreLitVolume original, PreLitVolume target)
        {
            string[] names = { "mode", "m_densityNoise", "DensityBoost", "m_fogDistance", "m_fogColor", "m_fogDensity", "fogLightAttenuation",
                "m_fogAmbience", "m_densityNoiseDirection", "m_densityNoiseSpeed", "m_densityNoiseScale", "m_densityHeightAltitude",
                "m_densityHeightRange", "m_densityHeightMaxBoost", "nearPeak", "nearRange", "lightFogTonemap", "FogShadowSamples" };
            foreach (string name in names)
            {
                var property = typeof(PreLitVolume).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite) throw new MissingMemberException("PreLitVolume", name);
                property.SetValue(target, property.GetValue(original));
            }
            // Shader objects hold bindings; give the feed its own shader instances.
            if (original.m_computeFogVolume != null)
            { target.m_computeFogVolume = UnityEngine.Object.Instantiate(original.m_computeFogVolume); ownedAssets.Add(target.m_computeFogVolume); }
            if (original.ComputeIndirect != null)
            { target.ComputeIndirect = UnityEngine.Object.Instantiate(original.ComputeIndirect); ownedAssets.Add(target.ComputeIndirect); }
            target.FogAACount = 1; target.FogPostBlur = 0;
            target.AdvancedGI = false; target.UpdateIndirectVolume = false;
            target.UpdateFogVolume = true; target.UpdateLitVolume = true;
        }

        public void Capture(PlayerAgent target, Camera main, ControlView? controlView = null)
        {
            if (!initialized || disposed || target == null || main == null) throw new InvalidOperationException("副相机或观看对象失效");
            if (target.m_movingCuller?.CurrentNode == null) throw new InvalidOperationException("队友房间裁剪节点尚未加载");
            var head = controlView.HasValue ? null : target.GetHeadCamTransform();
            Vector3 eye = controlView?.Position ?? (head != null ? head.position : target.EyePosition);
            Vector3 direction = controlView?.Direction ?? target.TargetLookDir;
            if (direction.sqrMagnitude < .001f) direction = target.Forward;
            if (direction.sqrMagnitude < .001f) direction = head != null ? head.forward : Vector3.forward;
            direction.Normalize();
            float offset = Plugin.EyeOffset.Value;
            // Keep the view on this side of walls/doors; do not relocate any player/culler.
            if (Physics.Raycast(eye, direction, out RaycastHit hit, offset + .04f, LayerManager.MASK_WORLD, QueryTriggerInteraction.Ignore))
                offset = Math.Max(0, hit.distance - .04f);
            camera.transform.SetPositionAndRotation(eye + direction * offset, Quaternion.LookRotation(direction, Vector3.up));
            camera.fieldOfView = Math.Clamp(controlView?.FieldOfView ?? Plugin.FieldOfView.Value, 45, 110);
            camera.aspect = texture.width / (float)texture.height;
            camera.nearClipPlane = .06f; camera.farClipPlane = Math.Min(main.farClipPlane, Plugin.ViewDistance.Value);
            camera.cullingMask = main.cullingMask;
            ConfigureBuffers();
            culler.m_cullAgent = target.m_movingCuller; // Read the existing node; never UpdatePosition/SetCurrentNode.
            using var globals = new RenderGlobals();
            BotLightViewScope viewLight = null;
            bool changedVisibility = false, changedIndirect = false;
            Exception restorationFailure = null;
            string captureState = null;
            bool logFrame = false;
            try
            {
                ClusteredRendering.Current = lighting; PreLitVolume.Current = fog; C_Camera.Current = culler;
                new LightBufferBindings(lighting).Restore();
                C_Camera.RenderWithElevatorMask = false;
                IRF.InstancedRenderFeatureCamera.DimensionIndex = target.DimensionIndex;
                CameraManager.CullingPosition = camera.transform.position;
                CameraManager.CullingDirection = direction; CameraManager.CullingFov = camera.fieldOfView;
                // Existing native spotlights need their shadow atlas refreshed for
                // this viewpoint too. Follow the main renderer's setting.
                lighting.UpdateShadows = globals.UpdateSceneShadows;
                captureState = "controlled=" + controlView.HasValue
                    + " nativeSceneLights=True nativeShadowUpdates=" + lighting.UpdateShadows;
                logFrame = captureState != lastCaptureState; lastCaptureState = captureState;
                if (logFrame) Plugin.Instance.Log.LogInfo("[摄像头阶段] 拍摄 BEGIN " + captureState + " pos=" + camera.transform.position + " HDR=" + camera.allowHDR + " path=" + camera.actualRenderingPath + " output=" + Plugin.OutputMode.Value);
                viewLight = BotLightViewScope.For(target, out string lampAttempt);
                changedVisibility = true;
                culler.RunVisibility(); C_CullingManager.C_CameraUpdate(); C_CullingManager.SortAndUpdateLights();
                viewLight?.BeforeRender();
                var mainFog = globals.Fog;
                if (mainFog != null) fog.SetFogSettings(mainFog.GetFogSettings());
                fog.UpdateShaderParameters();
                beforeGBuffer.Clear(); beforeLighting.Clear();
                changedIndirect = true;
                DrawIndirect(camera);
                // Execute render-only native stages at Unity's real callback phases so
                // transparent visibility has been collected before OnPreRender.
                // Native component Updates remain disabled and no FPS callback is involved.
                callbackError = null; callbackPhases = 0; renderActive = true;
                camera.Render();
                var lampCheck = NativeBotLamp.Inspect(target);
                string lampState = target.Pointer + " / " + lampAttempt + " / " + lampCheck.Reason;
                if (lampState != lastLampState && Time.unscaledTime >= nextLampNotice) {
                    lastLampState = lampState; nextLampNotice = Time.unscaledTime + 5;
                    Plugin.Instance.Log.LogInfo("[副相机原生手电] attempt=" + lampAttempt + " " + NativeBotLamp.Inspect(target, true).Details + " " + viewLight?.Describe() + " " + viewLight?.Pose);
                }
                if (callbackError != null) throw new InvalidOperationException("副相机渲染回调失败", callbackError);
                if ((callbackPhases & 3) != 3) throw new InvalidOperationException("副相机没有执行完整的 PreCull / PreRender 回调，不能确认画面提交");
            }
            finally
            {
                // Restore all independent stages even if rendering/native callbacks throw.
                renderActive = false;
                lighting.UpdateShadows = false;
                try { FogCompatibility.Restore(lighting); } catch (Exception e) { restorationFailure ??= e; }
                try { if (transparencyActive) { lighting.OnPostRender(); transparencyActive = false; } } catch (Exception e) { restorationFailure = e; }
                try { viewLight?.Dispose(); } catch (Exception e) { restorationFailure ??= e; }
                globals.Restore();
                try
                {
                    if (changedVisibility && globals.Culler != null)
                    {
                        globals.Culler.RunVisibility(); C_CullingManager.C_CameraUpdate(); C_CullingManager.SortAndUpdateLights();
                    }
                }
                catch (Exception e) { restorationFailure ??= e; }
                try { if (changedIndirect) RestoreIndirect(main); } catch (Exception e) { restorationFailure ??= e; }
                globals.Restore();
                if (restorationFailure != null) throw new CaptureRestoreException("恢复主画面裁剪／渲染状态失败；副相机停止", restorationFailure);
            }
            HeadCameraApi.ProjectMarker(camera);
            try { presentation.Update(texture, Plugin.OutputMode.Value); }
            catch
            {
                diagnostics.Observe(texture, null, camera, target, main, outputFailed: true, controlled: controlView.HasValue, captureState: captureState);
                throw;
            }
            if (logFrame) Plugin.Instance.Log.LogInfo("[摄像头阶段] 拍摄 END " + captureState + "；输出已提交，像素照明仍需实测");
            diagnostics.Observe(texture, presentation.Texture, camera, target, main, controlled: controlView.HasValue, captureState: captureState);
        }

        internal void Callback(int phase)
        {
            if (!renderActive || disposed || !initialized || callbackError != null) return;
            try
            {
                if (phase == 0)
                {
                    callbackPhases |= 1;
                    lighting.OnPreCull(); lighting.CollectCommands(beforeGBuffer, beforeLighting); instancing.OnPreCull();
                    if (Plugin.ProjectionFix.Value)
                    {
                        // FPSCamera normally supplies these separately from clustered CollectCommands.
                        // Never invoke its gameplay/flashlight/UI callbacks for a second viewpoint.
                        var projection = RenderUtils.GetProjectionParams(camera);
                        var depth = RenderUtils.GetZBufferParams(camera);
                        beforeGBuffer.SetGlobalVector(ClusteredRendering.ID_ProjectionParams, projection);
                        beforeGBuffer.SetGlobalVector(ClusteredRendering.ID_ZBufferParams, depth);
                        beforeLighting.SetGlobalVector(ClusteredRendering.ID_ProjectionParams, projection);
                        beforeLighting.SetGlobalVector(ClusteredRendering.ID_ZBufferParams, depth);
                    }
                }
                else if (phase == 1) { callbackPhases |= 2; transparencyActive = true; lighting.OnPreRender(); }
                else if (phase == 2) { lighting.OnPostRender(); transparencyActive = false; }
            }
            catch (Exception error)
            {
                // Unity logs/swallowing a callback exception must not disguise a failed
                // capture as success. Propagate it after Render returns through the circuit.
                callbackError = error; camera.cullingMask = 0;
            }
        }

        static void DrawIndirect(Camera view)
        {
            var instances = IndirectBatcher.Instances;
            if (instances == null) return;
            for (int i = 0; i < instances.Count; i++)
            {
                var batch = instances[i];
                if (batch == null || !batch.isActiveAndEnabled) continue;
                // Main-camera occlusion depth is not valid for the other viewpoint.
                batch.ResetCull(); batch.Draw(view);
            }
        }
        static void RestoreIndirect(Camera main)
        {
            var instances = IndirectBatcher.Instances;
            if (instances == null) return;
            var nativeCamera = main.GetComponent<IndirectBatcherCamera>();
            for (int i = 0; i < instances.Count; i++)
            {
                var batch = instances[i];
                if (batch == null || !batch.isActiveAndEnabled) continue;
                if (C_CullingManager.CullingEnabled && nativeCamera != null && nativeCamera.m_cull) batch.Cull(main);
                else batch.ResetCull();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; initialized = false;
            foreach (var identity in hudCameraIdentities) HeadCameraApi.UnregisterHudCamera(identity.handle, identity.instance);
            hudCameraIdentities.Clear();
            using var globals = new RenderGlobals();
            Exception first = null;
            void Clean(Action action) { try { action(); } catch (Exception e) { first ??= e; } }
            if (camera != null)
            {
                Clean(() =>
                {
                    var cache = RenderPipe.s_cameraData;
                    if (cache == null || !cache.TryGetValue(camera, out var data)) return;
                    if (data.cmdsAdded) RenderPipe.OnCameraPostRender(camera);
                    if (data.frostingBuffer != null) { data.frostingBuffer.Release(); UnityEngine.Object.DestroyImmediate(data.frostingBuffer); }
                    cache.Remove(camera);
                });
                Clean(() => camera.RemoveAllCommandBuffers());
                Clean(() => camera.targetTexture = null);
            }
            Clean(() => beforeGBuffer?.Dispose()); Clean(() => beforeLighting?.Dispose());
            Clean(presentation.Dispose);
            long fogIdentity = fog?.Pointer.ToInt64() ?? 0;
            long lightingIdentity = lighting?.Pointer.ToInt64() ?? 0;
            Clean(() => { if (lighting != null) GuiManager.UnregisterOnResolutionChangeHandler(lighting.Cast<UI_IOnResolutionChange>()); });
            var freed = new HashSet<long>();
            // Never dispose a buffer while a global shader slot still points to it.
            globals.Restore();
            // Only owned instances are examined; no scanning of assemblies or main-renderer fields.
            ReleaseGpu(lighting, freed, Clean); ReleaseGpu(collector, freed, Clean); ReleaseGpu(fog, freed, Clean);
            // Immediate destruction is limited to our own hierarchy, inside the globals scope.
            // Deferred native OnDestroy would run later outside that scope.
            int fogSpheresBefore = PreLitVolume.s_activeFogSpheres?.Count ?? 0;
            Clean(() => ownedFog.Destroy(fogIdentity, () => { if (root != null) UnityEngine.Object.DestroyImmediate(root); }, () => root == null));
            int fogSpheresAfter = PreLitVolume.s_activeFogSpheres?.Count ?? 0;
            Plugin.Instance?.Log.LogInfo("[摄像头清理] 全局雾球=" + fogSpheresBefore + "→" + fogSpheresAfter + "；rootDestroyed=" + (root == null));
            if (fogSpheresAfter < fogSpheresBefore) Plugin.Instance?.Log.LogWarning("[摄像头清理] 雾球登记减少，需核对原生回调和其他MOD清理日志");
            if (root == null) ownedClusters.Remove(lightingIdentity);
            Clean(() => { if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); } });
            foreach (var asset in ownedAssets) Clean(() => { if (asset != null) UnityEngine.Object.DestroyImmediate(asset); });
            ownedAssets.Clear(); root = null; camera = null; culler = null; lighting = null; collector = null; fog = null;
            callbacks = null;
            texture = null; beforeGBuffer = beforeLighting = null;
            if (first != null) Plugin.Instance?.Log.LogWarning("[头戴摄像头] 资源释放异常（其余释放阶段已执行）：" + first);
        }

        static void ReleaseGpu(object owner, HashSet<long> freed, Action<Action> clean)
        {
            if (owner == null) return;
            Type type = owner.GetType();
            if (!gpuProperties.TryGetValue(type, out var properties))
            {
                var selected = new List<PropertyInfo>();
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
                        && (property.PropertyType == typeof(RenderTexture) || property.PropertyType == typeof(ComputeBuffer)
                            || property.PropertyType == typeof(CommandBuffer) || property.PropertyType == typeof(Material))) selected.Add(property);
                gpuProperties[type] = properties = selected.ToArray();
            }
            foreach (var property in properties) clean(() =>
            {
                var value = property.GetValue(owner);
                property.SetValue(owner, null);
                if (value is RenderTexture rt && rt != null && freed.Add(rt.Pointer.ToInt64())) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                else if (value is ComputeBuffer cb && freed.Add(cb.Pointer.ToInt64())) cb.Dispose();
                else if (value is CommandBuffer cmd && freed.Add(cmd.Pointer.ToInt64())) cmd.Dispose();
                else if (value is Material material && material != null && freed.Add(material.Pointer.ToInt64())) UnityEngine.Object.DestroyImmediate(material);
            });
        }
    }

    internal sealed class RenderGlobals : IDisposable
    {
        public readonly C_Camera Culler = C_Camera.Current;
        public readonly PreLitVolume Fog = PreLitVolume.Current;
        readonly ClusteredRendering lighting = ClusteredRendering.Current;
        internal readonly bool UpdateSceneShadows = ClusteredRendering.Current?.UpdateShadows == true;
        readonly LightBufferBindings lightBuffers = new(ClusteredRendering.Current);
        readonly FogShaderState fogShader = new();
        readonly Vector3 cameraPosition = CameraManager.CullingPosition, cameraDirection = CameraManager.CullingDirection;
        readonly float cameraFov = CameraManager.CullingFov;
        readonly Vector3 position = C_Camera.Position, forward = C_Camera.Forward, right = C_Camera.Right, up = C_Camera.Up;
        readonly bool earlyTransparent = EarlyTransparentRenderer.Enable;
        readonly bool elevatorMask = C_Camera.RenderWithElevatorMask;
        readonly eDimensionIndex dimension = IRF.InstancedRenderFeatureCamera.DimensionIndex;
        readonly bool rainNoise = PreLitVolume.RainNoiseEnabled, rainNoiseWasEnabled = PreLitVolume.s_rainNoiseWasEnabled;
        readonly Vector4 projection = Shader.GetGlobalVector(ClusteredRendering.ID_ProjectionParams);
        readonly Vector4 depth = Shader.GetGlobalVector(ClusteredRendering.ID_ZBufferParams);
        public void Restore()
        {
            C_Camera.Current = Culler; ClusteredRendering.Current = lighting; PreLitVolume.Current = Fog;
            lightBuffers.Restore();
            CameraManager.CullingPosition = cameraPosition; CameraManager.CullingDirection = cameraDirection; CameraManager.CullingFov = cameraFov;
            C_Camera.Position = position; C_Camera.Forward = forward; C_Camera.Right = right; C_Camera.Up = up;
            EarlyTransparentRenderer.Enable = earlyTransparent;
            C_Camera.RenderWithElevatorMask = elevatorMask;
            IRF.InstancedRenderFeatureCamera.DimensionIndex = dimension;
            PreLitVolume.RainNoiseEnabled = rainNoise; PreLitVolume.s_rainNoiseWasEnabled = rainNoiseWasEnabled;
            Shader.SetGlobalVector(ClusteredRendering.ID_ProjectionParams, projection);
            Shader.SetGlobalVector(ClusteredRendering.ID_ZBufferParams, depth);
            fogShader.Restore();
        }
        public void Dispose() => Restore();
    }
    internal sealed class CaptureRestoreException : Exception
    { public CaptureRestoreException(string message, Exception inner) : base(message, inner) { } }

    public sealed class CameraRenderCallbacks : MonoBehaviour
    {
        public CameraRenderCallbacks(IntPtr pointer) : base(pointer) { }
        internal NativeCapture Owner;
        public void OnPreCull() => Owner?.Callback(0);
        public void OnPreRender() => Owner?.Callback(1);
        public void OnPostRender() => Owner?.Callback(2);
    }
}
