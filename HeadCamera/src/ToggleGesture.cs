using System;
namespace PreLuna.HeadCamera
{
    public enum ToggleIntent { None, Open, Close, RequestControl, ExitControl, RejectControl }
    public sealed class ToggleGesture
    {
        bool pending, closing;
        float pressedAt;
        CameraTarget? target;
        public CameraTarget? RequestTarget { get; private set; }
        public string RejectionReason { get; private set; }
        public void Reset() { pending = false; target = RequestTarget = null; }
        public ToggleIntent Step(float now, bool pressed, bool opened, bool controlling, CameraTarget? selected, float window)
        {
            RequestTarget = null;
            RejectionReason = null;
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
                if (target.HasValue && selected.HasValue && target.Value.Same(selected.Value)) return ToggleIntent.RequestControl;
                RejectionReason = !target.HasValue || !selected.HasValue ? "没有可接管的观看对象，请先选择存活机器人"
                    : "双击期间观看对象已改变，请选定机器人后重新双击";
                return ToggleIntent.RejectControl;
            }
            pending = true; closing = opened; pressedAt = now; target = selected;
            return opened ? ToggleIntent.None : ToggleIntent.Open;
        }
        public void CaptureOpenedTarget(CameraTarget? value) { if (pending) target = value; }
    }
}
