using AIGraph;
using LevelGeneration;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class ItemRegion
    {
        static bool Valid(AIG_CourseNode node) => node?.m_zone?.Layer != null;
        // Placement is the current replicated location. originCourseNode can refer to
        // a previous room after an item is carried/dropped, so it is not a fallback.
        public static bool TryResolve(ItemInLevel item, out AIG_CourseNode node)
        {
            node = null;
            if (item == null) return false;
            var sync = item.GetSyncComponent()?.TryCast<LG_PickupItem_Sync>();
            if (!SpecialItemAccess.Placed(item)) return false;
            AIG_CourseNode current;
            if (sync != null) {
                var placement = sync.GetCurrentState().placement;
                if (placement.node.TryGet(out current) && Valid(current)) { node = current; return true; }
            }
            var container = item.container;
            current = container?.m_core?.SpawnNode;
            if (Valid(current)) { node = current; return true; }
            current = item.CourseNode;
            if (Valid(current)) { node = current; return true; }
            return false; // An unknown dimension must not be guessed from the bot's position.
        }
        public static bool SameSpace(PlayerAgent agent, AIG_CourseNode node) => agent?.CourseNode?.m_zone?.Layer != null && Valid(node)
            && node.m_zone.DimensionIndex == agent.DimensionIndex
            && node.m_zone.Layer.Pointer == agent.CourseNode.m_zone.Layer.Pointer;
        public static bool SameZone(PlayerAgent agent, ItemInLevel item) => TryResolve(item, out var node) && SameSpace(agent, node)
            && agent.CourseNode.m_zone.Pointer == node.m_zone.Pointer;
        public static string Describe(PlayerAgent agent, ItemInLevel item)
        {
            TryResolve(item, out var node);
            return "bot-dimension=" + agent?.DimensionIndex + " bot-zone=" + agent?.CourseNode?.m_zone?.Pointer
                + " item-zone=" + node?.m_zone?.Pointer + " item-dimension=" + node?.m_zone?.DimensionIndex
                + " field-node=" + item?.CourseNode?.Pointer + " item=" + item?.Pointer;
        }
    }
}
