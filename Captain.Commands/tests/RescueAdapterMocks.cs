using System;
using System.Collections.Generic;
using System.Reflection;
class FakeObject
{
    static int next;
    public IntPtr Pointer = new(++next);
    public T TryCast<T>() where T:class => this as T;
}
namespace UnityEngine {
    public static class Time { public static float time; }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
}
namespace AIGraph { class AIG_CourseNode:FakeObject { public int Zone=1; } class AIG_CoursePortal:FakeObject {} }
namespace LevelGeneration { static class Builder { public static FakeObject CurrentFloor=new(); } }
namespace SNetwork { static class SNet { public static bool IsMaster=true; } }
enum eGameStateName { InLevel, Lobby }
static class GameStateManager { public static eGameStateName CurrentStateName=eGameStateName.InLevel; }
class AIDangerZone { public enum SeverityEnum { Harmful } }
class Interact_Timed { public sealed class InteractorInfo { public Player.PlayerAgent Agent; } public List<InteractorInfo> m_interactors=new(); }
class Interact_Revive:Interact_Timed {}
namespace HarmonyLib {
    static class Priority { public const int First=800,Last=0; }
    sealed class HarmonyMethod { public int priority; public HarmonyMethod(Type type,string name){} }
    sealed class Harmony { public void Patch(MethodInfo method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){} }
    static class AccessTools { public static MethodInfo Method(Type t,string n)=>t.GetMethod(n); }
}
namespace Player {
    class Owner:FakeObject { public bool IsBot; }
    class Damage { public bool GrabbedAtAll; }
    class PlayerLocomotion { public enum PLOC_State { Stand,Downed } public PLOC_State m_currentStateEnum; }
    class PlayerAgent:FakeObject {
        public Owner Owner=new(); public bool Alive=true; public Damage Damage=new();
        public PlayerLocomotion Locomotion=new(); public Interact_Revive ReviveInteraction=new();
        public AIGraph.AIG_CourseNode CourseNode=new(); public int PlayerSlotIndex;public string PlayerName;
        public UnityEngine.Vector3 Position; public PlayerAIBot Bot;public int DimensionIndex;
        public T GetComponent<T>() where T:class => Bot as T;
    }
    class PlayerManager {
        public static PlayerManager Current=new();public PlayerAgent[] Slots=new PlayerAgent[4];
        public PlayerAgent GetPlayerAgentInSlot(int slot)=>slot>=0&&slot<4?Slots[slot]:null;
        public static PlayerAgent GetLocalPlayerAgent()=>Current.Slots[0];
    }
    class PlayerBotActionBase:FakeObject {
        [Flags] public enum AccessLayers { None=0,Legs=1,RootPosition=2,Hands=4 }
        public Descriptor DescBase;
        public class Descriptor:FakeObject {
            public enum StatusType { Stopped,Active,Successful,Failed }
            public float Prio=5; public AccessLayers RequiredLayers;public PlayerBotActionBase ParentActionBase,ActionBase;
            public StatusType Status;
            public bool IsTerminated()=>Status!=StatusType.Active;
        }
    }
    class PlayerBotActionRevive:PlayerBotActionBase {
        public enum State { Idle,Move,Revive }
        public State m_state;
        public new class Descriptor:PlayerBotActionBase.Descriptor { public PlayerAgent Client; }
    }
    class PlayerBotActionUseBioscan:PlayerBotActionBase { public new class Descriptor:PlayerBotActionBase.Descriptor {} }
    class PlayerBotActionDeploySentryGun:PlayerBotActionBase {}
    class PlayerBotActionDeployTripMine:PlayerBotActionBase {}
    class PlayerBotActionGatherDeployables:PlayerBotActionBase {}
    class PlayerBotActionUnlock:PlayerBotActionBase {}
    class PlayerBotActionCollectItem:PlayerBotActionBase {}
    class PlayerBotActionShareResourcePack:PlayerBotActionBase {}
    class PlayerBotActionCarryExpeditionItem:PlayerBotActionBase {}
    class RootPlayerBotAction:PlayerBotActionBase {
        public PlayerAIBot m_bot;public PlayerBotActionRevive.Descriptor m_reviveAction=new();
    }
    class PlayerAIBot:FakeObject {
        public PlayerAgent Agent;public object SyncValues=new();public PlayerBotActionBase.Descriptor m_rootAction;
        public List<PlayerBotActionBase> Actions=new();public int Requests,Stops;public bool Accept=true,Danger=true;
        public bool TestDangerZones(UnityEngine.Vector3 point,AIDangerZone.SeverityEnum severity,out int ignored){ignored=0;return Danger;}
        public bool RequestAction(PlayerBotActionBase.Descriptor desc){
            Requests++;if(!Accept)return false;
            desc.Status=PlayerBotActionBase.Descriptor.StatusType.Active;
            var action=new PlayerBotActionRevive{DescBase=desc,m_state=PlayerBotActionRevive.State.Move};desc.ActionBase=action;Actions.Add(action);return true;
        }
        public void StopAction(PlayerBotActionBase.Descriptor desc){Stops++;desc.Status=PlayerBotActionBase.Descriptor.StatusType.Stopped;Actions.RemoveAll(a=>a.DescBase==desc);}
    }
}
namespace PreLuna.Captain {
    static class Plugin { public static readonly Log Logger=new(); public sealed class Log { public void LogInfo(string s){} public void LogWarning(string s){} } }
}
namespace PreLuna.Captain.Tasks {
    sealed record RoutePlan(UnityEngine.Vector3[] Points,AIGraph.AIG_CourseNode EndNode,AIGraph.AIG_CoursePortal Door,bool WaitingForDoor,AIGraph.AIG_CoursePortal[] Passages,long[] Nodes);
    static class RouteTraversal {
        public static readonly HashSet<IntPtr> BlockedBots=new();
        public static int Checks;
        public static RoutePlan Build(Player.PlayerAgent player,AIGraph.AIG_CourseNode node,UnityEngine.Vector3 point,AIGraph.AIG_CoursePortal door,AIGraph.AIG_CourseNode origin,out string reason){
            Checks++;reason=BlockedBots.Contains(player.Pointer)?"no route":null;
            return reason!=null?null:new(new[]{player.Position,point},node,null,false,Array.Empty<AIGraph.AIG_CoursePortal>(),Array.Empty<long>());
        }
    }
    static class ItemRegion { public static bool SameSpace(Player.PlayerAgent player,AIGraph.AIG_CourseNode node)=>player.CourseNode.Zone==node?.Zone; }
    static class SearchTraversal { public static bool InScope(AIGraph.AIG_CourseNode node,AIGraph.AIG_CourseNode origin,int range)=>node?.Zone==origin?.Zone; }
    static class AutoPickupAdapter { public static int Cancelled;public static void CancelPrompt(Player.PlayerAIBot bot){Cancelled++;} }
    static class MovementOrders { public static int Yields;public static void BeforeNativeCommand(Player.PlayerAIBot bot){Yields++;} }
    static class NativePickupAdapter { public static readonly HashSet<IntPtr> Busy=new();public static bool Owns(Player.PlayerAIBot b)=>Busy.Contains(b.Pointer); }
    static class ResourceUseAdapter { public static readonly HashSet<IntPtr> Busy=new();public static bool Owns(Player.PlayerAIBot b)=>Busy.Contains(b.Pointer); }
    static class SingleKillAdapter { public static readonly HashSet<IntPtr> Busy=new();public static bool Owns(Player.PlayerAIBot b)=>Busy.Contains(b.Pointer); }
    static class ItemTaskAdapter {
        public static readonly List<string> Messages=new();public static readonly HashSet<IntPtr> Atomic=new();public static int Suspends,Resumes;
        public static void Feedback(string s)=>Messages.Add(s);
        public static bool Busy(Player.PlayerAIBot b)=>Atomic.Contains(b.Pointer)||RescueOrders.Owns(b);
        public static bool RescueYielded(Player.PlayerAIBot b)=>!Atomic.Contains(b.Pointer);
        public static void SuspendForRescue(Player.PlayerAIBot b,List<Action> resume){Suspends++;resume.Add(()=>Resumes++);}
    }
}
