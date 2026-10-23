using System;
using System.Collections.Generic;
using System.Linq;
using PreLuna.Captain.Tasks;
namespace UnityEngine {
    class Object {public virtual T TryCast<T>() where T:class=>this as T;}
    class Scene {public bool Valid=true;public bool IsValid()=>Valid;}
    class GameObject {public bool activeInHierarchy=true;public Scene scene=new();}
    class Component:Object {public IntPtr Pointer=(IntPtr)9;}
    class Transform {public object position="(1,2,3)";}
    static class Time {public static float time=10;}
    static class Resources {public static List<Object> Objects=new();public static int Reads;public static IEnumerable<Object> FindObjectsOfTypeAll(Type type){Reads++;return Objects;}}
}
namespace Il2CppInterop.Runtime {static class Il2CppType {public static Type Of<T>()=>typeof(T);}}
namespace AIGraph {
    class Layer {public IntPtr Pointer=(IntPtr)1;}
    class Zone {public IntPtr Pointer=(IntPtr)1;public Layer Layer=new();public int Dimension;public string AliasName="ZONE 1";}
    class NavInfo {public int Number=1;}
    class Area {public IntPtr Pointer=(IntPtr)1;public NavInfo m_navInfo=new();}
    class AIG_CourseNode {public IntPtr Pointer=(IntPtr)1;public Zone m_zone=new();public Area m_area=new();}
}
namespace Player {class PlayerAgent {public AIGraph.AIG_CourseNode CourseNode=new();}}
namespace SNetwork {static class SNet {public static bool IsMaster=true;}}
enum eGameStateName {InLevel,Lobby}
static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
namespace LevelGeneration {
    class Floor {public IntPtr Pointer=(IntPtr)100;}
    static class Builder {public static Floor CurrentFloor=new();}
    class LG_WeakLock {public OpenLockKind Kind;}
    class Revealer {public uint MapGUIObjID=1;}
    class LG_WeakResourceContainer:UnityEngine.Object {
        public IntPtr Pointer;public int m_serialNumber;public bool m_isLocker,ISOpen,Locked,Throw;
        public UnityEngine.GameObject gameObject=new();public UnityEngine.Transform transform=new();
        public object m_terminalItem=new(),m_storage=new(),m_sync=new();
        public UnityEngine.Component m_syncComp=new(),m_storageComp=new LG_ResourceContainer_Storage();
        public AIGraph.AIG_CourseNode SpawnNode=new();public LG_WeakLock WeakLockComponent;public Revealer m_mapLookatRevealer=new();
        public List<ItemInLevel> Items=new();public bool IsLocked(){if(Throw)throw new Exception("broken box");return Locked;}
        public IEnumerable<T> GetComponentsInChildren<T>(bool inactive) where T:class {if(Throw)throw new Exception("broken box");return Items.OfType<T>();}
    }
    class LG_ResourceContainer_Storage:UnityEngine.Component {public int m_numberOfItems;public LG_WeakResourceContainer m_core;}
    class ItemInLevel {public IntPtr Pointer;public object ItemDataBlock=new();public bool Placed=true;public LG_ResourceContainer_Storage container;public string PublicName="弹药包";}
    enum eCM_GuiObjectStatus {Normal}
}
enum NavMarkerOption {ResourceLockerTitleDistance,ResourceBoxTitleDistance}
static class MapDataManager {public static int Calls;public static bool Throw;public static void WantToSetGUIObjVisible(uint id,LevelGeneration.eCM_GuiObjectStatus state){if(Throw)throw new Exception("map failure");Calls++;}}
namespace PreLuna.Captain {
    static class Plugin {public static Logger Logger=new();public static Catalog Catalog=new();}
    class Logger {public int Warnings;public string Last;public void LogWarning(string t){Warnings++;Last=t;}}
    class Catalog {public string TranslateItems(string t)=>t;}
}
namespace PreLuna.Captain.Tasks {
    enum OpenLockKind {None,Melee,Hackable,Unsupported}
    record CaptainContainerResult(int Id,string Label,string Blocked=null,bool NeedsMelee=false,int NeededMelters=0,bool Notice=false);
    class MarkedOpenTarget {
        public LevelGeneration.LG_WeakResourceContainer Box;public bool NeedsMelee=>Kind(Box.WeakLockComponent)==OpenLockKind.Melee;
        public int NeededMelters=>Kind(Box.WeakLockComponent)==OpenLockKind.Hackable?1:0;
        public string Restriction=>Kind(Box.WeakLockComponent)==OpenLockKind.Unsupported?"未知锁":null;
        public static OpenLockKind Kind(LevelGeneration.LG_WeakLock weak)=>weak?.Kind??OpenLockKind.None;
        public static MarkedOpenTarget FromContainer(Player.PlayerAgent source,LevelGeneration.LG_WeakResourceContainer box,AIGraph.AIG_CourseNode node)=>new(){Box=box};
    }
    static class SearchMarker {public static int Calls;public static bool Available=true;public static bool Place(UnityEngine.GameObject target,string title,NavMarkerOption options){Calls++;return Available;}}
    static class ItemRegion {
        public static bool SameSpace(Player.PlayerAgent p,AIGraph.AIG_CourseNode n)=>p.CourseNode.m_zone.Dimension==n.m_zone.Dimension&&p.CourseNode.m_zone.Layer.Pointer==n.m_zone.Layer.Pointer;
        public static bool TryResolve(LevelGeneration.ItemInLevel item,out AIGraph.AIG_CourseNode node){node=new();return item.Placed;}
    }
    static class SearchTraversal {
        public static HashSet<long> Allowed=new(){1,2};
        public static bool InScope(AIGraph.AIG_CourseNode n,AIGraph.AIG_CourseNode o,int scope)=>n?.m_zone?.Layer!=null&&o?.m_zone?.Layer!=null
            &&n.m_zone.Layer.Pointer==o.m_zone.Layer.Pointer&&n.m_zone.Dimension==o.m_zone.Dimension
            &&SearchAccessPolicy.InScope(n.m_zone.Pointer.ToInt64(),n.m_area?.Pointer.ToInt64()??0,o.m_zone.Pointer.ToInt64(),o.m_area?.Pointer.ToInt64()??0,scope==0);
        public static HashSet<long> Reachable(Player.PlayerAgent a,AIGraph.AIG_CourseNode b,AIGraph.AIG_CourseNode c,int scope,out bool cut){cut=false;return Allowed;}
    }
}
static class KnownContainerTests {
    static int checks;static long ids;static Player.PlayerAgent player=new();
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static LevelGeneration.LG_WeakResourceContainer Box(){var b=new LevelGeneration.LG_WeakResourceContainer{Pointer=(IntPtr)(++ids),m_serialNumber=(int)ids};b.m_syncComp.Pointer=(IntPtr)(ids+100);UnityEngine.Resources.Objects.Add(b);return b;}
    static void Main(){
        var box=Box();Check(KnownContainerIndex.Resolve(box,out _),"初始化容器可定位");
        Check(KnownContainerIndex.Candidates(out var cut).Count==1&&!cut&&UnityEngine.Resources.Reads==1,"空箱独立枚举一次，不靠物资反推");
        Check(!KnownContainerIndex.HasState&&KnownContainerIndex.Capture(player).Results.Count==0,"预读取不登记发现");
        Check(KnownContainerIndex.Contents(box)=="内容未确认","未开启空内容不能误报空箱");
        box.ISOpen=true;Check(KnownContainerIndex.Contents(box).Contains("当前为空"),"已开且存储明确零件才报告空箱");
        ((LevelGeneration.LG_ResourceContainer_Storage)box.m_storageComp).m_numberOfItems=1;
        Check(KnownContainerIndex.Contents(box)=="内容未确认","存储尚有物品但接口暂未呈现，不能误报空箱");box.ISOpen=false;
        var content=new LevelGeneration.ItemInLevel{Pointer=(IntPtr)20,container=new(){m_core=box}};box.Items.AddRange(new[]{content,content,new(){Pointer=(IntPtr)21,Placed=false,container=new(){m_core=box}}});
        Check(KnownContainerIndex.Contents(box).Contains("已确认 1 件")&&KnownContainerIndex.Contents(box).Contains("弹药包"),"闭合柜内已生成物资可确认、去重、不统计已拿走物品");
        Check(KnownContainerIndex.Matches(box,0)&&KnownContainerIndex.Matches(box,1)&&!KnownContainerIndex.Matches(box,2),"全部／未开／有锁过滤");
        box.Locked=true;box.WeakLockComponent=new(){Kind=OpenLockKind.Hackable};Check(KnownContainerIndex.Matches(box,2)&&KnownContainerIndex.State(box).Contains("入侵锁"),"入侵锁状态与过滤");
        box.WeakLockComponent.Kind=OpenLockKind.Melee;Check(KnownContainerIndex.State(box).Contains("物理锁"),"物理锁状态");
        box.WeakLockComponent.Kind=OpenLockKind.Unsupported;Check(KnownContainerIndex.State(box).Contains("不支持"),"未知锁不会当作无锁");
        box.WeakLockComponent.Kind=OpenLockKind.Hackable;KnownContainerIndex.Observe(box);var snapshot=KnownContainerIndex.Capture(player);var id=snapshot.Targets.Keys.Single();var saved=snapshot.Targets[id];
        Check(snapshot.Results.Single().NeededMelters==1&&snapshot.Details[id].Contains("位置")&&snapshot.Details[id].Contains("弹药包"),"结果读真实锁能力、位置、内容");
        KnownContainerIndex.Observe(box);Check(KnownContainerIndex.Capture(player).Targets.Keys.Single()==id,"再次查询相同容器去重");
        Check(KnownContainerIndex.Current(id,saved)&&KnownContainerIndex.OpenTarget(player,saved)?.Box==box,"指定对象生成原开启目标，不重新找编号同名对象");
        SearchTraversal.Allowed.Clear();Check(KnownContainerIndex.OpenTarget(player,saved)==null&&!KnownContainerIndex.Accessible(player,saved,out _),"重新封锁路线拒绝开启／标记");
        Check(KnownContainerIndex.Capture(player).Results.Single().Blocked.Contains("封锁"),"结果显示通路封锁原因");SearchTraversal.Allowed.Add(1);
        box.m_isLocker=true;Check(KnownContainerIndex.Name(box).Contains("储物柜"),"柜子和箱子分别显示正确类型");box.m_isLocker=false;
        box.Throw=true;Check(KnownContainerIndex.Contents(box).Contains("读取失败")&&PreLuna.Captain.Plugin.Logger.Last.Contains("broken box"),"内容读取失败不误报空箱，保留实际诊断");
        var failedContents=KnownContainerIndex.Capture(player);Check(failedContents.Results.Count==1&&failedContents.Targets.Count==1&&failedContents.Details.Count==1,"坏内容接口不会留下缺失详情的菜单结果");box.Throw=false;
        box.ISOpen=true;Check(KnownContainerIndex.Capture(player).Results.Single().Blocked.Contains("已开启"),"开箱后刷新结果阻止重复操作");
        box.ISOpen=false;box.WeakLockComponent.Kind=OpenLockKind.Unsupported;Check(KnownContainerIndex.Capture(player).Results.Single().Blocked!=null,"未知锁开启受限");
        var remote=Box();remote.SpawnNode.m_zone.Pointer=(IntPtr)2;KnownContainerIndex.Observe(remote);
        var layer=Box();layer.SpawnNode.m_zone.Layer.Pointer=(IntPtr)2;KnownContainerIndex.Observe(layer);
        var dimension=Box();dimension.SpawnNode.m_zone.Dimension=1;KnownContainerIndex.Observe(dimension);
        Check(KnownContainerIndex.Capture(player).Results.Count==1,"结果限定当前Zone、维度、分层");
        Check(KnownContainerIndex.Mark(box)&&SearchMarker.Calls==1&&MapDataManager.Calls==1,"标记复用已修复的共享流程");
        MapDataManager.Throw=true;Check(KnownContainerIndex.Mark(box)&&KnownContainerIndex.Current(id,saved),"地图揭示异常不损失结果与导航标记");MapDataManager.Throw=false;
        SearchMarker.Available=false;Check(!KnownContainerIndex.Mark(box)&&KnownContainerIndex.Current(id,saved),"导航层缺失保留结果");SearchMarker.Available=true;
        box.m_syncComp.Pointer=(IntPtr)500;Check(!KnownContainerIndex.Current(id,saved),"同步组件变化拒绝旧菜单");KnownContainerIndex.Observe(box);
        var newer=KnownContainerIndex.Capture(player);var newId=newer.Targets.Keys.Single();Check(newId!=id,"地址重用同步身份变化获得新编号");
        var currentSaved=newer.Targets[newId];box.m_serialNumber++;Check(!KnownContainerIndex.Current(newId,currentSaved),"容器序号变化拒绝旧身份");KnownContainerIndex.Observe(box);
        var beforeFloor=KnownContainerIndex.Capture(player);currentSaved=beforeFloor.Targets.Values.Single();int beforeFloorId=beforeFloor.Targets.Keys.Single();
        LevelGeneration.Builder.CurrentFloor.Pointer=(IntPtr)101;Check(!KnownContainerIndex.Current(beforeFloorId,currentSaved),"关卡身份变化拒绝旧目标");
        KnownContainerIndex.Reset();Check(!KnownContainerIndex.HasState,"关卡清理发现记录");KnownContainerIndex.Observe(box);Check(KnownContainerIndex.Capture(player).Targets.Keys.Single()>newId,"清理不复用旧菜单结果编号");
        foreach(int invalid in Enumerable.Range(0,7)) {
            var bad=Box();if(invalid==0)bad.gameObject.activeInHierarchy=false;else if(invalid==1)bad.gameObject.scene.Valid=false;else if(invalid==2)bad.m_terminalItem=null;
            else if(invalid==3)bad.m_storage=null;else if(invalid==4)bad.m_sync=null;else if(invalid==5)bad.m_syncComp=null;else bad.SpawnNode.m_zone.Layer=null;
            Check(!KnownContainerIndex.Resolve(bad,out _)&&!KnownContainerIndex.Observe(bad),"未初始化／预制资产排除 "+invalid);
        }
        SNetwork.SNet.IsMaster=false;Check(KnownContainerIndex.Candidates(out _).Count==0&&!KnownContainerIndex.Mark(box),"非主机不枚举／标记");SNetwork.SNet.IsMaster=true;
        GameStateManager.CurrentStateName=eGameStateName.Lobby;Check(KnownContainerIndex.Capture(player).Results.Count==0,"大厅不读取关卡对象");GameStateManager.CurrentStateName=eGameStateName.InLevel;
        KnownContainerIndex.Reset();UnityEngine.Resources.Objects.Clear();for(int i=0;i<70;i++)KnownContainerIndex.Observe(Box());
        var capped=KnownContainerIndex.Capture(player);Check(capped.Targets.Count==64&&capped.Results.Count==65&&capped.Results.Last().Notice,"显示64件上限并有不可执行提示");
        for(int i=0;i<4030;i++)Box();Check(KnownContainerIndex.Candidates(out cut).Count==4096&&cut,"场景候选检查有界，截断明确");
        Console.WriteLine("PASS: "+checks+" 项真实容器索引／身份／状态／内容／范围／失效／标记断言；场景与原开箱目标使用替身，未原生游戏测试。");
    }
}
