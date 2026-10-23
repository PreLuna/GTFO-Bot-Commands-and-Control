using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using LevelGeneration;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    // DropItemFixed's slot type is internal. Keep its existing instance, placement and synchronization hooks.
    internal sealed class ContainerBridge
    {
        static FieldInfo registryField;
        static bool registryChecked;
        readonly object slot;
        public readonly IntPtr Key;
        public readonly LG_ResourceContainer_Storage Storage;
        readonly MethodInfo opened, inUse, transform;
        ContainerBridge(object value, LG_ResourceContainer_Storage storage)
        {
            slot = value; Storage = storage; Key = ((Component)value).Pointer;
            var t = value.GetType(); opened = AccessTools.Method(t, "IsContainerOpened"); inUse = AccessTools.Method(t, "GetIsSlotInUse"); transform = AccessTools.Method(t, "GetTransform");
        }
        static object Reference(object instance, string name)
        { var field = AccessTools.Field(instance.GetType(), name).GetValue(instance); return field.GetType().GetProperty("Value").GetValue(field); }
        public bool Available => ((Component)slot) != null && Storage != null && (bool)opened.Invoke(slot, null) && !(bool)inUse.Invoke(slot, null);
        public Transform Placement(InventorySlot inventorySlot) => (Transform)transform.Invoke(slot, new object[] { inventorySlot });
        public static bool Find(RaycastHit hit, InventorySlot inventorySlot, out ContainerBridge selected)
        {
            selected = null;
            var type = KnownTypes.Get("DropItem", "DropItem.Comps.ResourceContainerSlot");
            if (type != null && !registryChecked) {
                registryChecked = true;
                registryField = AccessTools.Field(type, "_CreatedSlots");
                if (registryField == null) { Plugin.Logger.LogWarning("放回容器接口缺失：DropItem.ResourceContainerSlot._CreatedSlots"); return false; }
            }
            var registry = registryField?.GetValue(null) as IDictionary;
            if (registry == null || hit.collider == null) return false;
            float distance = float.PositiveInfinity;
            foreach (DictionaryEntry entry in registry)
            {
                var component = entry.Value as Component; if (component == null) continue;
                var graphic = Reference(entry.Value, "Graphic") as LG_WeakResourceContainer_Graphics;
                var storage = Reference(entry.Value, "Storage") as LG_ResourceContainer_Storage;
                if (graphic == null || storage == null) continue;
                bool selectedContainer = hit.collider.transform.IsChildOf(graphic.transform)
                    || component.transform.parent != null && hit.collider.transform.IsChildOf(component.transform.parent);
                if (!selectedContainer) continue;
                var candidate = new ContainerBridge(entry.Value, storage);
                if (!candidate.Available) continue;
                var position = candidate.Placement(inventorySlot); if (position == null) continue;
                float rank = Vector3.Distance(position.position, hit.point);
                if (rank < distance) { distance = rank; selected = candidate; }
            }
            return selected != null;
        }
    }
}
