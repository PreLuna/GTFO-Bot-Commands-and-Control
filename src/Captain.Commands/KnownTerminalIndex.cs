using System;
using System.Collections.Generic;
using AIGraph;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;
namespace PreLuna.Captain.Tasks
{
    internal sealed record TerminalIdentity(LG_ComputerTerminal Value, uint Sync, IntPtr Floor);
    internal sealed class KnownTerminalSnapshot
    {
        public readonly List<CaptainTerminalResult> Results = new();
        public readonly Dictionary<int, TerminalIdentity> Targets = new();
        public readonly Dictionary<int, string> Details = new();
    }
    // Read the game's live registry only on command submission; discovery still requires arrival.
    internal static class KnownTerminalIndex
    {
        static readonly KnownSupplyLedger<TerminalIdentity> known = new();
        static readonly HashSet<IntPtr> mapped = new();
        static float nextWarning;
        internal static bool HasState => known.Count != 0;
        static bool Active => SNet.IsMaster && GameStateManager.CurrentStateName == eGameStateName.InLevel && Builder.CurrentFloor != null;
        internal static bool Resolve(LG_ComputerTerminal terminal, out AIG_CourseNode node)
        {
            node = null;
            try { return ResolveLive(terminal, out node); }
            catch (Exception error) {
                if (Time.time >= nextWarning) { nextWarning = Time.time + 10; Plugin.Logger.LogWarning("终端搜寻对象核验失败，跳过该对象：" + error); }
                return false;
            }
        }
        static bool ResolveLive(LG_ComputerTerminal terminal, out AIG_CourseNode node)
        {
            node = null;
            if (!Active || terminal == null) return false;
            var root = terminal.gameObject;
            var manager = LG_ComputerTerminalManager.Current;
            if (root == null || !root.activeInHierarchy || !terminal.m_isSetup || terminal.m_terminalItem == null
                || manager?.m_terminals == null || !manager.m_terminals.TryGetValue(terminal.SyncID, out var registered)
                || registered?.Pointer != terminal.Pointer) return false;
            node = terminal.SpawnNode;
            return node?.m_zone?.Layer != null;
        }
        internal static List<LG_ComputerTerminal> Candidates(out bool truncated)
        {
            var result = new List<LG_ComputerTerminal>(); truncated = false;
            var registry = LG_ComputerTerminalManager.Current?.m_terminals;
            if (!Active || registry == null) return result;
            var seen = new HashSet<IntPtr>(); int examined = 0;
            foreach (var terminal in registry.Values) {
                if (++examined > 1024) { truncated = true; break; }
                if (Resolve(terminal, out _) && seen.Add(terminal.Pointer)) result.Add(terminal);
            }
            return result;
        }
        internal static string Name(LG_ComputerTerminal terminal) => "终端 " + (string.IsNullOrWhiteSpace(terminal.ItemKey)
            ? "TERMINAL_" + terminal.m_serialNumber : terminal.ItemKey);
        internal static string State(LG_ComputerTerminal terminal)
        {
            string state = terminal.CurrentStateName switch {
                TERM_State.Sleeping => "待唤醒", TERM_State.Awake => "就绪", TERM_State.PlayerInteracting => "有人使用",
                TERM_State.DataMining => "下载中", TERM_State.Hacked => "已入侵", TERM_State.CodePuzzle => "代码任务",
                TERM_State.InputTest => "输入测试", TERM_State.ReactorError => "反应堆错误",
                TERM_State.AskToPlayLogAudio => "等待播放日志", TERM_State.DoPlayAudioFile => "正在播放音频",
                TERM_State.AudioLoopError => "音频循环错误", TERM_State.Ping => "定位中",
                TERM_State.PasswordProtected => "密码保护", TERM_State.EnterPassword => "输入密码中", _ => "未知状态" };
            return terminal.IsPasswordProtected && terminal.CurrentStateName is not TERM_State.PasswordProtected and not TERM_State.EnterPassword
                ? state + "·密码保护" : state;
        }
        internal static bool Observe(LG_ComputerTerminal terminal)
        {
            if (!Resolve(terminal, out _)) return false;
            long key = terminal.Pointer.ToInt64();
            foreach (var entry in known.Entries) if (entry.Key == key && (entry.Value.Sync != terminal.SyncID || entry.Value.Floor != Builder.CurrentFloor.Pointer)) {
                known.Forget(key); mapped.Remove(terminal.Pointer); break;
            }
            return known.Observe(key, new(terminal, terminal.SyncID, Builder.CurrentFloor.Pointer), SupplySource.RobotSearch) != null;
        }
        internal static bool Current(int id, TerminalIdentity saved) => saved != null && saved.Floor == Builder.CurrentFloor?.Pointer
            && Resolve(saved.Value, out _) && saved.Value.SyncID == saved.Sync && known.Current(saved.Value.Pointer.ToInt64(), id);
        internal static KnownTerminalSnapshot Capture(PlayerAgent source)
        {
            var snapshot = new KnownTerminalSnapshot();
            if (!Active || source?.CourseNode?.m_zone == null) return snapshot;
            var entries = new List<KnownSupplyLedger<TerminalIdentity>.Entry>(known.Entries); entries.Sort((a,b) => a.Id.CompareTo(b.Id));
            foreach (var entry in entries) {
                var saved = entry.Value;
                if (!Current(entry.Id, saved)) { known.Forget(entry.Key); continue; }
                var terminal = saved.Value;
                if (!Resolve(terminal, out var node) || !ItemRegion.SameSpace(source, node) || !SearchTraversal.InScope(node, source.CourseNode, 1)) continue;
                if (snapshot.Results.Count >= 64) { snapshot.Results.Add(new(0, "本次最多展示64个终端，请缩小范围或重开列表", true)); break; }
                string zone = node.m_zone.AliasName ?? "当前Zone";
                string room = node.m_area?.m_navInfo?.Number.ToString() ?? "未知房间";
                string label = Name(terminal) + "（" + State(terminal) + "）";
                snapshot.Results.Add(new(entry.Id, label)); snapshot.Targets[entry.Id] = saved;
                snapshot.Details[entry.Id] = label + "；" + zone + " / 房间 " + room + "；位置 " + terminal.transform.position
                    + "；来源：队长即时搜寻；仅定位，终端命令请玩家自行操作";
            }
            return snapshot;
        }
        internal static bool Mark(LG_ComputerTerminal terminal)
        {
            if (!Resolve(terminal, out _)) return false;
            bool marked = SearchMarker.Place(terminal.gameObject, "搜寻：" + Name(terminal), NavMarkerOption.TerminalTitleDistance);
            try {
                var revealer = terminal.m_mapLookatRevealer;
                if (revealer != null && !mapped.Contains(terminal.Pointer)) {
                    MapDataManager.WantToSetGUIObjVisible(revealer.MapGUIObjID, eCM_GuiObjectStatus.Normal); mapped.Add(terminal.Pointer);
                }
            } catch (Exception error) {
                if (Time.time >= nextWarning) { nextWarning = Time.time + 10; Plugin.Logger.LogWarning("终端地图图标失败，结果及临时标记仍保留：" + error); }
            }
            return marked;
        }
        internal static void Reset() { known.Reset(); mapped.Clear(); nextWarning = 0; }
    }
}
