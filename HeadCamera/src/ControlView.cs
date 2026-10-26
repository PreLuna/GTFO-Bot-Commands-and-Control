using System;
using UnityEngine;
namespace PreLuna.HeadCamera
{
    // Illuminate is retained for API compatibility; lighting is owned by the native bot inventory.
    public readonly record struct ControlView(CameraTarget Target, Vector3 Position, Vector3 Direction, float FieldOfView, bool Enlarged, bool Suspended, bool Illuminate = false);
    public static class WindowLayoutPolicy
    {
        public static (float Width, float Height) ReferenceSize(float screenWidth, float screenHeight)
        {
            float scale = MathF.Sqrt(Math.Max(1, screenWidth) / 1920 * Math.Max(1, screenHeight) / 1080);
            return (screenWidth / scale, screenHeight / scale);
        }
        public static float SpectatorWidth(float screenWidth, float screenHeight) => Width(float.MaxValue, screenWidth, screenHeight);
        public static float Width(float requested, float screenWidth, float screenHeight)
        {
            var (w, h) = ReferenceSize(screenWidth, screenHeight);
            return Math.Max(80, Math.Min(requested, Math.Min(w - 32, (h - 90) * 16 / 9 + 12)));
        }
    }
}
