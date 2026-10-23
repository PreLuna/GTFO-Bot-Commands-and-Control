using System;

namespace PreLuna.HeadCamera
{
    public static partial class HeadCameraApi
    {
        public const int HudIdentityVersion = 1;
        static readonly HudCameraRegistry hudCameras = new();
        static HeadHudIdentity lastHudIdentity;
        static long lastHudCameraRevision;
        static bool notifyingHud;
        static int hudMainThread;
        internal static void BindHudThread() => hudMainThread = Environment.CurrentManagedThreadId;
        public static event Action HudViewIdentityChanged;

        // Primitive-only optional API: consumers do not need a hard HeadCamera assembly reference.
        public static bool TryGetHudViewIdentity(out bool previewVisible, out bool controlsBody,
            out bool replacesMainView, out long subjectAgent, out int subjectInstance,
            out long subjectOwner, out int subjectSlot)
        {
            previewVisible = controlsBody = replacesMainView = false;
            subjectAgent = subjectOwner = 0; subjectInstance = 0; subjectSlot = -1;
            var runtime = CameraRuntime.Current;
            if (Environment.CurrentManagedThreadId != hudMainThread) return false;
            // The assembly can remain loaded after plugin shutdown. A known clean shutdown is inactive.
            if (runtime == null) return Plugin.Instance == null;
            try
            {
            if (!runtime.OnMainThread || !runtime.TryReadHudIdentity(out var identity)) return false;
            previewVisible = identity.PreviewVisible; controlsBody = identity.ControlsBody;
            replacesMainView = identity.ReplacesMainView; subjectAgent = identity.SubjectAgent;
            subjectInstance = identity.SubjectInstance; subjectOwner = identity.SubjectOwner; subjectSlot = identity.SubjectSlot;
            return identity.Ready;
            }
            catch { return false; }
        }
        public static bool IsHudCaptureCamera(long handle, int instance) =>
            CameraRuntime.Current?.OnMainThread == true && hudCameras.Owns(handle, instance);
        internal static void RegisterHudCamera(long handle, int instance)
        { hudCameras.Register(handle, instance); NotifyHudViewIdentityChanged(); }
        internal static void UnregisterHudCamera(long handle, int instance)
        { hudCameras.Unregister(handle, instance); NotifyHudViewIdentityChanged(); }
        internal static void NotifyHudViewIdentityChanged()
        {
            if (notifyingHud || CameraRuntime.Current?.OnMainThread != true) return;
            notifyingHud = true;
            try
            {
                CameraRuntime.Current.TryReadHudIdentity(out var current);
                if (current == lastHudIdentity && lastHudCameraRevision == hudCameras.Revision) return;
                lastHudIdentity = current; lastHudCameraRevision = hudCameras.Revision;
                var handlers = HudViewIdentityChanged;
                if (handlers == null) return;
                foreach (Action handler in handlers.GetInvocationList())
                    try { handler(); } catch (Exception error) { Plugin.Instance?.Log.LogWarning("HUD 视角订阅者异常：" + error.Message); }
            }
            catch (Exception error) { Plugin.Instance?.Log.LogWarning("HUD 视角身份读取失败：" + error.Message); }
            finally { notifyingHud = false; }
        }
    }
}
