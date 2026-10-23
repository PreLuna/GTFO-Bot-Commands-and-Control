using System;
using System.Collections.Generic;
using PreLuna.Captain.Tasks;
public class NativeObject { static long serial; readonly int instance=(int)++serial; public IntPtr Pointer; public NativeObject(){Pointer=new(instance);} public int GetInstanceID()=>instance; public T TryCast<T>() where T : class => this as T; }
public enum eGameStateName { InLevel, Lobby }
public static class GameStateManager { public static eGameStateName CurrentStateName = eGameStateName.InLevel; }
public static class PUIWorld { public static bool StopThrows; }
namespace UnityEngine { public static class Time { public static float unscaledTime; } }
namespace SNetwork { public static class SNet { public static bool IsMaster = true; public static NativeObject Master=new(); } }
namespace LevelGeneration { public static class Builder { public static NativeObject CurrentFloor = new(); } }
namespace Player {
    public class Owner : NativeObject { public bool IsBot; }
    public class Damage { public bool GrabbedAtAll; }
    public class PlayerLocomotion { public int m_currentStateEnum; }
    public class PlayerAgent : NativeObject {
        public bool Alive = true, IsBeingDestroyed, IsBeingDespawned;
        public int PlayerSlotIndex; public string PlayerName = "bot"; public Owner Owner = new() { IsBot = true };
        public object CourseNode = ItemRegion.Node; public Damage Damage = new(); public PlayerLocomotion Locomotion = new(); public PlayerAIBot Bot;
        public T GetComponent<T>() where T : class => Bot as T;
    }
    public class PlayerManager {
        public static PlayerManager Current = new(); public PlayerAgent[] Slots = new PlayerAgent[4];
        public PlayerAgent GetPlayerAgentInSlot(int slot) => Slots[slot]; public static PlayerAgent GetLocalPlayerAgent() => Current.Slots[0];
    }
    public class PlayerBotActionBase : NativeObject {
        public Descriptor DescBase = new(); public class Descriptor : NativeObject { public bool Terminated; public bool IsTerminated() => Terminated; }
    }
    public class RootPlayerBotAction : PlayerBotActionBase { public new class Descriptor : PlayerBotActionBase.Descriptor { } }
    public class PlayerBotActionTravel : PlayerBotActionBase { }
    public class PlayerBotActionWalk : PlayerBotActionBase { }
    public class PlayerBotActionLook : PlayerBotActionBase { }
    public class PlayerBotActionFollow : PlayerBotActionBase { }
    public class PlayerBotActionIdle : PlayerBotActionBase { }
    public class PlayerBotActionTurn : PlayerBotActionBase { }
    public class PlayerBotActionCollectItem : PlayerBotActionBase { }
    public class PlayerBotActionUnlock : PlayerBotActionBase { }
    public class PlayerAIBot : NativeObject {
        public PlayerAgent Agent; public List<PlayerBotActionBase> Actions = new(); public List<PlayerBotActionBase.Descriptor> m_queuedActions = new();
        public Sync SyncValues = new(); public class Sync { public PlayerAgent Leader; } public int Stops;
        public void StopAction(PlayerBotActionBase.Descriptor desc) { if (PUIWorld.StopThrows) throw new Exception("stop failure"); desc.Terminated = true; Stops++; }
    }
}
public class PUI_CommunicationMenu {
    public int Calls; public bool SawScope;
    public bool ExecuteCmdCall(Player.PlayerAgent local, Player.PlayerAgent target) {
        Calls++; SawScope = BotControlService.RecallScope; target.Bot.SyncValues.Leader = local; return true;
    }
}
namespace PreLuna.Captain {
    public static class Plugin { public static Log Logger = new(); public class Log { public void LogInfo(string s) { } public void LogWarning(string s) { } } }
}
namespace PreLuna.Captain.Tasks {
    public enum ItemTaskStage { Moving, Interacting, Verifying, Cancelled }
    static class ControlCommandAdapter {
        internal static bool Owns(long a, long o) => false;
        internal static bool Busy(long t, out string label) { label = null; return false; }
        internal static bool Mark(long t, Player.PlayerAIBot bot, PreLuna.Captain.Control.BotControlRay ray, out string reason) { reason = null; return true; }
        internal static bool AllowsAction(long a,long o,long d) => false;
        internal static bool AllowsTeleport(long a,long o) => false;
        internal static bool Detach(long t,Action release) => false;
        internal static void Tick() { }
        internal static void Reset() { }
    }
    static class ItemRegion { internal static readonly object Node = new(); internal static bool SameSpace(Player.PlayerAgent agent, object node) => ReferenceEquals(agent.CourseNode, node); }
    static class BotTaskBridge {
        internal static bool Permit = true, Renewal = true, UnsafeValue; internal static int Releases;
        static long serial; static readonly Dictionary<IntPtr,long> leases = new();
        internal static void Reset() { Permit = Renewal = true; UnsafeValue = false; Releases = 0; leases.Clear(); }
        internal static bool Bind() => true; internal static bool Unsafe(Player.PlayerAIBot bot) => UnsafeValue;
        internal static bool Acquire(Player.PlayerAIBot bot, out long token) { token = 0; if (!Permit || leases.ContainsKey(bot.Pointer)) return false; token = ++serial; leases[bot.Pointer] = token; return true; }
        internal static bool Report(Player.PlayerAIBot bot, long token, ItemTaskStage stage) => Renewal && leases.TryGetValue(bot.Pointer, out var current) && current == token;
        internal static bool CanExecute(Player.PlayerAIBot bot, long token) => Report(bot, token, ItemTaskStage.Moving);
        internal static void Release(Player.PlayerAIBot bot, long token, ItemTaskStage stage) { Releases++; if (leases.TryGetValue(bot.Pointer, out var current) && current == token) leases.Remove(bot.Pointer); }
        internal static bool AutoAvailable(Player.PlayerAIBot bot) => !leases.ContainsKey(bot.Pointer);
    }
    static class AutoPickupAdapter { internal static void CancelPrompt(Player.PlayerAIBot bot) { } }
    static class ItemTaskAdapter {
        internal static bool CommunicationOpen => false;
        internal sealed class Job { internal bool Paused, Yielded = true; internal int Revision; }
        internal static readonly Dictionary<IntPtr,Job> Jobs = new(); internal static PUI_CommunicationMenu NativeMenu = new();
        internal static bool HasItemTask(Player.PlayerAIBot bot) => Jobs.ContainsKey(bot.Pointer);
        internal static void SuspendForBorrow(Player.PlayerAIBot bot, List<BorrowHold> holds) {
            if (!Jobs.TryGetValue(bot.Pointer, out var job)) return;
            bool changed = !job.Paused; job.Paused = true; int revision = job.Revision;
            holds.Add(new(() => job.Yielded, () => { if (changed && job.Revision == revision) job.Paused = false; }, () => job.Revision == revision));
        }
    }
    static class MovementOrders {
        internal static bool ControlStationRecallProtected(long a,long o) => false;
        internal static bool AcceptControlStation(Player.PlayerAIBot b,MarkedPosition mark,out string reason){reason=null;return mark!=null;}
        internal static bool FollowPaused;
        internal static void SuspendFollowForControl(Player.PlayerAIBot bot, List<BorrowHold> holds) { bool previous = FollowPaused; FollowPaused = true; holds.Add(new(() => true, () => FollowPaused = previous)); }
        internal static void KeepFollowPausedAfterControl(Player.PlayerAIBot bot) => FollowPaused = true;
    }
    static class RescueOrders { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class ToolSupplyAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class ResourceUseAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class NativePickupAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class SearchTaskAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class SingleKillAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class FocusOrders { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class SharedMeleeBridge { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class OpenTaskAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class CollectTaskAdapter { internal static bool Owns(Player.PlayerAIBot bot) => false; }
    static class CaptainMenuInput { internal static bool OwnsInput => false; }
    sealed class MarkedPosition { }
}
