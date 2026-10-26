using System;
using UnityEngine;
namespace PreLuna.HeadCamera
{
    public readonly record struct ControlMarker(CameraTarget Target, Vector3 Position, string Label, bool CanStation);
    public static partial class HeadCameraApi
    {
        static Func<ControlMarker?> markerProvider;
        internal static Vector3 MarkerViewport;
        internal static bool MarkerProjected;
        public static bool RegisterControlMarker(object owner, Func<ControlMarker?> provider)
        {
            if (owner == null || !ReferenceEquals(owner, inputOwner) || CameraRuntime.Current?.OnMainThread != true || provider == null) return false;
            markerProvider = provider; return true;
        }
        public static bool ReportControlFeedback(object owner, string text)
        {
            if (owner == null || !ReferenceEquals(owner, inputOwner) || CameraRuntime.Current?.OnMainThread != true || string.IsNullOrWhiteSpace(text)) return false;
            CameraRuntime.Current.ShowControlFeedback(text); return true;
        }
        internal static bool TryMarker(out ControlMarker marker)
        {
            marker = default;
            if (!Busy || !IsVisible || markerProvider == null || !TryGetSelected(out var selected)) return false;
            var value = markerProvider();
            if (!value.HasValue || !value.Value.Target.Same(selected)) return false;
            marker = value.Value; return float.IsFinite(marker.Position.x) && float.IsFinite(marker.Position.y) && float.IsFinite(marker.Position.z);
        }
        internal static void ProjectMarker(Camera camera)
        {
            MarkerProjected = TryMarker(out var marker);
            if (MarkerProjected) MarkerViewport = camera.WorldToViewportPoint(marker.Position);
        }
        internal static void ClearPresentation(bool unregister = false) { if (unregister) markerProvider = null; MarkerProjected = false; }
    }
}
