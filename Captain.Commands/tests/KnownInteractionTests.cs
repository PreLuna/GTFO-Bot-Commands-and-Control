using System;
using System.Collections.Generic;
using System.Linq;
using PreLuna.Captain.Tasks;
namespace UnityEngine {
    class Object {public virtual T TryCast<T>() where T:class=>this as T;}
    class Scene {public bool Valid=true;public bool IsValid()=>Valid;}
    class GameObject {public bool activeInHierarchy=true;public Scene scene=new();}
    struct Vector3 {public float x,y,z;public override string ToString()=>"(1,2,3)";}
    class Transform {public Vector3 position;}
    class Component:Object {
        public IntPtr Pointer; public Component Parent; public GameObject gameObject=new();public Transform transform=new();
        public T GetComponentInParent<T>() where T:class {for(Component c=this;c!=null;c=c.Parent)if(c is T found)return found;return null;}
    }
    static class Time {public static float time=10;}
    static class Resources {public static List<Object> Objects=new();public static int Reads;public static IEnumerable<Object> FindObjectsOfTypeAll(Type t){Reads++;return Objects.Where(o=>t.IsInstanceOfType(o));}}
}
namespace Il2CppInterop.Runtime {static class Il2CppType {public static Type Of<T>()=>typeof(T);}}
namespace AIGraph {
    class Layer {public IntPtr Pointer=(IntPtr)1;}
    class Zone {public IntPtr Pointer=(IntPtr)1;public Layer Layer=new();public int Dimension;public string AliasName="ZONE 1";}
    class NavInfo {public int Number=1;}
    class Area {public IntPtr Pointer=(IntPtr)1;public NavInfo m_navInfo=new();}
    class AIG_CourseNode {public IntPtr Pointer=(IntPtr)1;public Zone m_zone=new();public Area m_area=new();public List<AIG_CoursePortal> m_portals=new();}
    class AIG_CoursePortal {public LevelGeneration.iLG_Door_Core m_door;public AIG_CourseNode m_nodeA,m_nodeB;}
}
namespace Player {class PlayerAgent {public AIGraph.AIG_CourseNode CourseNode=new();}}
namespace SNetwork {static class SNet {public static bool IsMaster=true;}}
enum eGameStateName {InLevel,Lobby}
static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
enum eCarryItemInsertTargetType {None,PowerCell,SmallHSU,Crate,Unknown}
struct pSyncTriggerState {public bool HasBeenTriggered;}
class Sync<T>:UnityEngine.Object {public IntPtr Pointer;public T m_currentState;}
class WorldEventTrigger_Sync {public Sync<pSyncTriggerState> m_stateReplicator;}
namespace LevelGeneration {
    class Floor {public IntPtr Pointer=(IntPtr)100;}
    static class Builder {public static Floor CurrentFloor=new();}
    enum eDoorStatus {Closed,Unlocked,Open,Opening,Destroyed,Closed_LockedWithNoKey,Closed_LockedWithChainedPuzzle_Alarm}
    enum ePowerGeneratorStatus {Powered,UnPowered,Unknown}
    struct pPowerGeneratorState {public ePowerGeneratorStatus status;}
    class LG_Gate {public iLG_Door_Core _SpawnedDoor_k__BackingField;}
    class iLG_Door_Core:UnityEngine.Component {}
    class LG_WeakDoor:iLG_Door_Core {public UnityEngine.Component m_syncComp;public object m_sync=new();public LG_Gate _Gate_k__BackingField;public int m_serialNumber;public eDoorStatus _LastStatus_k__BackingField;}
    class LG_SecurityDoor:iLG_Door_Core {public UnityEngine.Component m_syncComp;public object m_sync=new();public LG_Gate _Gate_k__BackingField;public int m_serialNumber;public eDoorStatus _LastStatus_k__BackingField;}
    class LG_PowerGenerator_Core:UnityEngine.Component {public Sync<pPowerGeneratorState> m_stateReplicator;public object m_terminalItem=new();public AIGraph.AIG_CourseNode _SpawnNode_k__BackingField=new();public int m_serialNumber;}
    class LG_HSUActivator_Core:UnityEngine.Component {public Sync<int> m_stateReplicator;public object m_terminalItem=new();public AIGraph.AIG_CourseNode _SpawnNode_k__BackingField=new();public int m_serialNumber;public bool _ObjectiveItemSolved_k__BackingField;}
    class LG_Area:UnityEngine.Component {public AIGraph.AIG_CourseNode m_courseNode=new();}
    class LG_WorldEventObject:UnityEngine.Component {public LG_Area _ParentArea_k__BackingField;}
    class LG_InteractWorldEventTrigger:UnityEngine.Component {public WorldEventTrigger_Sync m_worldEventTriggerSync;public eCarryItemInsertTargetType m_insertType;public uint m_interactionText;public bool m_isToggle;}
}
enum NavMarkerOption {WaypointTitleDistance}
namespace PreLuna.Captain {
    static class Plugin {public static Logger Logger=new();}
    class Logger {public int Warnings;public string Last;public void LogWarning(string t){Warnings++;Last=t;}}
}
namespace PreLuna.Captain.Tasks {
    record CaptainInteractionResult(int Id,string Label,bool Notice=false);
    static class SearchMarker {public static int Calls;public static bool Available=true;public static bool Place(UnityEngine.GameObject target,string title,NavMarkerOption option){Calls++;return Available;}}
    static class ItemRegion {public static bool SameSpace(Player.PlayerAgent p,AIGraph.AIG_CourseNode n)=>p.CourseNode.m_zone.Dimension==n.m_zone.Dimension&&p.CourseNode.m_zone.Layer.Pointer==n.m_zone.Layer.Pointer;}
    static class SearchTraversal {
        public static HashSet<long> Allowed=new(){1,2};
        public static bool InScope(AIGraph.AIG_CourseNode n,AIGraph.AIG_CourseNode o,int scope)=>n?.m_zone?.Layer!=null&&o?.m_zone?.Layer!=null
            &&n.m_zone.Layer.Pointer==o.m_zone.Layer.Pointer&&n.m_zone.Dimension==o.m_zone.Dimension
            &&SearchAccessPolicy.InScope(n.m_zone.Pointer.ToInt64(),n.m_area?.Pointer.ToInt64()??0,o.m_zone.Pointer.ToInt64(),o.m_area?.Pointer.ToInt64()??0,scope==0);
        public static HashSet<long> Reachable(Player.PlayerAgent a,AIGraph.AIG_CourseNode b,AIGraph.AIG_CourseNode c,int scope,out bool cut){cut=false;return Allowed;}
    }
}
static class KnownInteractionTests {
    static int checks;static long ids=1000;static Player.PlayerAgent player=new();
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static IntPtr Id()=>(IntPtr)(++ids);
    static T Add<T>(T c) where T:UnityEngine.Component {c.Pointer=Id();UnityEngine.Resources.Objects.Add(c);return c;}
    static InteractionCandidate Generator(){var g=Add(new LevelGeneration.LG_PowerGenerator_Core{m_serialNumber=123,m_stateReplicator=new(){Pointer=Id(),m_currentState=new(){status=LevelGeneration.ePowerGeneratorStatus.UnPowered}}});return new(g,InteractionPointKind.Generator,null);}
    static InteractionCandidate Mission(eCarryItemInsertTargetType type=eCarryItemInsertTargetType.Crate){var p=Add(new LevelGeneration.LG_InteractWorldEventTrigger{Parent=new LevelGeneration.LG_Area(),m_insertType=type,m_worldEventTriggerSync=new(){m_stateReplicator=new(){Pointer=Id()}}});return new(p,InteractionPointKind.WorldEvent,null);}
    static Dictionary<long,AIGraph.AIG_CourseNode> Nodes()=>new(){{1,player.CourseNode}};
    static void Main(){
        var g=Generator();Check(KnownInteractionIndex.Resolve(g,out var node)&&node.Pointer==(IntPtr)1,"发电机读取真实SpawnNode");
        Check(KnownInteractionIndex.Candidates(Nodes(),0,out var cut).Count==1&&!cut&&UnityEngine.Resources.Reads==3,"候选枚举三类真实场景，不启动动作");
        Check(!KnownInteractionIndex.HasState&&KnownInteractionIndex.Capture(player).Results.Count==0,"读取候选不等于发现");
        Check(KnownInteractionIndex.Observe(g),"即时查询登记对象");var snap=KnownInteractionIndex.Capture(player);int id=snap.Targets.Keys.Single();var saved=snap.Targets[id];
        Check(snap.Results.Single().Label.Contains("发电机接口 #123")&&snap.Details[id].Contains("未供电")&&snap.Details[id].Contains("只读"),"编号供电状态与只读说明来自真实字段");
        Check(KnownInteractionIndex.Current(id,saved)&&KnownInteractionIndex.Accessible(player,saved),"已登记身份可用且限定当前可达图");
        KnownInteractionIndex.Observe(g);Check(KnownInteractionIndex.Capture(player).Targets.Keys.Single()==id,"重复登记去重");
        ((LevelGeneration.LG_PowerGenerator_Core)g.Value).m_stateReplicator.m_currentState=new(){status=LevelGeneration.ePowerGeneratorStatus.Powered};
        Check(KnownInteractionIndex.Capture(player).Details[id].Contains("已供电"),"重开结果更新同步状态而不修改状态");
        Check(KnownInteractionIndex.Mark(g)&&SearchMarker.Calls==1,"标记复用共享初始化流程");SearchMarker.Available=false;
        Check(!KnownInteractionIndex.Mark(g)&&KnownInteractionIndex.Current(id,saved),"标记层不可用仍保留记录");SearchMarker.Available=true;
        SearchTraversal.Allowed.Clear();Check(!KnownInteractionIndex.Accessible(player,saved)&&KnownInteractionIndex.Capture(player).Details[id].Contains("封锁"),"重新封锁后禁止重新标记，保留历史详情");SearchTraversal.Allowed.Add(1);
        var mission=Mission();KnownInteractionIndex.Observe(mission);var ms=KnownInteractionIndex.Capture(player);int mid=ms.Targets.Single(x=>x.Value.Candidate.Value==mission.Value).Key;
        Check(ms.Results.Single(x=>x.Id==mid).Label.Contains("任务交付点（任务箱）")&&ms.Details[mid].Contains("未触发"),"接收类型确认交付用途，事件同步状态不冒充可操作权限");
        var actual=(LevelGeneration.LG_InteractWorldEventTrigger)mission.Value;actual.m_worldEventTriggerSync.m_stateReplicator.m_currentState=new(){HasBeenTriggered=true};actual.m_isToggle=true;
        Check(KnownInteractionIndex.Capture(player).Details[mid].Contains("已触发不代表任务完成"),"切换式事件不可把已触发当完成");
        foreach(var type in new[]{eCarryItemInsertTargetType.PowerCell,eCarryItemInsertTargetType.SmallHSU,eCarryItemInsertTargetType.None,eCarryItemInsertTargetType.Unknown}){
            var other=Mission(type);KnownInteractionIndex.Observe(other);var detail=KnownInteractionIndex.Capture(player).Results.Last().Label;
            Check(type is eCarryItemInsertTargetType.None or eCarryItemInsertTargetType.Unknown?detail.Contains("用途未确认"):detail.Contains("任务交付点"),"已知接收类型与未知用途区分 "+type);
        }
        actual.Parent=null;Check(!KnownInteractionIndex.Resolve(mission,out _),"没有实际区域的事件不按坐标猜其他维度节点");actual.Parent=new LevelGeneration.LG_Area();
        var owner=new LevelGeneration.LG_WorldEventObject{_ParentArea_k__BackingField=new()};actual.Parent=owner;
        Check(KnownInteractionIndex.Resolve(mission,out var ownerNode)&&ownerNode==owner._ParentArea_k__BackingField.m_courseNode,"事件物件绑定真实ParentArea，支持不在Area变换层级下的任务点");
        owner._ParentArea_k__BackingField=null;owner.Parent=new LevelGeneration.LG_Area();
        Check(!KnownInteractionIndex.Resolve(mission,out _),"事件物件未完成Setup时不回退猜测父层级");actual.Parent=new LevelGeneration.LG_Area();
        actual.Parent=g.Value;Check(!KnownInteractionIndex.Resolve(mission,out _),"发电机自己的子交互不重复登记");actual.Parent=new LevelGeneration.LG_Area();
        var hsu=Add(new LevelGeneration.LG_HSUActivator_Core{m_serialNumber=5,m_stateReplicator=new(){Pointer=Id()},_ObjectiveItemSolved_k__BackingField=true});
        var hc=new InteractionCandidate(hsu,InteractionPointKind.HSU,null);KnownInteractionIndex.Observe(hc);
        Check(KnownInteractionIndex.Capture(player).Results.Last().Label.Contains("已完成"),"HSU实际任务物件状态");
        actual.Parent=hsu;Check(!KnownInteractionIndex.Resolve(mission,out _),"HSU子交互不重复登记");actual.Parent=new LevelGeneration.LG_Area();
        var door=Add(new LevelGeneration.LG_WeakDoor{m_serialNumber=7,m_syncComp=new(){Pointer=Id()},_LastStatus_k__BackingField=LevelGeneration.eDoorStatus.Closed_LockedWithNoKey});
        door._Gate_k__BackingField=new(){_SpawnedDoor_k__BackingField=door};
        var portal=new AIGraph.AIG_CoursePortal{m_door=door,m_nodeA=player.CourseNode,m_nodeB=new(){Pointer=(IntPtr)2}};player.CourseNode.m_portals.Add(portal);portal.m_nodeB.m_portals.Add(portal);
        var nodes=Nodes();nodes[2]=portal.m_nodeB;
        int beforeReads=UnityEngine.Resources.Reads;
        var doors=KnownInteractionIndex.Candidates(nodes,1,out cut);Check(doors.Count==1&&!cut&&UnityEngine.Resources.Reads==beforeReads,"门按可达节点门户读取，双侧去重、不做场景全枚举");
        KnownInteractionIndex.Observe(doors[0]);Check(KnownInteractionIndex.Capture(player).Results.Last().Label.Contains("普通门 #7")&&KnownInteractionIndex.Capture(player).Results.Last().Label.Contains("锁定"),"普通门实际编号锁定状态");
        var security=Add(new LevelGeneration.LG_SecurityDoor{m_serialNumber=8,m_syncComp=new(){Pointer=Id()},_LastStatus_k__BackingField=LevelGeneration.eDoorStatus.Closed_LockedWithChainedPuzzle_Alarm});
        security._Gate_k__BackingField=new(){_SpawnedDoor_k__BackingField=security};var sp=new AIGraph.AIG_CoursePortal{m_door=security,m_nodeA=player.CourseNode,m_nodeB=new(){Pointer=(IntPtr)99}};player.CourseNode.m_portals.Add(sp);
        var sc=KnownInteractionIndex.Candidates(Nodes(),1,out cut).Last();KnownInteractionIndex.Observe(sc);
        Check(KnownInteractionIndex.Capture(player).Results.Last().Label.Contains("安全门 #8")&&KnownInteractionIndex.Capture(player).Results.Last().Label.Contains("不启动扫描"),"安全门只记录可达侧而非跨越门户");
        foreach(var candidate in new[]{g,hc,mission,doors[0],sc})foreach(int filter in Enumerable.Range(0,4))Check(KnownInteractionIndex.Matches(candidate,filter)==(filter==0||filter==1&&candidate.Kind is InteractionPointKind.OrdinaryDoor or InteractionPointKind.SecurityDoor||filter==2&&candidate.Kind==InteractionPointKind.Generator||filter==3&&candidate.Kind is InteractionPointKind.HSU or InteractionPointKind.WorldEvent),"类型筛选 "+candidate.Kind+" / "+filter);
        var isolated=new LevelGeneration.LG_WeakDoor{Pointer=Id(),m_syncComp=new(){Pointer=Id()},_Gate_k__BackingField=door._Gate_k__BackingField};
        Check(!KnownInteractionIndex.Resolve(new(isolated,InteractionPointKind.OrdinaryDoor,player.CourseNode,portal),out _),"门户与门绑定不一致拒绝对象");
        var old=KnownInteractionIndex.Capture(player);var oldId=old.Targets.Single(x=>x.Value.Candidate.Value==g.Value).Key;var oldSaved=old.Targets[oldId];
        ((LevelGeneration.LG_PowerGenerator_Core)g.Value).m_stateReplicator.Pointer=Id();Check(!KnownInteractionIndex.Current(oldId,oldSaved),"同步身份变化拒绝旧菜单");KnownInteractionIndex.Observe(g);
        var newer=KnownInteractionIndex.Capture(player);int newId=newer.Targets.Single(x=>x.Value.Candidate.Value==g.Value).Key;Check(newId!=oldId,"重用地址获得新发现身份");
        ((LevelGeneration.LG_PowerGenerator_Core)g.Value).m_serialNumber++;Check(!KnownInteractionIndex.Current(newId,newer.Targets[newId]),"原版编号变化拒绝旧身份");
        foreach(int invalid in Enumerable.Range(0,7)){
            var bad=Generator();var value=(LevelGeneration.LG_PowerGenerator_Core)bad.Value;
            if(invalid==0)value.gameObject.activeInHierarchy=false;else if(invalid==1)value.gameObject.scene.Valid=false;else if(invalid==2)value.m_stateReplicator=null;
            else if(invalid==3)value.m_terminalItem=null;else if(invalid==4)value._SpawnNode_k__BackingField=null;else if(invalid==5)value.transform.position=new(){x=float.NaN};else value._SpawnNode_k__BackingField.m_zone.Layer=null;
            Check(!KnownInteractionIndex.Resolve(bad,out _),"未初始化／隐藏／预制件／非有限位置过滤 "+invalid);
        }
        KnownInteractionIndex.Reset();Check(!KnownInteractionIndex.HasState,"生命周期清理");KnownInteractionIndex.Observe(g);Check(KnownInteractionIndex.Capture(player).Targets.Keys.Single()>newId,"不复用旧菜单编号");
        var current=KnownInteractionIndex.Capture(player);int fid=current.Targets.Keys.Single();LevelGeneration.Builder.CurrentFloor.Pointer=(IntPtr)101;
        Check(!KnownInteractionIndex.Current(fid,current.Targets[fid]),"关卡身份变化拒绝旧目标");KnownInteractionIndex.Reset();
        foreach(int outside in Enumerable.Range(0,3)){
            var foreign=Generator();var fn=((LevelGeneration.LG_PowerGenerator_Core)foreign.Value)._SpawnNode_k__BackingField;
            if(outside==0)fn.m_zone.Pointer=(IntPtr)2;else if(outside==1)fn.m_zone.Layer.Pointer=(IntPtr)2;else fn.m_zone.Dimension=2;
            KnownInteractionIndex.Observe(foreign);Check(KnownInteractionIndex.Capture(player).Results.Count==0,"列表不混入其他Zone／层／维度 "+outside);
        }
        SNetwork.SNet.IsMaster=false;Check(KnownInteractionIndex.Candidates(Nodes(),0,out cut).Count==0,"非房主不枚举");SNetwork.SNet.IsMaster=true;
        GameStateManager.CurrentStateName=eGameStateName.Lobby;Check(KnownInteractionIndex.Capture(player).Results.Count==0,"大厅不查询");GameStateManager.CurrentStateName=eGameStateName.InLevel;
        KnownInteractionIndex.Reset();for(int i=0;i<65;i++)KnownInteractionIndex.Observe(Generator());var capped=KnownInteractionIndex.Capture(player);
        Check(capped.Targets.Count==64&&capped.Results.Count==65&&capped.Results.Last().Notice,"64项结果限额和只读提示");
        UnityEngine.Resources.Objects.Clear();for(int i=0;i<4097;i++)Generator();Check(KnownInteractionIndex.Candidates(Nodes(),2,out cut).Count==4096&&cut,"4096候选上限，查询明确部分统计");
        Console.WriteLine("PASS: "+checks+" 项真实交互点索引／同步身份／用途／当前可达侧／边界／缓存与限额断言；原生场景使用替身，未执行游戏任务。");
    }
}
