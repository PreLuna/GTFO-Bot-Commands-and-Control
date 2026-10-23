using System;
using UnityEngine;
namespace PreLuna.HeadCamera
{
    public readonly record struct ControlView(CameraTarget Target, Vector3 Position, Vector3 Direction, float FieldOfView, bool Enlarged, bool Suspended);
    public static class WindowLayoutPolicy
    {
        public static float Width(float requested, float screenWidth, float screenHeight)
        {
            float scale = MathF.Sqrt(Math.Max(1, screenWidth) / 1920 * Math.Max(1, screenHeight) / 1080);
            float w = screenWidth / scale, h = screenHeight / scale;
            return Math.Max(80, Math.Min(requested, Math.Min(w - 32, (h - 90) * 16 / 9 + 12)));
        }
    }
}
