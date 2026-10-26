using System;
using UnityEngine;

namespace PreLuna.HeadCamera
{
    internal static class FirstPersonCapture
    {
        internal static float Aspect(bool controlled, float nativeAspect)
            => controlled && float.IsFinite(nativeAspect) && nativeAspect > .1f && nativeAspect < 10 ? nativeAspect : 16f / 9;
        internal static int Height(int width, float aspect) => Math.Max(32, (int)Math.Round(width / aspect));
        internal static float NearRadius(float near, float fov, float aspect)
        {
            float halfHeight = near * MathF.Tan(fov * MathF.PI / 360);
            return MathF.Sqrt(near * near + halfHeight * halfHeight * (1 + aspect * aspect));
        }
        // Protect the whole near plane, including its corners, without pushing the eye through geometry.
        internal static float SafeNear(Vector3 eye, float nativeNear, float fov, float aspect)
        {
            float near = Math.Clamp(nativeNear, .01f, .3f);
            int mask = LayerManager.MASK_WORLD | LayerManager.MASK_PLAYER_INTERACT_BLOCKERS | (1 << LayerManager.LAYER_DYNAMIC);
            for (;;)
            {
                if (!Physics.CheckSphere(eye, NearRadius(near, fov, aspect) + .005f, mask, QueryTriggerInteraction.Ignore)) return near;
                if (near <= .01001f) return 0; // Ordinary occlusion; retry next capture, not the failure circuit.
                near = Math.Max(.01f, near * .5f);
            }
        }
    }
}
