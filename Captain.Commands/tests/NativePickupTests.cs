using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PreLuna.Captain.Tasks;
namespace UnityEngine {
    class Object {static long serial;public IntPtr Pointer=new(++serial);public T TryCast<T>() where T:class=>this as T;}
    class Transform {public Vector3 position;}
    class GameObject {public bool activeInHierarchy=true;}
    struct Vector3 {public float x;}
    static class Time {public static float time;}
}
namespace HarmonyLib {
    class Harmony {public void Patch(object method,HarmonyMethod prefix=null){}}
    class HarmonyMethod {public int priority;public HarmonyMethod(Type t,string m){}}
    static class Priority {public const int First=800;}
    static class AccessTools {public static object Method(Type t,string m,Type[] p)=>new();}
}
namespace AIGraph {
    class Zone:UnityEngine.Object {public UnityEngine.Object Layer=new();public int DimensionIndex;}
    class AIG_CourseNode:UnityEngine.Object {public Zone m_zone=new();}
    struct pCourseNode {public AIG_CourseNode Node;public bool TryGet(out AIG_CourseNode node){node=Node;return node!=null;}}
}
enum eGameStateName {Lobby,InLevel}
static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
namespace SNetwork {
    static class SNet {public static bool IsMaster=true;}
    class SNet_Player:UnityEngine.Object {public bool IsBot=true;}
    struct pPlayer {public SNet_Player Owner;public bool TryGetPlayer(out SNet_Player owner){owner=Owner;return owner!=null;}}
}
namespace Player {
    enum InventorySlot {None=0,ResourcePack=4,Consumable=5,ConsumableHeavy=6,InPocket=7,InLevelCarry=8}
    struct pItemData {public uint itemID_gearCRC;public InventorySlot slot;}
    class PlayerAgent:UnityEngine.Object {public SNetwork.SNet_Player Owner=new();public bool Alive=true;public int PlayerSlotIndex,DimensionIndex;public string PlayerName="Bishop";public AIGraph.AIG_CourseNode CourseNode=new();public T GetComponent<T>() where T:class=>null;}
    class PlayerManager {public static PlayerManager Current;public static PlayerAgent Source;public static PlayerAgent GetLocalPlayerAgent()=>Source;public PlayerAgent GetPlayerAgentInSlot(int slot)=>null;}
    class BackpackItem {public Item Instance;}
    class PlayerBackpack {public Dictionary<InventorySlot,BackpackItem> Items=new();public HashSet<uint> Pocket=new();public bool HasPocketItem(uint id)=>Pocket.Contains(id);public bool TryGetBackpackItem(InventorySlot slot,out BackpackItem item)=>Items.TryGetValue(slot,out item);}
    static class PlayerBackpackManager {
        public static Dictionary<IntPtr,PlayerBackpack> Bags=new();public static Dictionary<uint,Item> Worlds=new();
        public static bool TryGetBackpack(SNetwork.SNet_Player player,out PlayerBackpack bag)=>Bags.TryGetValue(player.Pointer,out bag);
        public static bool TryGetItemInLevelFromItemData(pItemData data,out Item item)=>Worlds.TryGetValue(data.itemID_gearCRC,out item);
    }
    class PlayerBotActionBase:UnityEngine.Object {
        public PlayerAIBot m_bot;public Descriptor DescBase;
        public class Descriptor:UnityEngine.Object {public static float s_minAbortPrioDiff=1;public float Prio;public bool Terminated;public PlayerBotActionBase ParentActionBase,ActionBase;public bool IsTerminated()=>Terminated;public void OnAborted(){Terminated=true;}}
    }
    class PlayerBotActionCollectItem:PlayerBotActionBase {
        public enum State {Idle,Move,Transfer}public State m_state;
        public new class Descriptor:PlayerBotActionBase.Descriptor {public Item TargetItem;public object TargetContainer;public UnityEngine.Vector3 TargetPosition;public float Haste;public Descriptor(PlayerAIBot bot){}}
    }
    class PlayerBotActionUnlock:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor {public enum TargetTypeEnum {Container,Door}public TargetTypeEnum TargetType;}}
    class PlayerBotActionHighlight:PlayerBotActionBase {public new class Descriptor:PlayerBotActionBase.Descriptor {public enum TargetTypeEnum {Container,Enemy}public TargetTypeEnum TargetType;}}
    class PlayerBotActionCarryExpeditionItem:PlayerBotActionBase {
        public Descriptor m_desc;public PlayerBotActionCollectItem.Descriptor m_collectAction;
        public new class Descriptor:PlayerBotActionBase.Descriptor {
            public CarryItemPickup_Core TargetItem;public float HoldPrio,MaxLeaderDistance=10;public bool AllowIdleDrop;
            public Descriptor(PlayerAIBot bot){}
            public static bool Allowed=true;public static bool Evaluate(PlayerAIBot bot,CarryItemPickup_Core core,float prio,bool idle)=>Allowed;
        }
    }
    static class RootPlayerBotAction {public static Settings m_prioSettings=new();public class Settings {public float CollectItem=3,UseBioscan=10;}}
    class PlayerAIBot:UnityEngine.Object {
        public List<PlayerBotActionBase> Actions=new();
        public PlayerAgent Agent=new();public int Stops;public bool StopSucceeds=true;
        public void StopAction(PlayerBotActionBase.Descriptor desc){Stops++;if(StopSucceeds)desc.Terminated=true;}
    }
}
class Item:UnityEngine.Object {public object ItemDataBlock=new();public UnityEngine.GameObject gameObject=new();public UnityEngine.Transform transform=new();public string PublicName="Cell";public Player.pItemData Data;public Player.pItemData Get_pItemData()=>Data;}
namespace LevelGeneration {
    static class Builder {public static UnityEngine.Object CurrentFloor=new();}
    enum ePickupItemStatus {PlacedInLevel,PickedUp}
    struct Placement {public AIGraph.pCourseNode node;}
    struct pPickupItemState {public ePickupItemStatus status;public Placement placement;public SNetwork.pPlayer pPlayer;}
    class LG_PickupItem_Sync:UnityEngine.Object {public pPickupItemState State;public pPickupItemState GetCurrentState()=>State;}
    class Storage {public Core m_core=new();public class Core {public AIGraph.AIG_CourseNode SpawnNode;}}
    class ItemInLevel:Item {
        public LG_PickupItem_Sync Sync;public Storage container;public AIGraph.AIG_CourseNode CourseNode;
        public LG_PickupItem_Sync GetSyncComponent()=>Sync;
        public struct Custom {public float ammo;}
        public Custom GetCustomData()=>throw new Exception("special item must never read ammo");
    }
}
class CarryItemPickup_Core:LevelGeneration.ItemInLevel {
    public bool ObjectiveItemSolved,IsLinkedToMachine,IsInteractable=true;
    public LevelGeneration.ePickupItemStatus PickupItemStatus=LevelGeneration.ePickupItemStatus.PlacedInLevel;
    public Player.PlayerAgent PickedUpByPlayer;
}
class PUI_CommunicationMenu {
    public int Calls;public bool Accept=true,Deferred,Throw,IdleDrop=true;public float Hold=7;
    public float GetItemHoldPrio(CarryItemPickup_Core item,out bool idleDrop){idleDrop=IdleDrop;return Hold;}
    public bool RequestBotAction(Player.PlayerAIBot bot,Player.PlayerBotActionBase.Descriptor desc){Calls++;if(Throw)throw new Exception("request failure");if(!NativePickupAdapter.BeforeCommand(bot,desc))return false;if(Accept&&!Deferred){
        Player.PlayerBotActionBase action=desc is Player.PlayerBotActionCarryExpeditionItem.Descriptor carry?new Player.PlayerBotActionCarryExpeditionItem{m_desc=carry}:new Player.PlayerBotActionCollectItem();action.DescBase=desc;action.m_bot=bot;desc.ActionBase=action;
    }return Accept;}
}
namespace PreLuna.Captain {
    static class Plugin {public static Log Logger=new();public static Catalog Catalog=new();}
    class Log {public List<string> Warnings=new();public void LogWarning(string text)=>Warnings.Add(text);public void LogInfo(string text){}}
    class Catalog {public string TranslateItems(string text)=>text;}
}
namespace PreLuna.Captain.Tasks {
    enum TaskBoardState {Waiting,Executing,Complete,Failed,Cancelled}
    static class TaskBoard {public static Journal Journal=new();}
    class Journal {public TaskBoardState State;public string Detail;public void Record(object key,int slot,long owner,string bot,string task,string target,TaskBoardState state,string stage,string reason,float now){State=state;Detail=reason;}}
    static class AutoPickupAdapter {public static bool Restrict=true,IsOpen=true;public static bool Open(LevelGeneration.ItemInLevel item)=>IsOpen;public static void CancelPrompt(Player.PlayerAIBot bot,LevelGeneration.ItemInLevel item){}}
    static class OpenTaskAdapter {public static bool Authorizes(Player.PlayerAIBot b,Player.PlayerBotActionBase.Descriptor d)=>false;}
    static class SearchTraversal {
        public static bool InRange=true,Reach=true;
        public static bool InScope(AIGraph.AIG_CourseNode a,AIGraph.AIG_CourseNode b,int scope)=>InRange;
        public static HashSet<long> Reachable(Player.PlayerAgent a,AIGraph.AIG_CourseNode start,AIGraph.AIG_CourseNode origin,int scope,out bool truncated){truncated=false;return Reach?new HashSet<long>{start.Pointer.ToInt64(),TestFixture.TargetNode.Pointer.ToInt64()}:new();}
    }
    static class ItemTaskAdapter {
        public static string Busy;public static bool OtherReserved;public static int Returns;public static List<string> Messages=new();
        public static string BusyReason(Player.PlayerAIBot bot)=>NativePickupAdapter.Owns(bot)?"native busy":Busy;
        public static bool Reserved(LevelGeneration.ItemInLevel item)=>OtherReserved||NativePickupAdapter.Reserved(item);
        public static void Feedback(string message)=>Messages.Add(message);
        public static void Diagnose(Player.PlayerAIBot b,LevelGeneration.ItemInLevel i,Player.InventorySlot s,string label,string phase,string reason)=>Messages.Add(reason);
        public static void ReturnAfterSupply(Player.PlayerAIBot bot){Returns++;}
    }
}
class TestFixture {
    public static AIGraph.AIG_CourseNode TargetNode;
    public Player.PlayerAIBot Bot=new();public Player.PlayerBackpack Bag=new();public PUI_CommunicationMenu Menu=new();public LevelGeneration.ItemInLevel Item;
    public TestFixture(Player.InventorySlot slot=Player.InventorySlot.InLevelCarry) {
        NativePickupAdapter.Reset();ItemActionAuthorization.Reset();UnityEngine.Time.time=10;SNetwork.SNet.IsMaster=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;
        Player.PlayerBotActionCarryExpeditionItem.Descriptor.Allowed=true;SearchTraversal.Reach=SearchTraversal.InRange=AutoPickupAdapter.IsOpen=true;
        ItemTaskAdapter.Busy=null;ItemTaskAdapter.OtherReserved=false;ItemTaskAdapter.Returns=0;ItemTaskAdapter.Messages.Clear();
        TargetNode=Bot.Agent.CourseNode;Player.PlayerManager.Source=new(){CourseNode=TargetNode,Owner=new(){IsBot=false}};
        Item=slot==Player.InventorySlot.InLevelCarry?new CarryItemPickup_Core():new LevelGeneration.ItemInLevel{Sync=new()};
        Item.Data=new(){slot=slot,itemID_gearCRC=131};Item.CourseNode=TargetNode;
        Player.PlayerBackpackManager.Bags[Bot.Agent.Owner.Pointer]=Bag;
    }
    public bool Start()=>NativePickupAdapter.Issue(Bot,Item,Menu);
    public Player.PlayerBotActionBase.Descriptor Desc()=>GetJobDesc(Bot);
    static Player.PlayerBotActionBase.Descriptor GetJobDesc(Player.PlayerAIBot bot){var dict=(System.Collections.IDictionary)typeof(NativePickupAdapter).GetField("active",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);return (Player.PlayerBotActionBase.Descriptor)dict[bot.Pointer].GetType().GetField("Desc").GetValue(dict[bot.Pointer]);}
    public void Pick(Player.PlayerAgent picker=null){picker??=Bot.Agent;if(Item is CarryItemPickup_Core carry){carry.PickupItemStatus=LevelGeneration.ePickupItemStatus.PickedUp;carry.PickedUpByPlayer=picker;}else{Item.Sync.State=new(){status=LevelGeneration.ePickupItemStatus.PickedUp,pPlayer=new(){Owner=picker.Owner}};if(Item.Data.slot==Player.InventorySlot.InPocket)Bag.Pocket.Add(Item.Data.itemID_gearCRC);else{Bag.Items[Item.Data.slot]=new(){Instance=Item};Player.PlayerBackpackManager.Worlds[Item.Data.itemID_gearCRC]=Item;}}}
    public void Step(float dt=.1f){UnityEngine.Time.time+=dt;NativePickupAdapter.Tick();}
}
static class NativePickupTests {
    static int checks;static void Check(bool result,string reason){checks++;if(!result)throw new Exception(reason);}
    static bool Request(Player.PlayerAIBot bot,Player.PlayerBotActionBase.Descriptor desc){object[] args={bot,desc,false};return (bool)typeof(NativePickupAdapter).GetMethod("Request",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
    static bool Authorized(Player.PlayerAIBot bot,Player.PlayerBotActionBase.Descriptor desc){object[] args={bot,desc,false};return (bool)typeof(ItemActionAuthorization).GetMethod("Request",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
    static void Main(){
        foreach(int slot in new[]{4,5,6,7,8})Check(SpecialItemPolicy.Query(slot),"query slot "+slot);
        foreach(int slot in new[]{0,1,2,3,9,10,11})Check(!SpecialItemPolicy.Query(slot),"exclude equipment "+slot);
        Check(SpecialItemPolicy.Matches(7,3)&&SpecialItemPolicy.Matches(8,4)&&SpecialItemPolicy.Matches(6,4)&&!SpecialItemPolicy.Matches(8,2),"special filters do not invade consumables");
        var f=new TestFixture();Check(ItemRegion.TryResolve(f.Item,out var node)&&node.Pointer==TestFixture.TargetNode.Pointer,"carry without LG sync is queryable by actual placed state");
        Check(SpecialItemAccess.AmountText(f.Item).Contains("单件"),"no ammo access for carry");
        Check(f.Start()&&f.Menu.Calls==1&&NativePickupAdapter.Owns(f.Bot),"native carry single submission tracked");
        var desc=f.Desc();Check(desc is Player.PlayerBotActionCarryExpeditionItem.Descriptor d&&d.TargetItem.Pointer==f.Item.Pointer&&d.HoldPrio==7&&d.AllowIdleDrop,"exact target and native hold policy");
        for(int i=0;i<100;i++)f.Step();Check(f.Menu.Calls==1&&NativePickupAdapter.Owns(f.Bot),"no repeated request or accepted-is-success");
        var child=new Player.PlayerBotActionCollectItem.Descriptor(f.Bot){TargetItem=f.Item,ParentActionBase=desc.ActionBase};
        Check(NativePickupAdapter.Authorizes(f.Bot,child),"native carry Collect child authorized");
        Check(Authorized(f.Bot,child)&&!Authorized(f.Bot,new Player.PlayerBotActionCollectItem.Descriptor(f.Bot){TargetItem=f.Item}),"actual authorization patch allows only owned native child");
        Check(!NativePickupAdapter.Authorizes(f.Bot,new Player.PlayerBotActionCollectItem.Descriptor(f.Bot){TargetItem=f.Item}),"same target unrelated child refused");
        f.Pick();f.Step();Check(!NativePickupAdapter.Owns(f.Bot)&&TaskBoard.Journal.State==TaskBoardState.Complete,"actual picker confirmation completes");
        Check(NativePickupAdapter.Authorizes(f.Bot,child)&&f.Bot.Stops==0,"successful persistent carry not stopped, child permit retained");
        Check(Authorized(f.Bot,child),"completed carry child remains allowed by actual restriction patch");
        Check(!ItemRegion.TryResolve(f.Item,out _),"picked item removed from query results");
        f=new();f.Start();f.Pick(new());f.Step();Check(TaskBoard.Journal.State==TaskBoardState.Failed&&!NativePickupAdapter.Owns(f.Bot),"other picker cannot cause false success");
        f=new();f.Start();Check(!f.Start()&&f.Menu.Calls==1,"duplicate commands suppressed");
        var second=new Player.PlayerAIBot();Player.PlayerBackpackManager.Bags[second.Agent.Owner.Pointer]=new();Check(!NativePickupAdapter.Issue(second,f.Item,f.Menu),"same item reserved across bots");
        f=new();f.Menu.Accept=false;Check(!f.Start()&&!NativePickupAdapter.Owns(f.Bot),"native rejection releases tracking");
        f=new();Player.PlayerBotActionCarryExpeditionItem.Descriptor.Allowed=false;Check(!f.Start()&&f.Menu.Calls==0,"native capability evaluation respected");
        f=new();((CarryItemPickup_Core)f.Item).IsLinkedToMachine=true;Check(!f.Start()&&f.Menu.Calls==0,"linked mission item not extracted");
        f=new();((CarryItemPickup_Core)f.Item).ObjectiveItemSolved=true;Check(!f.Start(),"solved item refused");
        f=new();((CarryItemPickup_Core)f.Item).IsInteractable=false;Check(!f.Start(),"native disabled interaction respected");
        f=new();AutoPickupAdapter.IsOpen=false;Check(!f.Start(),"closed cabinet not opened by pickup");
        f=new();f.Bag.Items[Player.InventorySlot.InLevelCarry]=new(){Instance=new Item()};Check(!f.Start(),"occupied carry slot not replaced");
        f=new();SearchTraversal.InRange=false;Check(!f.Start(),"other Zone refused");
        f=new();SearchTraversal.Reach=false;Check(!f.Start(),"blocked door reachability refused");
        f=new();f.Menu.Deferred=true;f.Start();NativePickupAdapter.Cancel(f.Bot,true);Check(!NativePickupAdapter.Owns(f.Bot)&&ItemTaskAdapter.Returns==1,"unstarted native command aborted, cancellation return requested");
        f=new();f.Start();desc=f.Desc();NativePickupAdapter.Cancel(f.Bot,false);Check(!NativePickupAdapter.Owns(f.Bot)&&!Request(f.Bot,desc),"cancelled delayed request rejected");
        f=new();f.Start();f.Bot.StopSucceeds=false;NativePickupAdapter.Cancel(f.Bot,false);f.Step(3);Check(!NativePickupAdapter.Owns(f.Bot)&&f.Bot.Stops==1&&TaskBoard.Journal.State==TaskBoardState.Cancelled,"unconfirmed native stop cannot block forever or resubmit");
        f=new();f.Start();desc=f.Desc();var action=(Player.PlayerBotActionCarryExpeditionItem)desc.ActionBase;action.m_collectAction=new(f.Bot){ActionBase=new Player.PlayerBotActionCollectItem{m_state=Player.PlayerBotActionCollectItem.State.Transfer}};
        NativePickupAdapter.Cancel(f.Bot,true);Check(NativePickupAdapter.Owns(f.Bot)&&f.Bot.Stops==0,"transfer protected from cancel");
        f.Pick();f.Step();Check(!NativePickupAdapter.Owns(f.Bot)&&f.Bot.Stops==0&&ItemTaskAdapter.Returns==0,"post-transfer actual carry retained, no conflicting external return lease");
        f=new();f.Start();desc=f.Desc();desc.Terminated=true;var fresh=new Player.PlayerBotActionCarryExpeditionItem.Descriptor(f.Bot){TargetItem=(CarryItemPickup_Core)f.Item};Check(Request(f.Bot,fresh)&&f.Desc().Pointer==fresh.Pointer,"warp replacement descriptor adopted");
        f.Pick();f.Step();Check(TaskBoard.Journal.State==TaskBoardState.Complete,"replacement still verifies same world item");
        f=new();f.Start();f.Bot.Agent.Owner=new();f.Step();Check(!NativePickupAdapter.Owns(f.Bot)&&f.Bot.Stops==0,"new network owner not touched");
        f=new();f.Start();f.Step(121);Check(!NativePickupAdapter.Owns(f.Bot)&&f.Menu.Calls==1,"timeout one stop no new request");
        f=new();f.Start();LevelGeneration.Builder.CurrentFloor=new();f.Step();Check(!NativePickupAdapter.Owns(f.Bot)&&f.Bot.Stops==0,"old floor releases only tracking");
        foreach(var slot in new[]{Player.InventorySlot.InPocket,Player.InventorySlot.ConsumableHeavy}){
            f=new(slot);Check(f.Start()&&f.Desc() is Player.PlayerBotActionCollectItem.Descriptor,"special single native Collect "+slot);f.Pick();f.Step();Check(TaskBoard.Journal.State==TaskBoardState.Complete,"special picker and inventory identity verified "+slot);
        }
        f=new(Player.InventorySlot.InPocket);f.Bag.Pocket.Add(131);Check(!f.Start(),"existing pocket item not assumed new pickup");
        f=new();f.Menu.Throw=true;bool threw=false;try{f.Start();}catch{threw=true;}Check(threw&&!NativePickupAdapter.Owns(f.Bot),"request exceptions release reservations");
        f=new();f.Start();NativePickupAdapter.Reset();Check(NativePickupAdapter.Count==0&&!NativePickupAdapter.Reserved(f.Item),"reset clears task and reservations");
        Console.WriteLine("PASS "+checks+" native-special adapter assertions; native actions, network and UI controlled, game not run.");
    }
}
