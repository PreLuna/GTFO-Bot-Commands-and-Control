using System;
using System.Collections.Generic;
namespace PreLuna.HeadCamera
{
    // Keep ownership visible to the native OnDestroy prefix until destruction
    // has actually finished. A failed destroy must not release the guard early.
    internal sealed class OwnedFogLifetime
    {
        readonly Dictionary<long, int> objects = new();
        internal void Register(long handle, int instance) => objects[handle] = instance;
        internal bool Contains(long handle, int instance) => objects.TryGetValue(handle, out var id) && id == instance;
        internal void Destroy(long handle, Action destroy, Func<bool> destroyed)
        {
            try { destroy(); }
            finally { if (destroyed()) objects.Remove(handle); }
        }
    }
}
