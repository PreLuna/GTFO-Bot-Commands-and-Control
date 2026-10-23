using System;
using System.Collections.Generic;
using HarmonyLib;
using LevelGeneration;
namespace PreLuna.Captain.Tasks
{
    internal static class ItemWorldIndex
    {
        static readonly List<ItemInLevel> items = new();
        static readonly Dictionary<IntPtr, int> positions = new();
        static bool initialized;
        public static IReadOnlyList<ItemInLevel> Items
        {
            get
            {
                if (!initialized)
                {
                    foreach (var value in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<ItemInLevel>()))
                    {
                        var item = value.TryCast<ItemInLevel>();
                        if (ItemRegion.TryResolve(item, out _)) Added(item);
                    }
                    initialized = true;
                }
                return items;
            }
        }
        public static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ItemInLevel), "Setup", new[] { typeof(GameData.ItemDataBlock) }),
                postfix: new HarmonyMethod(typeof(ItemWorldIndex), nameof(Added)));
            harmony.Patch(AccessTools.Method(typeof(ItemInLevel), "OnDespawn"),
                prefix: new HarmonyMethod(typeof(ItemWorldIndex), nameof(Removed)));
            harmony.Patch(AccessTools.Method(typeof(ItemInLevel), "OnPlacedInLevel"),
                postfix: new HarmonyMethod(typeof(ItemWorldIndex), nameof(Added)));
            // Carry overrides setup and has its own placed/picked-up state callback.
            harmony.Patch(AccessTools.Method(typeof(CarryItemPickup_Core), "Setup", new[] { typeof(GameData.ItemDataBlock) }),
                postfix: new HarmonyMethod(typeof(ItemWorldIndex), nameof(Added)));
            harmony.Patch(AccessTools.Method(typeof(CarryItemPickup_Core), "OnSyncStateChange"),
                postfix: new HarmonyMethod(typeof(ItemWorldIndex), nameof(Added)));
            harmony.Patch(AccessTools.Method(typeof(LG_ResourceContainer_Storage), "EnablePickupInteractions"),
                postfix: new HarmonyMethod(typeof(ItemWorldIndex), nameof(ContainerOpened)));
        }
        public static void Register(ItemInLevel item) => Added(item);
        static void ContainerOpened(LG_ResourceContainer_Storage __instance)
        {
            if (__instance == null) return;
            foreach (var item in __instance.GetComponentsInChildren<ItemInLevel>(true)) Added(item);
        }
        static void Added(ItemInLevel __instance)
        {
            if (__instance == null || positions.ContainsKey(__instance.Pointer)) return;
            positions[__instance.Pointer] = items.Count; items.Add(__instance);
        }
        static void Removed(ItemInLevel __instance)
        {
            if (__instance == null || !positions.Remove(__instance.Pointer, out int index)) return;
            int last = items.Count - 1;
            if (index != last) { items[index] = items[last]; positions[items[index].Pointer] = index; }
            items.RemoveAt(last); AutoPickupAdapter.Forget(__instance.Pointer);
        }
        public static void Reset() { items.Clear(); positions.Clear(); initialized = false; }
    }
}
