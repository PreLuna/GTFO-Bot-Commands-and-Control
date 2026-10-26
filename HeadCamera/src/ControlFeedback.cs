using System;

namespace PreLuna.HeadCamera
{
    internal sealed class ControlFeedback
    {
        string message;
        float expires, last;
        internal int Revision { get; private set; }
        internal bool Store(string value, float now)
        {
            if (string.IsNullOrWhiteSpace(value) || !float.IsFinite(now)) return false;
            message = value.Length <= 400 ? value : value[..400];
            last = now; expires = now + 5; Revision++;
            return true;
        }
        internal string Read(float now)
        {
            if (!float.IsFinite(now) || now < last || now >= expires) { Clear(); return null; }
            last = now; return message;
        }
        internal void Clear() { message = null; expires = last = 0; }
        internal void Request(CameraTarget target, Func<CameraTarget, bool> callback, bool testPattern,
            Action<string> notify, Action<Exception> logError)
        {
            if (testPattern) { notify("当前为彩条显示测试，请将显示模式改回 ToneMapped 后接管"); return; }
            if (callback == null) { notify("接管入口未就绪：请检查微操大师是否加载，以及前置版本和 BepInEx 日志"); return; }
            int before = Revision;
            try
            {
                if (!callback(target) && before == Revision) notify("接管请求未被接受，请检查微操大师日志中的拒绝原因");
            }
            catch (Exception error)
            {
                logError(error);
                notify("接管请求异常：" + error.GetBaseException().Message + "；摄像头仍可观看");
            }
        }
    }
}
