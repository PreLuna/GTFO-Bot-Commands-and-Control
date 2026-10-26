using System;
using System.Collections.Generic;
using CullingSystem;
using Player;
using UnityEngine;

namespace PreLuna.HeadCamera
{
    // Synchronous render-only changes: never activate objects, change colliders or relocate a player.
    internal sealed class FirstPersonRenderScope : IDisposable
    {
        readonly List<(Renderer renderer, bool hidden)> meshes = new();
        readonly HashSet<long> seen = new();
        Transform lamp;
        Vector3 lampPosition;
        Quaternion lampRotation;
        C_LightDynamic lampCuller;
        CL_SpotLight lampSpot;
        bool disposed;
        internal FirstPersonRenderScope(PlayerAgent target, Camera camera)
        {
            try
            {
                var model = target.PlayerSyncModel;
                if (model == null) throw new InvalidOperationException("机器人第一人称模型尚未就绪");
                foreach (var parts in new[] { model.m_gfxHead, model.m_gfxTorso, model.m_gfxArms })
                {
                    if (parts == null) continue;
                    foreach (var part in parts)
                    {
                        if (part == null) continue;
                        foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
                        {
                            if (renderer == null || !seen.Add(renderer.Pointer.ToInt64())) continue;
                            if (renderer.TryCast<MeshRenderer>() == null && renderer.TryCast<SkinnedMeshRenderer>() == null) continue;
                            meshes.Add((renderer, renderer.forceRenderingOff));
                            renderer.forceRenderingOff = true;
                        }
                    }
                }
                if (NativeBotLamp.Resolve(target, out var inventory, out var culler, out var spot, out _)
                    && inventory.WantsFlashlightEnabled && inventory.FlashlightEnabled)
                {
                    var offset = inventory.m_flashlightCameraOffset;
                    if (!float.IsFinite(offset.x) || !float.IsFinite(offset.y) || !float.IsFinite(offset.z) || offset.sqrMagnitude > 1)
                        throw new InvalidOperationException("原生第一人称手电偏移无效");
                    lamp = spot.UnityLight.transform; lampCuller = culler; lampSpot = spot;
                    lampPosition = lamp.localPosition; lampRotation = lamp.localRotation;
                    lamp.SetPositionAndRotation(camera.transform.position + camera.transform.rotation * offset, camera.transform.rotation);
                    RefreshLamp();
                }
            }
            catch
            {
                try { Dispose(); }
                catch (Exception error) { throw new CaptureRestoreException("第一人称准备失败且无法完整归还显示状态", error); }
                throw;
            }
        }
        // Only transform caches are refreshed here; light parameters and GPU upload remain native.
        void RefreshLamp() { lampCuller.UpdateTransformData(); lampSpot.UpdateTransform(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Exception failure = null;
            if (lamp != null)
            {
                try { lamp.localPosition = lampPosition; lamp.localRotation = lampRotation; RefreshLamp(); }
                catch (Exception error) { failure = error; }
            }
            for (int i = meshes.Count - 1; i >= 0; i--)
            {
                try { if (meshes[i].renderer != null) meshes[i].renderer.forceRenderingOff = meshes[i].hidden; }
                catch (Exception error) { failure ??= error; }
            }
            meshes.Clear();
            if (failure != null) throw new InvalidOperationException("第一人称模型／灯光姿态恢复失败", failure);
        }
    }
}
