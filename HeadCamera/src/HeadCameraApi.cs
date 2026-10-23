using System;
namespace PreLuna.HeadCamera
{
    // Public API v1 contains no Captain task classes, native player wrappers or inventory objects.
    // Methods must be called from Unity's main thread. No control/possession in this version.
    public static partial class HeadCameraApi
    {
        public const int ApiVersion = 1;
        public const int ControlInputVersion = 1;
        public const int ControlViewVersion = 1;
        public const int ControlAimVersion = 1;
        static ControlView? renderedControl;
        static float renderedAt;
        internal static void RecordControlAim(ControlView? value) { renderedControl = value; renderedAt = UnityEngine.Time.unscaledTime; NotifyHudViewIdentityChanged(); }
        public static bool TryGetControlAim(out ControlView view)
        {
            view = default;
            if (!Busy || !IsVisible || CameraRuntime.Current?.OnMainThread != true || !renderedControl.HasValue
                || UnityEngine.Time.unscaledTime < renderedAt || UnityEngine.Time.unscaledTime - renderedAt > .75f
                || !TryGetSelected(out var selected) || !selected.Same(renderedControl.Value.Target)) return false;
            view = renderedControl.Value; return !view.Suspended;
        }
        static Func<CameraTarget, ControlView?> viewProvider;
        internal static bool Enlarged { get; private set; }
        internal static bool HasViewProvider => viewProvider != null;
        public static void ClearControlView(object owner) { if (ReferenceEquals(owner, inputOwner)) { Enlarged = false; renderedControl = null; NotifyHudViewIdentityChanged(); } }
        public static float DefaultFieldOfView => Plugin.FieldOfView?.Value ?? 80;
        public static bool RegisterControlView(object owner, Func<CameraTarget, ControlView?> provider)
        {
            if (!ReferenceEquals(owner, inputOwner) || owner == null || provider == null || CameraRuntime.Current?.OnMainThread != true) return false;
            viewProvider = provider; return true;
        }
        internal static bool TryView(CameraTarget selected, out ControlView view)
        {
            view = default; Enlarged = false;
            if (!Busy || viewProvider == null) return false;
            var value = viewProvider(selected);
            if (!value.HasValue || !value.Value.Target.Same(selected)) return false;
            view = value.Value;
            static bool Finite(UnityEngine.Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
            if (!Finite(view.Position) || !Finite(view.Direction) || view.Direction.sqrMagnitude < .001f || !float.IsFinite(view.FieldOfView)) return false;
            Enlarged = view.Enlarged; return true;
        }
        static object inputOwner;
        internal static Func<CameraTarget, bool> RequestControl;
        internal static Func<bool> ControlBusy;
        internal static Action ExitControl;
        internal static Func<string> ControlStatus;
        internal static float DoubleClickWindow = .3f;
        public static bool RegisterControlInput(object owner, Func<CameraTarget, bool> request, Func<bool> busy,
            Action exit, Func<string> status, float doubleClickWindow)
        {
            if (owner == null || request == null || busy == null || exit == null || inputOwner != null
                || CameraRuntime.Current?.OnMainThread != true) return false;
            inputOwner = owner; RequestControl = request; ControlBusy = busy; ExitControl = exit; ControlStatus = status;
            DoubleClickWindow = Math.Clamp(doubleClickWindow, .18f, .6f); return true;
        }
        public static void UnregisterControlInput(object owner)
        {
            if (!ReferenceEquals(owner, inputOwner)) return;
            inputOwner = null; RequestControl = null; ControlBusy = null; ExitControl = null; ControlStatus = null;
            viewProvider = null; Enlarged = false;
            renderedControl = null;
            NotifyHudViewIdentityChanged();
        }
        internal static bool Busy => ControlBusy?.Invoke() == true;
        public static bool IsVisible => CameraRuntime.Current?.Opened == true;
        public static bool TryGetSelected(out CameraTarget target)
        {
            target = default;
            var current = CameraRuntime.Current;
            if (current == null || !current.OnMainThread || !current.Selected.HasValue) return false;
            target = current.Selected.Value;
            return true;
        }
        public static bool TrySelectSlot(int slot) => !Busy && CameraRuntime.Current?.SelectSlot(slot) == true;
        public static bool TrySetVisible(bool visible) => CameraRuntime.Current?.SetVisible(visible) == true;
    }
}
