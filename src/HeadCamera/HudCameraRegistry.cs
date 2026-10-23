using System.Collections.Generic;

namespace PreLuna.HeadCamera
{
    // Identity-only registry; no Unity objects retained. Partial initialization is also registered.
    internal sealed class HudCameraRegistry
    {
        readonly Dictionary<long, int> cameras = new();
        internal long Revision { get; private set; }
        internal void Register(long handle, int instance)
        { if (handle == 0 || instance == 0) return; cameras[handle] = instance; Revision++; }
        internal void Unregister(long handle, int instance)
        { if (Owns(handle, instance)) { cameras.Remove(handle); Revision++; } }
        internal bool Owns(long handle, int instance) => handle != 0 && instance != 0
            && cameras.TryGetValue(handle, out int existing) && existing == instance;
    }
    internal readonly record struct HeadHudIdentity(bool Ready, bool PreviewVisible, bool ControlsBody,
        bool ReplacesMainView, long SubjectAgent, int SubjectInstance, long SubjectOwner, int SubjectSlot);
    internal static class HudViewIdentityPolicy
    {
        internal static HeadHudIdentity Resolve(bool playing, bool opened, bool hudVisible, bool enabled,
            bool controls, bool enlarged, long agent, int instance, long owner, int slot)
        {
            bool visible = playing && opened && hudVisible && enabled;
            return new(true, visible, controls, controls || visible && enlarged, agent, instance, owner, slot);
        }
    }
}
