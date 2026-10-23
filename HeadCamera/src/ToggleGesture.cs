using System;
namespace PreLuna.HeadCamera
{
    public enum ToggleIntent { None, Open, Close, RequestControl, ExitControl }
    public sealed class ToggleGesture
    {
        bool pending, closing;
        float pressedAt;
        CameraTarget? target;
        public CameraTarget? RequestTarget { get; private set; }
        public void Reset() { pending = false; target = RequestTarget = null; }
        public ToggleIntent Step(float now, bool pressed, bool opened, bool controlling, CameraTarget? selected, float window)
        {
            RequestTarget = null;
            if (!float.IsFinite(now)) { Reset(); return ToggleIntent.None; }
            window = Math.Clamp(window, .18f, .6f);
            if (controlling) { Reset(); return pressed ? ToggleIntent.ExitControl : ToggleIntent.None; }
            if (pending && now < pressedAt) Reset();
            if (pending && now - pressedAt > window) {
                pending = false;
                // A late second press must not also open a new session in this frame.
                if (closing && opened) return ToggleIntent.Close;
            }
            if (!pressed) return ToggleIntent.None;
            if (pending) {
                pending = false; RequestTarget = target;
                return target.HasValue && selected.HasValue && target.Value.Same(selected.Value)
                    ? ToggleIntent.RequestControl : ToggleIntent.None;
            }
            pending = true; closing = opened; pressedAt = now; target = selected;
            return opened ? ToggleIntent.None : ToggleIntent.Open;
        }
        public void CaptureOpenedTarget(CameraTarget? value) { if (pending) target = value; }
    }
}
