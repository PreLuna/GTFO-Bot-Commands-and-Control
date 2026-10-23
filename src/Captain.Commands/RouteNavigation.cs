using System;
using AIGraph;
using ChainedPuzzles;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal sealed record RouteDestination(AIG_CourseNode Node, Vector3 Position, AIG_CoursePortal Portal = null);
    internal sealed record RouteTarget(string Label, Func<RouteDestination> Resolve);
    // Local visual only. No bot task, scanner, chained-puzzle state, navigation
    // agent, colliders, network packets or gameplay callbacks are created here.
    internal static class RouteNavigation
    {
        static RouteTarget target; static RoutePlan plan; static AIG_CourseNode origin;
        static IntPtr floor, issuer; static float nextTick, lastPlan, nextRequest;
        static Vector3 plannedTarget; static bool scanHidden;
        static GameObject root; static LineRenderer line; static Material material;
        internal static bool HasState => target != null || root != null || material != null;
        internal static void Start(RouteTarget value)
        {
            if (Time.time < nextRequest) { ItemTaskAdapter.Feedback("导航操作间隔至少1秒"); return; }
            nextRequest = Time.time + 1;
            var player = PlayerManager.GetLocalPlayerAgent();
            if (value == null || !SNet.IsMaster || GameStateManager.CurrentStateName != eGameStateName.InLevel
                || Builder.CurrentFloor == null || player?.Owner == null || player.CourseNode?.m_zone?.Layer == null) {
                ItemTaskAdapter.Feedback("当前关卡／本地玩家不具备导航条件"); return;
            }
            var destination = value.Resolve();
            if (destination == null) { ItemTaskAdapter.Feedback("导航目标已经失效，请重新查询"); return; }
            // Replace only after a new request can be resolved; failed rendering
            // leaves no stale route labelled as the new target.
            Clear(); target = value; origin = player.CourseNode; floor = Builder.CurrentFloor.Pointer; issuer = player.Owner.Pointer;
            try { Rebuild(player, destination); }
            catch (Exception error) { Failure(error); }
        }
        internal static void Control(int handler)
        {
            if (handler == 34) { Reset(); ItemTaskAdapter.Feedback("已取消独立导航；机器人任务继续执行"); return; }
            if (target == null) { ItemTaskAdapter.Feedback("当前没有独立导航，请从已知结果选择目标"); return; }
            if (handler == 32) {
                ItemTaskAdapter.Feedback("独立导航：" + target.Label + (plan?.WaitingForDoor == true ? "；路线停在普通门前，请先开门" : "；沿青绿色路线前往")
                    + (scanHidden ? "；当前有扫描，自己的导航线暂时隐藏" : "") + "；范围限定启动时的Zone，不自动交互"); return;
            }
            if (Time.time < nextRequest) { ItemTaskAdapter.Feedback("导航操作间隔至少1秒"); return; }
            nextRequest = Time.time + 1;
            try {
                var player = PlayerManager.GetLocalPlayerAgent(); var destination = target.Resolve();
                if (!Session(player) || destination == null) { Stop("目标／导航会话已经失效"); return; }
                Rebuild(player, destination);
            } catch (Exception error) { Failure(error); }
        }
        static bool Session(PlayerAgent player) => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel
            && Builder.CurrentFloor?.Pointer == floor && player?.Owner?.Pointer == issuer
            && SearchTraversal.InScope(player?.CourseNode, origin, 1) && ItemRegion.SameSpace(player, origin);
        static bool Scanning()
        {
            // Use the existing registry and raw replicated fields, twice per second
            // only while navigating. Never enumerate scene objects or touch events.
            var instances = ChainedPuzzleManager.Current?.m_instances;
            if (instances == null) return true; // Uncertain visibility: hide our line.
            int count = 0;
            foreach (var puzzle in instances) {
                if (++count > 256) return true;
                if (puzzle?.m_stateReplicator == null) continue;
                var state = puzzle.m_stateReplicator.m_currentState;
                if (!state.isSolved && (state.isActive || state.status == eChainedPuzzleStatus.Active)
                    && (puzzle.m_sourceArea == null || SearchTraversal.InScope(puzzle.m_sourceArea.m_courseNode, origin, 1))) return true;
            }
            return false;
        }
        static void Visibility() { scanHidden = Scanning(); if (line != null) line.enabled = !scanHidden; }
        internal static void Tick()
        {
            if (target == null || Time.time < nextTick) return;
            nextTick = Time.time + .5f;
            try {
                var player = PlayerManager.GetLocalPlayerAgent();
                if (!Session(player)) { Stop("离开本次Zone／维度、切换玩家或关卡，导航已取消"); return; }
                var destination = target.Resolve();
                if (destination == null || !SearchTraversal.InScope(destination.Node, origin, 1) || !ItemRegion.SameSpace(player, destination.Node)) {
                    Stop("目标被拾取、失效或离开本Zone，导航已取消"); return;
                }
                bool changedDoor = plan?.Door != null && plan.WaitingForDoor && RouteTraversal.Open(plan.Door);
                bool unsafeRoute = false;
                if (plan?.Passages != null) foreach (var passage in plan.Passages)
                    if (!RouteTraversal.Open(passage)) { unsafeRoute = true; changedDoor = true; break; }
                if (unsafeRoute && line != null) line.enabled = false;
                // Following the cached line, including entering its next room, is
                // not a reason to run the entire native path search again.
                bool leftCorridor = plan?.Nodes != null && Array.IndexOf(plan.Nodes, player.CourseNode.Pointer.ToInt64()) < 0;
                if (RoutePolicy.Replan(Time.time, lastPlan, leftCorridor, changedDoor,
                    RouteTraversal.Deviation(player.Position, plan?.Points), Vector3.Distance(destination.Position, plannedTarget))) {
                    Rebuild(player, destination, false); if (target == null) return;
                }
                if (plan != null && Vector3.Distance(player.Position, plan.Points[^1]) <= 1.25f
                    && player.CourseNode.Pointer == plan.EndNode.Pointer && !plan.WaitingForDoor) {
                    Stop("已到达目标附近；请自行交互"); return;
                }
                if (!unsafeRoute) Visibility();
            } catch (Exception error) { Failure(error); }
        }
        static void Rebuild(PlayerAgent player, RouteDestination destination, bool notify = true)
        {
            if (line != null) line.enabled = false;
            var next = RouteTraversal.Build(player, destination.Node, destination.Position, destination.Portal, origin, out string reason);
            if (next == null) { Stop("导航未建立：" + reason); return; }
            bool oldWaiting = plan?.WaitingForDoor == true;
            Draw(next.Points); plan = next; plannedTarget = destination.Position;
            lastPlan = Time.time; Visibility();
            if (notify || oldWaiting != next.WaitingForDoor)
                ItemTaskAdapter.Feedback("导航：" + target.Label + (next.WaitingForDoor ? "；先沿路线到普通门前，开门后继续" : "；沿青绿色路线前往")
                    + (scanHidden ? "；当前扫描期间导航线隐藏" : ""));
        }
        static void Draw(Vector3[] points)
        {
            if (root == null) {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                if (shader == null) throw new InvalidOperationException("未找到独立导航线可用的简单着色器");
                material = new Material(shader) { name = "PreLuna.Captain.Route.Material" };
                root = new GameObject("PreLuna.Captain.LocalRoute");
                line = root.AddComponent<LineRenderer>(); line.enabled = false; line.sharedMaterial = material;
                line.useWorldSpace = true; line.loop = false; line.startWidth = .10f; line.endWidth = .10f;
                line.startColor = line.endColor = new Color(.1f, .95f, .7f, .9f);
                line.numCornerVertices = 2; line.numCapVertices = 2;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            }
            line.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++) line.SetPosition(i, points[i] + Vector3.up * .12f);
        }
        static void Stop(string reason) { Clear(); ItemTaskAdapter.Feedback(reason); }
        static void Failure(Exception error) {
            Clear(); Plugin.Logger.LogWarning("独立路线导航失败，已清理自己的显示对象：" + error);
            ItemTaskAdapter.Feedback("导航暂不可用：" + error.Message);
        }
        static void Clear()
        {
            target = null; plan = null; origin = null; floor = issuer = IntPtr.Zero;
            nextTick = lastPlan = 0; scanHidden = false;
            var ownedRoot = root; var ownedMaterial = material; root = null; material = null;
            if (line != null) line.enabled = false; line = null;
            try { if (ownedRoot != null) UnityEngine.Object.Destroy(ownedRoot); }
            finally { if (ownedMaterial != null) UnityEngine.Object.Destroy(ownedMaterial); }
        }
        internal static void Reset() { Clear(); nextRequest = 0; }
    }
}
