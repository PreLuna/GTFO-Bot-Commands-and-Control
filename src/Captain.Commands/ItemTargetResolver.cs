using LevelGeneration;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class ItemTargetResolver
    {
        public static bool Ray(PlayerAgent source, out RaycastHit hit)
            => Ray(source, source?.EyePosition ?? default, source?.TargetLookDir ?? default, out hit);
        internal static bool Ray(PlayerAgent source, Vector3 origin, Vector3 direction, out RaycastHit hit, int extraMask = 0)
        {
            hit = default; if (source == null) return false;
            int mask = LayerManager.MASK_PLAYER_INTERACT_SPHERE | LayerManager.MASK_PLAYER_INTERACT_BLOCKERS | LayerManager.MASK_DEFAULT;
            if (source.Interaction != null) mask |= source.Interaction.m_pickupMask | source.Interaction.m_interactionMask;
            mask |= extraMask;
            return Physics.Raycast(origin, direction, out hit, 80, mask, QueryTriggerInteraction.Collide);
        }
        static ItemInLevel World(Item value) => value?.TryCast<ItemInLevel>();
        static ItemInLevel From(Component component)
        {
            if (component == null) return null;
            return component.GetComponentInParent<ItemInLevel>() ?? World(component.GetComponentInParent<Item>())
                ?? World(component.GetComponentInParent<LG_PickupItem_Sync>()?.item);
        }
        static bool Usable(ItemInLevel item) => item?.ItemDataBlock != null && item.gameObject.activeInHierarchy
            && SpecialItemAccess.Placed(item);
        static ItemInLevel Selected(PlayerAgent source)
        {
            var selected = source?.Interaction?.m_bestSelectedInteract?.TryCast<Interact_Base>();
            if (source?.Interaction?.m_currentSelected != true || selected == null || !selected.IsSelected) return null;
            var item = From(selected) ?? From(selected.m_colliderToOwn) ?? From(selected.Root);
            if (item == null && selected.Root != null)
                foreach (var child in selected.Root.GetComponentsInChildren<ItemInLevel>(true))
                    if (child.PickupInteraction?.Pointer == selected.Pointer) { item = child; break; }
            if (item == null)
                foreach (var candidate in ItemWorldIndex.Items)
                    if (candidate?.PickupInteraction?.Pointer == selected.Pointer) { item = candidate; break; }
            if (!Usable(item) || item.PickupInteraction?.Pointer != selected.Pointer
                || Vector3.Distance(source.EyePosition, ItemInteractionGeometry.Point(item)) > 5) return null;
            ItemWorldIndex.Register(item); return item;
        }
        public static ItemInLevel Target(PlayerAgent source)
        {
            if (source == null) return null;
            var selected = Selected(source);
            if (selected != null) return selected;
            if (Ray(source, out var hit))
            {
                var direct = Resolve(source, hit);
                if (Usable(direct) && ItemInteractionGeometry.Visible(source.EyePosition, direct)) return direct;
            }
            ItemInLevel best = null; float rank = float.PositiveInfinity;
            var aim = source.TargetLookDir.normalized;
            foreach (var item in ItemWorldIndex.Items)
            {
                if (!Usable(item) || !AutoPickupAdapter.Open(item)) continue;
                var delta = ItemInteractionGeometry.Point(item) - source.EyePosition;
                float along = Vector3.Dot(delta, aim);
                if (along < 0 || along > 80) continue;
                float miss = (delta - aim * along).sqrMagnitude;
                if (miss > .3f * .3f || !ItemInteractionGeometry.Visible(source.EyePosition, item)) continue;
                float candidate = miss * 100 + along * .001f;
                if (candidate < rank) { best = item; rank = candidate; }
            }
            return best;
        }
        public static ItemInLevel Resolve(PlayerAgent source, RaycastHit hit)
        {
            var direct = From(hit.collider);
            if (direct != null) { ItemWorldIndex.Register(direct); return direct; }
            foreach (var item in ItemWorldIndex.Items)
                if (ItemInteractionGeometry.Owns(item, hit.collider)) return item;
            return null;
        }
    }
}
