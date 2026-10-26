using System;
namespace PreLuna.MicroMaster
{
    public static class ControlSafety
    {
        // StandStill is a stationary body, not a terminal, ladder or grabbed state.
        public static bool BodyLocomotion(int state) => state is >= 0 and <= 5 or 17;
        public static bool HealthDropped(float before, float after) => float.IsFinite(before) && float.IsFinite(after) && after < before - .00001f;
        public static string Check(bool enabled, bool host, bool playing, bool focused, bool sameBody, bool alive, bool grabbed, bool normalLocomotion, bool manual)
            => !enabled ? "插件已关闭" : !host ? "房主身份改变" : !playing ? "关卡状态改变" : !sameBody ? "本体身份改变"
            : !alive ? "本体倒地" : grabbed ? "本体被抓" : !normalLocomotion ? "本体进入不可接管状态"
            : !focused ? "窗口失去焦点" : manual ? "手动紧急退出" : null;
        public static bool BlockAction(int action) => action is >= 1 and <= 14 or >= 23 and <= 32 or 45 or 46 or 47 or 58 or 65 or 66 or 67;
    }
}
