using System;
using CullingSystem;
using Player;
using UnityEngine;
namespace PreLuna.HeadCamera
{
    internal sealed class BotLightViewScope : IDisposable
    {
        readonly C_LightDynamic culler, bodyCuller;
        readonly CL_SpotLight spot, bodySpot;
        readonly bool wasCullerFps, wasSpotFps, wasBodyFps, wasBodyCullerFps, wasForced, wasBodyForced, borrowedOn;
        readonly CL_ShadowGenerator forcedShadow;
        readonly int fpsIndex;
        bool disposed;
        internal static BotLightViewScope For(PlayerAgent target) => For(target, out _);
        internal static BotLightViewScope For(PlayerAgent target, out string reason)
        {
            try {
                if (!NativeBotLamp.Resolve(target, out var inventory, out var culler, out var spot, out reason)) return null;
                if (!inventory.WantsFlashlightEnabled || !inventory.FlashlightEnabled) { reason = "未收到开灯指令"; return null; }
                var body = PlayerManager.GetLocalPlayerAgent()?.Inventory?.m_flashlightCLight;
                if (body?.m_clusterLight?.TryCast<CL_SpotLight>()?.ShadowGenerator == null) body = null;
                // A native temporary shadow disable is not a request to turn the lamp off.
                reason = spot.m_isOn ? "尝试原生FPS灯流程" : "开灯指令有效，尝试恢复被渲染器禁用的灯";
                return new BotLightViewScope(culler, spot, body);
            } catch (Exception e) { reason = "FPS灯作用域失败：" + e.GetBaseException().Message; return null; }
        }
        internal BotLightViewScope(C_LightDynamic culler, CL_SpotLight spot, C_LightDynamic body)
        {
            this.culler = culler; this.spot = spot;
            bodyCuller = body?.Pointer == culler.Pointer ? null : body;
            bodySpot = bodyCuller?.m_clusterLight?.TryCast<CL_SpotLight>();
            wasCullerFps = culler.IsFPSPlayerLight; wasSpotFps = spot.IsFPSFlashlight;
            wasForced = spot.ShadowGenerator.IsForced;
            wasBodyFps = bodySpot?.IsFPSFlashlight == true;
            wasBodyCullerFps = bodyCuller?.IsFPSPlayerLight == true;
            wasBodyForced = bodySpot?.ShadowGenerator?.IsForced == true;
            forcedShadow = CL_ShadowAtlas.ForcedShadowRender;
            fpsIndex = Shader.GetGlobalInt(ClusteredRendering._FPSSpotIndex);
            borrowedOn = !spot.m_isOn;
            try {
                if (bodySpot != null) { bodyCuller.IsFPSPlayerLight = false; bodySpot.IsFPSFlashlight = false; }
                // Native property also updates ShadowGenerator.IsForced; the raw field does not.
                culler.IsFPSPlayerLight = true; spot.IsFPSFlashlight = true;
                if (borrowedOn) culler.SetOn(true);
                BeforeRender();
            } catch { Dispose(); throw; }
        }
        internal void BeforeRender() { if (!disposed) CL_ShadowAtlas.ForcedShadowRender = spot.ShadowGenerator; }
        internal string Describe() => "renderOn=" + spot.m_isOn + " fps=" + spot.IsFPSFlashlight + " forced=" + spot.ShadowGenerator.IsForced
            + " allocated=" + spot.ShadowGenerator.Allocated + " borrowedOn=" + borrowedOn
            + " fpsIndex=" + Shader.GetGlobalInt(ClusteredRendering._FPSSpotIndex);
        internal string Pose => "lampPos=" + spot.UnityLight.transform.position + " lampDir=" + spot.UnityLight.transform.forward;
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            try { culler.IsFPSPlayerLight = wasCullerFps; }
            finally { try { spot.IsFPSFlashlight = wasSpotFps; }
                finally { try { spot.ShadowGenerator.IsForced = wasForced; }
                    finally { try { if (bodySpot != null) { bodyCuller.IsFPSPlayerLight = wasBodyCullerFps; } }
                        finally { try { if (bodySpot != null) bodySpot.IsFPSFlashlight = wasBodyFps; }
                            finally { try { if (bodySpot != null) bodySpot.ShadowGenerator.IsForced = wasBodyForced; }
                                finally { try { if (borrowedOn) culler.SetOn(false); }
                                    finally { try { CL_ShadowAtlas.ForcedShadowRender = forcedShadow; }
                                        finally { Shader.SetGlobalInt(ClusteredRendering._FPSSpotIndex, fpsIndex); } } } } } } } }
        }
    }
}
