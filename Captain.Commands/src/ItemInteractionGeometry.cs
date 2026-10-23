using System;
using AIGraph;
using LevelGeneration;
using Player;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal static class ItemInteractionGeometry
    {
        public static Vector3 Point(ItemInLevel item)
        {
            var interact = item?.PickupInteraction;
            if (interact?.m_colliderToOwn != null) return interact.m_colliderToOwn.bounds.center;
            return interact?.Root != null ? interact.Root.position : item.transform.position;
        }
        public static bool Owns(ItemInLevel item, Collider collider)
        {
            if (item == null || collider == null) return false;
            if (collider.GetComponentInParent<ItemInLevel>()?.Pointer == item.Pointer) return true;
            var interact = item.PickupInteraction;
            if (interact?.m_colliderToOwn?.Pointer == collider.Pointer) return true;
            var root = interact?.Root;
            return root != null && (root.Pointer == collider.transform.Pointer || collider.transform.IsChildOf(root));
        }
        public static bool Visible(Vector3 eye, ItemInLevel item)
        {
            var delta = Point(item) - eye;
            if (delta.sqrMagnitude < .0025f) return true;
            return !Physics.Raycast(eye, delta.normalized, out var hit, Math.Max(0, delta.magnitude - .05f),
                LayerManager.MASK_PLAYER_INTERACT_BLOCKERS, QueryTriggerInteraction.Ignore) || Owns(item, hit.collider);
        }
        public static bool Standing(PlayerAgent agent, Vector3 point, ItemInLevel item, out Vector3 standing)
            => Standing(agent, point, item, out standing, out _);
        public static bool Standing(PlayerAgent agent, Vector3 point, ItemInLevel item, out Vector3 standing, out string reason)
        {
            standing = default; reason = null;
            int nav = 0, range = 0, region = 0, occupied = 0, occluded = 0;
            var approach = agent.Position - point; approach.y = 0;
            if (approach.sqrMagnitude < .01f) { approach = -agent.TargetLookDir; approach.y = 0; }
            if (approach.sqrMagnitude < .01f) approach = Vector3.forward;
            approach = approach.normalized; var side = Vector3.Cross(Vector3.up, approach);
            for (int ring = 0; ring < 2; ring++)
            for (int direction = 0; direction < 8; direction++)
            {
                var offset = direction == 0 ? approach : direction == 1 ? side : direction == 2 ? -side : direction == 3 ? -approach
                    : direction == 4 ? (approach + side).normalized : direction == 5 ? (approach - side).normalized
                    : direction == 6 ? (-approach + side).normalized : (-approach - side).normalized;
                var desired = point + offset * (ring == 0 ? 1.5f : 1f);
                if (!PlayerAIBot.SnapPositionToNav(desired, out var candidate, 2, 8)) { nav++; continue; }
                if (Math.Abs(candidate.x - desired.x) > .75f || Math.Abs(candidate.z - desired.z) > .75f || Math.Abs(candidate.y - desired.y) > 2.1f
                    || !ItemArrivalPolicy.Interactable(candidate.x - point.x, candidate.y - point.y, candidate.z - point.z, false, ItemTaskAdapter.PickupReach)) { range++; continue; }
                if (!AIG_CourseNode.TryGetCourseNode(agent.DimensionIndex, candidate, 1, out var node)
                    || node?.m_zone?.Layer == null || agent.CourseNode?.m_zone?.Layer == null
                    || node.m_zone.Layer.Pointer != agent.CourseNode.m_zone.Layer.Pointer) { region++; continue; }
                if (Physics.CheckCapsule(candidate + Vector3.up * .35f, candidate + Vector3.up * 1.4f, .25f,
                    LayerManager.MASK_DEFAULT, QueryTriggerInteraction.Ignore)) { occupied++; continue; }
                if (item != null && !Visible(candidate + (agent.EyePosition - agent.Position), item)) { occluded++; continue; }
                standing = candidate; return true;
            }
            reason = "16个候选站位均拒绝：导航贴合=" + nav + "，距离／高度=" + range + "，区域／分层=" + region
                + "，身体碰撞=" + occupied + "，视线遮挡=" + occluded;
            return false;
        }
    }
}
