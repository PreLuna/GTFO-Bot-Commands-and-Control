namespace PreLuna.Captain.Tasks
{
    internal enum SearchTargetKind { Supplies, Terminals, Containers, Interactions }
    internal enum SearchDoorState { Closed, Unlocked, Open, Opening, Destroyed, WeakLocked, Blocked }
    // A shared boundary for item searches and future terminal/interaction searches.
    internal static class SearchAccessPolicy
    {
        public static bool Door(bool traversable, bool progressionLocked, bool hasDoor, bool ordinary,
            bool needsKey, bool checkpoint, SearchDoorState state, bool knownWeakLock, bool unsupportedLock)
        {
            if (progressionLocked) return false;
            if (!hasDoor) return traversable;
            if (state is SearchDoorState.Open or SearchDoorState.Destroyed) return traversable;
            if (!ordinary || needsKey || checkpoint || unsupportedLock) return false;
            return state is SearchDoorState.Closed or SearchDoorState.Unlocked
                || state == SearchDoorState.WeakLocked && knownWeakLock;
        }
        public static bool InScope(long zone, long room, long originZone, long originRoom, bool roomOnly)
            => zone != 0 && zone == originZone && (!roomOnly || room != 0 && room == originRoom);
    }
}

