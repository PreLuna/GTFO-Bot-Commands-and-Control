using System;
using System.Collections.Generic;
using System.Linq;
using PreLuna.Captain.Tasks;
namespace UnityEngine {
    class GameObject { public bool activeInHierarchy=true; }
    static class Time { public static float time=10; }
}
namespace AIGraph {
    class Zone {public IntPtr Pointer=(IntPtr)1;public int Space;}
    class AIG_CourseNode {public IntPtr Pointer=(IntPtr)1;public Zone m_zone=new();public long Room=1;}
}
namespace SNetwork {static class SNet {public static bool IsMaster=true;}}
namespace Player {
    class Owner {public bool IsBot;public IntPtr Pointer=(IntPtr)10;}
    class PlayerAgent {public Owner Owner=new();public AIGraph.AIG_CourseNode CourseNode=new();public int PlayerSlotIndex;}
    // Deliberately has no movement, teleport, action, or broker APIs.
    class PlayerAIBot { }
}
enum InventorySlot { ResourcePack=4, Consumable=5, Heavy=6, TaskItem=7, Carry=8, Weapon=1 }
enum eGameStateName {InLevel,Lobby}
static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
namespace LevelGeneration {
    static class Builder {public static object CurrentFloor=new();}
    enum eCM_GuiObjectStatus {Normal}
    class Revealer {public uint MapGUIObjID;}
    class LG_WeakResourceContainer {public IntPtr Pointer;public AIGraph.AIG_CourseNode Node=new();public bool ISOpen,Locked;
        public Revealer m_mapLookatRevealer=new();public T TryCast<T>() where T:class=>this as T;}
    class Container {public IntPtr Pointer;public LG_WeakResourceContainer m_core=new();}
    class ItemData {public InventorySlot slot;}
    class ItemInLevel {
        public IntPtr Pointer;public UnityEngine.GameObject gameObject=new();public object ItemDataBlock=new();
        public AIGraph.AIG_CourseNode Node=new();public bool Placed=true,Throw,Open=true;
        public Container container;public string PublicName="弹药包";public ItemData Data=new();
        public ItemData Get_pItemData(){if(Throw)throw new Exception("bad item");return Data;}
    }
    class LG_ComputerTerminal {public IntPtr Pointer;public AIGraph.AIG_CourseNode Node=new();public bool Valid=true;}
}
enum NavMarkerOption {Empty,TerminalTitleDistance,WaypointTitleDistance}
class NavMarker {
    public string name="query",Title; public object m_fadeRoutine; public bool Initialized,Visible,Throw;
    public List<string> Order=new();
    public void SetVisualStates(NavMarkerOption a,NavMarkerOption b,NavMarkerOption c,NavMarkerOption d){Initialized=true;Order.Add("states");}
    public void SetTitle(string title){Title=title;Order.Add("title");}
    public void SetVisible(bool value){if(!Initialized)throw new NullReferenceException("stateOptions");if(Throw)throw new Exception("visibility");Visible=value;Order.Add("visible");}
}
class NavMarkerLayer {
    public List<NavMarker> Prepared=new();
    public int Calls,Removed;public bool Throw,Reuse,CleanupThrow;public NavMarker Last;
    public NavMarker PrepareGenericMarker(UnityEngine.GameObject root){Calls++;if(Reuse&&Last!=null)return Last;Last=new(){Throw=Throw};Prepared.Add(Last);return Last;}
    public object FadeMarkerOverTime(NavMarker a,string b,float c,float d,bool e)=>new();
    public void RemoveMarker(NavMarker m){Removed++;if(CleanupThrow)throw new Exception("cleanup");}
}
static class GuiManager {public static NavMarkerLayer NavMarkerLayer=new();}
static class CoroutineManager {public static int Stops;public static object StartCoroutine(object a,object b)=>new();public static void StopCoroutine(object a){Stops++;}}
static class MapDataManager {public static int Calls;public static bool Throw;public static void WantToSetGUIObjVisible(uint id,LevelGeneration.eCM_GuiObjectStatus state){Calls++;if(Throw)throw new Exception("map");}}
namespace PreLuna.Captain {
    static class Plugin {public static Logger Logger=new();public static Catalog Catalog=new();}
    class Logger {public List<string> Info=new(),Warnings=new();public void LogInfo(string text)=>Info.Add(text);public void LogWarning(string text)=>Warnings.Add(text);}
    class Catalog {public string TranslateItems(string text)=>text;}
}
namespace PreLuna.Captain.Tasks {
    enum SupplySource {RobotSearch}
    static class KnownSupplyIndex {public static bool SuppressQueryMarker;public static HashSet<IntPtr> Known=new();public static void Observe(LevelGeneration.ItemInLevel i,SupplySource s)=>Known.Add(i.Pointer);}
    static class ItemWorldIndex {public static List<LevelGeneration.ItemInLevel> Items=new();}
    static class AutoPickupAdapter {public static bool Open(LevelGeneration.ItemInLevel i)=>i.Open;}
    static class ItemTaskAdapter {public static List<string> Messages=new();public static void Feedback(string t)=>Messages.Add(t);}
    enum TaskBoardState {Complete}
    class Journal {public int Count;public string Detail;public void Record(object id,int slot,long owner,string bot,string task,string label,TaskBoardState state,string stage,string detail,float now){Count++;Detail=detail;}}
    static class TaskBoard {public static Journal Journal=new();}
    static class ItemRegion {
        public static bool SameSpace(Player.PlayerAgent source,AIGraph.AIG_CourseNode node)=>source?.CourseNode?.m_zone?.Space==node?.m_zone?.Space;
        public static bool TryResolve(LevelGeneration.ItemInLevel item,out AIGraph.AIG_CourseNode node){node=item.Node;return item.Placed&&node!=null;}
    }
    static class SearchTraversal {
        public static HashSet<long> Allowed=new(){1};public static bool Truncated;
        public static bool InScope(AIGraph.AIG_CourseNode n,AIGraph.AIG_CourseNode o,int scope)=>n!=null&&o!=null&&n.m_zone.Space==o.m_zone.Space
            &&SearchAccessPolicy.InScope(n.m_zone.Pointer.ToInt64(),n.Room,o.m_zone.Pointer.ToInt64(),o.Room,scope==0);
        public static HashSet<long> Reachable(Player.PlayerAgent s,AIGraph.AIG_CourseNode a,AIGraph.AIG_CourseNode b,int scope,out bool cut,Dictionary<long,AIGraph.AIG_CourseNode> nodes=null){cut=Truncated;if(nodes!=null)foreach(var key in Allowed)nodes[key]=new(){Pointer=(IntPtr)key};return Allowed;}
    }
    static class KnownTerminalIndex {
        public static List<LevelGeneration.LG_ComputerTerminal> List=new();public static HashSet<IntPtr> Known=new();public static int Marked;
        public static bool Truncated;public static List<LevelGeneration.LG_ComputerTerminal> Candidates(out bool cut){cut=Truncated;return List;}
        public static bool Resolve(LevelGeneration.LG_ComputerTerminal t,out AIGraph.AIG_CourseNode n){n=t.Node;return t.Valid;}
        public static bool Observe(LevelGeneration.LG_ComputerTerminal t)=>Known.Add(t.Pointer)||Known.Contains(t.Pointer);
        public static bool Mark(LevelGeneration.LG_ComputerTerminal t){Marked++;return true;}
    }
    static class KnownContainerIndex {
        public static List<LevelGeneration.LG_WeakResourceContainer> List=new();public static HashSet<IntPtr> Known=new();public static int Marked;
        public static List<LevelGeneration.LG_WeakResourceContainer> Candidates(out bool cut){cut=false;return List;}
        public static bool Resolve(LevelGeneration.LG_WeakResourceContainer box,out AIGraph.AIG_CourseNode node){node=box.Node;return node!=null;}
        public static bool Matches(LevelGeneration.LG_WeakResourceContainer box,int filter)=>filter==0||filter==1&&!box.ISOpen||filter==2&&box.Locked&&!box.ISOpen;
        public static bool Observe(LevelGeneration.LG_WeakResourceContainer box)=>Known.Add(box.Pointer)||Known.Contains(box.Pointer);
        public static bool Mark(LevelGeneration.LG_WeakResourceContainer box){Marked++;return true;}
    }
    record InteractionCandidate(LevelGeneration.LG_ComputerTerminal Value,int Kind);
    static class KnownInteractionIndex {
        public static List<InteractionCandidate> List=new();public static HashSet<IntPtr> Known=new();public static int Marked,NodeCount;
        public static List<InteractionCandidate> Candidates(IReadOnlyDictionary<long,AIGraph.AIG_CourseNode> nodes,int filter,out bool cut){cut=false;NodeCount=nodes.Count;return List;}
        public static bool Resolve(InteractionCandidate t,out AIGraph.AIG_CourseNode n){n=t.Value.Node;return t.Value.Valid;}
        public static bool Matches(InteractionCandidate t,int filter)=>filter==0||t.Kind==filter;
        public static bool Observe(InteractionCandidate t)=>Known.Add(t.Value.Pointer)||Known.Contains(t.Value.Pointer);
        public static bool Mark(InteractionCandidate t){Marked++;return true;}
    }
}
static class SearchTaskTests {
    static int checks; static Player.PlayerAgent player=new();
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static LevelGeneration.ItemInLevel Item(int id,InventorySlot slot=InventorySlot.ResourcePack,long node=1,long zone=1,long room=1,int space=0)=>new(){
        Pointer=(IntPtr)id,Data=new(){slot=slot},Node=new(){Pointer=(IntPtr)node,m_zone=new(){Pointer=(IntPtr)zone,Space=space},Room=room}};
    static void Reset(){
        SearchTaskAdapter.Reset();UnityEngine.Time.time+=2;KnownSupplyIndex.Known.Clear();ItemWorldIndex.Items.Clear();ItemTaskAdapter.Messages.Clear();
        TaskBoard.Journal=new();SearchTraversal.Allowed=new(){1,2};SearchTraversal.Truncated=false;GuiManager.NavMarkerLayer=new();
        SNetwork.SNet.IsMaster=true;GameStateManager.CurrentStateName=eGameStateName.InLevel;LevelGeneration.Builder.CurrentFloor=new();player=new();
        KnownTerminalIndex.List.Clear();KnownTerminalIndex.Known.Clear();KnownTerminalIndex.Marked=0;KnownTerminalIndex.Truncated=false;
        KnownContainerIndex.List.Clear();KnownContainerIndex.Known.Clear();KnownContainerIndex.Marked=0;
        KnownInteractionIndex.List.Clear();KnownInteractionIndex.Known.Clear();KnownInteractionIndex.Marked=KnownInteractionIndex.NodeCount=0;
        MapDataManager.Calls=0;MapDataManager.Throw=false;KnownSupplyIndex.SuppressQueryMarker=false;
    }
    static void Query(int scope=1,int filter=0,SearchTargetKind kind=SearchTargetKind.Supplies)=>SearchTaskAdapter.Issue(player,player.CourseNode,scope,filter,kind);
    static void Main(){
        Reset();var pack=Item(1);var consumable=Item(2,InventorySlot.Consumable,2);consumable.Open=false;consumable.gameObject.activeInHierarchy=false;
        var box=new LevelGeneration.Container{Pointer=(IntPtr)50};pack.container=consumable.container=box;
        ItemWorldIndex.Items.AddRange(new[]{pack,consumable,pack,Item(3,InventorySlot.TaskItem),Item(4,node:3),Item(5,zone:2),Item(6,space:1)});
        Query();Check(KnownSupplyIndex.Known.SetEquals(new[]{(IntPtr)1,(IntPtr)2,(IntPtr)3}),"登记范围内普通与任务物品，去重排除封锁／其他Zone／分层");
        Check(TaskBoard.Journal.Count==1&&TaskBoard.Journal.Detail.Contains("共 3 件")&&TaskBoard.Journal.Detail.Contains("任务／特殊物品 1"),"统计区分任务槽且立即完成");
        Check(GuiManager.NavMarkerLayer.Calls==3&&MapDataManager.Calls==1,"逐物品标记，柜子地图仅揭示一次");
        Check(GuiManager.NavMarkerLayer.Prepared.Exists(m=>m.Title.Contains("柜内")),"关闭柜内与未激活物品也能统计，并标明柜内");
        Check(GuiManager.NavMarkerLayer.Last.Order.SequenceEqual(new[]{"states","title","visible"}),"按原版初始化状态→标题→可见顺序，复现旧空引用条件");
        Check(!SearchTaskAdapter.HasState&&!SearchTaskAdapter.Owns(new())&&SearchTaskAdapter.Status(new())==null,"没有移动会话或机器人控制权占用");
        SearchTaskAdapter.Tick();Check(TaskBoard.Journal.Count==1,"后续tick无重复统计或移动");
        Query();Check(TaskBoard.Journal.Count==1,"重复快速提交抑制，不重复造标记");
        UnityEngine.Time.time+=2;Query();Check(TaskBoard.Journal.Count==2&&KnownSupplyIndex.Known.Count==3,"再次搜寻刷新但结果不重复");
        Check(!SearchTaskAdapter.Cancel(new(),true),"取消其他任务时不能错误触发搜索返回");
        Reset();ItemWorldIndex.Items.AddRange(new[]{Item(1),Item(2,InventorySlot.Consumable)});Query(filter:1);Check(KnownSupplyIndex.Known.SetEquals(new[]{(IntPtr)1}),"资源包筛选");
        Reset();ItemWorldIndex.Items.AddRange(new[]{Item(1),Item(2,InventorySlot.Consumable)});Query(filter:2);Check(KnownSupplyIndex.Known.SetEquals(new[]{(IntPtr)2}),"消耗品筛选");
        Reset();ItemWorldIndex.Items.AddRange(new[]{Item(1,InventorySlot.TaskItem),Item(2,InventorySlot.Carry),Item(3,InventorySlot.Heavy),Item(4,InventorySlot.Weapon)});Query(filter:3);Check(KnownSupplyIndex.Known.SetEquals(new[]{(IntPtr)1}),"口袋任务筛选独立于手持与武器");
        Reset();ItemWorldIndex.Items.AddRange(new[]{Item(1,InventorySlot.TaskItem),Item(2,InventorySlot.Carry),Item(3,InventorySlot.Heavy),Item(4,InventorySlot.Weapon)});Query(filter:4);Check(KnownSupplyIndex.Known.SetEquals(new[]{(IntPtr)2,(IntPtr)3}),"特殊手持与重型槽筛选，不读弹药或收集武器");
        Reset();ItemWorldIndex.Items.AddRange(new[]{Item(1),Item(2,node:2,room:2)});Query(scope:0);Check(KnownSupplyIndex.Known.SetEquals(new[]{(IntPtr)1}),"房间边界冻结");
        foreach(var invalid in Enumerable.Range(0,7)){
            Reset();ItemWorldIndex.Items.Add(Item(1));
            if(invalid==0)SNetwork.SNet.IsMaster=false;else if(invalid==1)GameStateManager.CurrentStateName=eGameStateName.Lobby;
            else if(invalid==2)LevelGeneration.Builder.CurrentFloor=null;else if(invalid==3)player.Owner.IsBot=true;
            var origin=player.CourseNode;if(invalid==4)origin=new(){m_zone=new(){Pointer=(IntPtr)9}};
            SearchTaskAdapter.Issue(player,origin,invalid==5?2:1,invalid==6?5:0);
            Check(KnownSupplyIndex.Known.Count==0&&TaskBoard.Journal.Count==0,"错误环境与过期范围拒绝 "+invalid);
        }
        Reset();var corrupt=Item(1);corrupt.Throw=true;ItemWorldIndex.Items.AddRange(new[]{corrupt,Item(2)});Query();
        Check(KnownSupplyIndex.Known.SetEquals(new[]{(IntPtr)2}),"单个损坏对象不能阻断后续统计");
        Check(PreLuna.Captain.Plugin.Logger.Warnings.Any(s=>s.Contains("bad item")),"保留实际错误诊断");
        Reset();GuiManager.NavMarkerLayer.Throw=true;KnownSupplyIndex.SuppressQueryMarker=true;ItemWorldIndex.Items.Add(Item(1));Query();
        Check(KnownSupplyIndex.Known.Count==1&&TaskBoard.Journal.Count==1,"标记失败仍即时完成并保留结果");
        Check(GuiManager.NavMarkerLayer.Removed==1&&KnownSupplyIndex.SuppressQueryMarker,"失败标记单独清理，嵌套来源标志恢复");
        Reset();var markerRoot=new UnityEngine.GameObject();Check(SearchMarker.Place(markerRoot,"a",NavMarkerOption.WaypointTitleDistance),"直接标记正常初始化");
        GuiManager.NavMarkerLayer.Reuse=true;int stops=CoroutineManager.Stops;SearchMarker.Place(markerRoot,"b",NavMarkerOption.WaypointTitleDistance);
        Check(CoroutineManager.Stops==stops+1&&GuiManager.NavMarkerLayer.Last.Title=="b","原生返回同一标记时，取消旧淡出再重新计时，不叠加协程");
        Reset();GuiManager.NavMarkerLayer.Throw=GuiManager.NavMarkerLayer.CleanupThrow=true;
        SearchMarker.Place(markerRoot,"a",NavMarkerOption.WaypointTitleDistance);
        Check(PreLuna.Captain.Plugin.Logger.Warnings.Last().Contains("visibility")&&PreLuna.Captain.Plugin.Logger.Warnings.Last().Contains("cleanup"),"清理失败诊断必须保留原始创建错误");
        Reset();var stored=Item(1);stored.container=new(){Pointer=(IntPtr)50};MapDataManager.Throw=true;ItemWorldIndex.Items.AddRange(new[]{stored,Item(2)});Query();
        Check(KnownSupplyIndex.Known.Count==2&&GuiManager.NavMarkerLayer.Calls==2,"地图失败不阻断标记或后续物资");
        Reset();GuiManager.NavMarkerLayer=null;ItemWorldIndex.Items.Add(Item(1));Query();Check(KnownSupplyIndex.Known.Count==1&&TaskBoard.Journal.Count==1,"HUD层不存在时仍可统计");
        Reset();for(int i=1;i<=150;i++)ItemWorldIndex.Items.Add(Item(i));Query();
        Check(KnownSupplyIndex.Known.Count==150&&TaskBoard.Journal.Detail.Contains("共 150 件"),"80个标记上限不截断真实统计");
        Check(GuiManager.NavMarkerLayer.Calls==80,"一次最多创建80个临时标记，避免挤满HUD");
        Reset();GuiManager.NavMarkerLayer.Throw=true;for(int i=1;i<=150;i++)ItemWorldIndex.Items.Add(Item(i));Query();
        Check(GuiManager.NavMarkerLayer.Calls==80&&GuiManager.NavMarkerLayer.Removed==80&&KnownSupplyIndex.Known.Count==150,"上限按尝试数而非成功数；故障时不无限造标记");
        Reset();SearchTraversal.Truncated=true;ItemWorldIndex.Items.Add(Item(1));Query();Check(TaskBoard.Journal.Detail.Contains("可能不完整"),"连接图检查达到上限如实报告");
        Reset();for(int i=1;i<=8200;i++)ItemWorldIndex.Items.Add(Item(i));Query();Check(KnownSupplyIndex.Known.Count==8192&&TaskBoard.Journal.Detail.Contains("可能不完整"),"极大索引有明确有限处理提示");
        Reset();var gone=Item(1);gone.Placed=false;ItemWorldIndex.Items.Add(gone);Query();Check(KnownSupplyIndex.Known.Count==0&&TaskBoard.Journal.Count==1,"空结果完成而不捡起已消失或已携带物品");
        Reset();KnownTerminalIndex.List.AddRange(new[]{new LevelGeneration.LG_ComputerTerminal{Pointer=(IntPtr)1},new(){Pointer=(IntPtr)2,Node=new(){Pointer=(IntPtr)3}},new(){Pointer=(IntPtr)3,Node=new(){m_zone=new(){Pointer=(IntPtr)2}}}});Query(kind:SearchTargetKind.Terminals);
        Check(KnownTerminalIndex.Known.SetEquals(new[]{(IntPtr)1})&&KnownTerminalIndex.Marked==1,"终端同样直接统计而不移动，遵守可达范围");
        Check(TaskBoard.Journal.Count==1&&TaskBoard.Journal.Detail.Contains("终端"),"终端即时完成记录");
        Reset();KnownContainerIndex.List.AddRange(new[]{new LevelGeneration.LG_WeakResourceContainer{Pointer=(IntPtr)1},new(){Pointer=(IntPtr)2,ISOpen=true},new(){Pointer=(IntPtr)3,Locked=true},new(){Pointer=(IntPtr)4,Node=new(){Pointer=(IntPtr)3}}});
        Query(kind:SearchTargetKind.Containers);Check(KnownContainerIndex.Known.Count==3&&KnownContainerIndex.Marked==3,"容器即时统计包括空箱、开箱、有锁箱，排除封锁路线");
        Check(TaskBoard.Journal.Detail.Contains("容器")&&!SearchTaskAdapter.HasState,"容器查询立即完成，不占机器人动作");
        UnityEngine.Time.time+=2;KnownContainerIndex.Known.Clear();Query(filter:1,kind:SearchTargetKind.Containers);Check(KnownContainerIndex.Known.SetEquals(new[]{(IntPtr)1,(IntPtr)3}),"未开启过滤");
        UnityEngine.Time.time+=2;KnownContainerIndex.Known.Clear();Query(filter:2,kind:SearchTargetKind.Containers);Check(KnownContainerIndex.Known.SetEquals(new[]{(IntPtr)3}),"有锁过滤");
        Reset();KnownInteractionIndex.List.AddRange(new[]{new InteractionCandidate(new(){Pointer=(IntPtr)1},1),new(new(){Pointer=(IntPtr)2},3),new(new(){Pointer=(IntPtr)3,Node=new(){Pointer=(IntPtr)99}},3),new(new(){Pointer=(IntPtr)4,Node=new(){m_zone=new(){Pointer=(IntPtr)2}}},2)});
        Query(kind:SearchTargetKind.Interactions);Check(KnownInteractionIndex.Known.SetEquals(new[]{(IntPtr)1,(IntPtr)2})&&KnownInteractionIndex.NodeCount==2,"交互点复用限定可达图，排除封锁后及其他Zone");
        Check(TaskBoard.Journal.Detail.Contains("交互点")&&!SearchTaskAdapter.HasState&&KnownInteractionIndex.Marked==2,"交互点立即完成，仅标记不触发任务");
        UnityEngine.Time.time+=2;KnownInteractionIndex.Known.Clear();Query(filter:3,kind:SearchTargetKind.Interactions);Check(KnownInteractionIndex.Known.SetEquals(new[]{(IntPtr)2}),"任务接口类型筛选3合法");
        UnityEngine.Time.time+=2;int journalBefore=TaskBoard.Journal.Count;Query(filter:4,kind:SearchTargetKind.Interactions);Check(TaskBoard.Journal.Count==journalBefore,"未支持的交互类型参数拒绝");
        Reset();Query(filter:5);Check(TaskBoard.Journal.Count==0,"新物资类型仍拒绝超出范围的筛选参数");
        Reset();for(int i=1;i<=150;i++)KnownInteractionIndex.List.Add(new(new(){Pointer=(IntPtr)i},2));Query(kind:SearchTargetKind.Interactions);
        Check(KnownInteractionIndex.Known.Count==150&&KnownInteractionIndex.Marked==80&&TaskBoard.Journal.Detail.Contains("显示限额"),"交互点标记限额不丢统计，明确结果展示限额");
        Console.WriteLine("PASS: "+checks+" 项真实即时搜索适配器／标记初始化／限额／失败隔离断言；场景替身未提供任何机器人移动API。");
    }
}
