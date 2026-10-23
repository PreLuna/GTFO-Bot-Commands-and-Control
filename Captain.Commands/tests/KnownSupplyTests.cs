using System;
using System.Collections.Generic;
using System.Linq;
using PreLuna.Captain.Tasks;
namespace UnityEngine {
    class GameObject { public bool activeInHierarchy=true; public LevelGeneration.ItemInLevel Item; public T GetComponentInParent<T>() where T:class => Item as T; }
    static class Time { public static float time; }
}
namespace HarmonyLib {
    class Harmony { public void Patch(object m, HarmonyMethod prefix=null,HarmonyMethod postfix=null){} }
    class HarmonyMethod { public HarmonyMethod(Type t,string m){} }
    static class AccessTools { public static object Method(Type t,string m,Type[] args=null)=>new(); }
}
class NavMarker { } class NavMarkerLayer { }
enum eGameStateName { Lobby,InLevel }
static class GameStateManager { public static eGameStateName CurrentStateName=eGameStateName.InLevel; }
namespace SNetwork { static class SNet { public static bool IsMaster=true; } }
namespace LevelGeneration {
    class Ptr { static int n; public IntPtr Pointer=new(++n); }
    class LG_Layer:Ptr{}
    class LG_NavInfo { public int Number; }
    class LG_Area:Ptr { public LG_NavInfo m_navInfo=new(){Number=1}; }
    class LG_Zone:Ptr { public string AliasName="ZONE_100"; public int DimensionIndex; public LG_Layer Layer=new(); public LG_NavInfo NavInfo=new(){Number=100}; }
    static class Builder { public static object CurrentFloor=new(); }
    class LG_GenericTerminalItem { public string TerminalItemKey; }
    class ItemData { public Player.InventorySlot slot=Player.InventorySlot.ResourcePack; }
    class CustomData { public float ammo=40; }
    class ItemBlock { public uint persistentID=101; }
    class ItemInLevel:Ptr {
        public UnityEngine.GameObject gameObject=new(); public ItemBlock ItemDataBlock=new(); public string PublicName="弹药包";
        public AIGraph.AIG_CourseNode Node; public bool Placed=true,Open=true;
        public LG_GenericTerminalItem Terminal=new(); public ItemData Data=new(); public CustomData Custom=new();
        public ItemData Get_pItemData()=>Data; public CustomData GetCustomData()=>Custom;
        public T GetComponentInChildren<T>() where T:class=>Terminal as T;
    }
}
namespace AIGraph { class AIG_CourseNode:LevelGeneration.Ptr { public LevelGeneration.LG_Zone m_zone; public LevelGeneration.LG_Area m_area; } }
namespace Player {
    enum InventorySlot { ResourcePack=4,Consumable=5,Carry=8 }
    class Owner { public bool IsBot; }
    class PlayerAgent { public Owner Owner=new(); public AIGraph.AIG_CourseNode CourseNode; }
    class PlayerManager {
        public static PlayerManager Current=new(); public PlayerAgent[] Agents=new PlayerAgent[4];
        public PlayerAgent GetPlayerAgentInSlot(int slot)=>Agents[slot];
    }
}
namespace PreLuna.Captain {
    static class Plugin { public static Catalog Catalog=new(); public static Logger Logger=new(); }
    class Catalog { public string TranslateItems(string text)=>text; }
    class Logger { public void LogWarning(string text){} }
}
namespace PreLuna.Captain.Tasks {
    record CaptainSupplyResult(int Id,string Label,string Blocked,uint TypeId=0,int ItemSlot=0,string TypeName=null,bool BotDiscovered=false);
    class CaptainSupplyScope { public readonly string Label;public string Notice;public readonly List<CaptainSupplyResult> Results=new();public CaptainSupplyScope(string label){Label=label;} }
    static class ItemTaskPolicy { public static bool SupportedSlot(int slot)=>slot==4||slot==5; }
    static class SpecialItemAccess {
        public static string Reason(LevelGeneration.ItemInLevel item)=>"原版搬运能力尚未确认";
        public static string AmountText(LevelGeneration.ItemInLevel item)=>SpecialItemPolicy.Special((int)item.Data.slot)?"特殊手持／任务搬运·单件":"余量 "+item.Custom.ammo.ToString("0.##");
    }
    static class AutoPickupAdapter { public static bool Open(LevelGeneration.ItemInLevel item)=>item.Open; }
    static class ItemRegion {
        public static bool TryResolve(LevelGeneration.ItemInLevel item,out AIGraph.AIG_CourseNode node){node=item?.Node;return item?.Placed==true&&node!=null;}
        public static bool SameSpace(Player.PlayerAgent source,AIGraph.AIG_CourseNode node)=>source.CourseNode.m_zone.DimensionIndex==node.m_zone.DimensionIndex&&source.CourseNode.m_zone.Layer.Pointer==node.m_zone.Layer.Pointer;
    }
}
static class KnownSupplyTests {
    static int checks;
    static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
    static void Hook(string name,params object[] args)=>typeof(KnownSupplyIndex).GetMethod(name,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,args);
    static LevelGeneration.ItemInLevel Item(AIGraph.AIG_CourseNode node,string key="AMMOPACK_001") {
        var item=new LevelGeneration.ItemInLevel{Node=node};item.Terminal.TerminalItemKey=key;item.gameObject.Item=item;return item;
    }
    static void Main(){
        var ledger=new KnownSupplyLedger<string>();
        Check(ledger.Observe(0,"bad",SupplySource.PlayerVisit)==null&&ledger.Count==0,"不接受无效世界身份");
        var first=ledger.Observe(1,"a",SupplySource.PlayerMark);
        Check(ledger.Observe(1,"a",SupplySource.QueryMarker).Id==first.Id&&ledger.Count==1,"重复物品只保留一个身份");
        Check(first.Sources==(SupplySource.PlayerMark|SupplySource.QueryMarker),"合并发现来源");
        ledger.Forget(1);var replaced=ledger.Observe(1,"b",SupplySource.Aimed);Check(replaced.Id!=first.Id,"地址重用不复用旧菜单身份");
        Check(!ledger.Current(1,first.Id)&&ledger.Current(1,replaced.Id),"提交时不能把旧世界地址指认为新对象");
        ledger.Reset();Check(ledger.Count==0&&ledger.Observe(1,"c",SupplySource.Aimed).Id!=replaced.Id,"换关卡清理并防止旧编号复用");
        for(int i=2;i<=2048;i++)ledger.Observe(i,"x",SupplySource.Aimed);
        Check(ledger.Observe(2049,"x",SupplySource.Aimed)==null&&ledger.Count==2048,"发现缓存有界");
        KnownSupplyIndex.Reset();
        var zone=new LevelGeneration.LG_Zone();var node=new AIGraph.AIG_CourseNode{m_zone=zone,m_area=new()};
        var player=new Player.PlayerAgent{CourseNode=node};Player.PlayerManager.Current.Agents[0]=player;
        var unseen=Item(node);
        Check(KnownSupplyIndex.Capture(player).Targets.Count==0,"场景生成物品本身不是已知物资");
        Hook("GenericMarker",unseen.gameObject,null);
        Check(KnownSupplyIndex.Capture(player).Targets.Count==0,"创建标记失败不登记");
        Hook("GenericMarker",unseen.gameObject,new NavMarker());
        var initial=KnownSupplyIndex.Capture(player);var id=initial.Targets.Keys.Single();
        Check(initial.Scopes[0].Results.Count==1&&initial.Scopes[1].Results.Count==1,"SmartPing通用标记接入房间和区域");
        Check(initial.Scopes[0].Results[0] is { TypeId:101, ItemSlot:4, TypeName:"弹药包" },"收集元数据来自实际数据ID和槽位，不猜测中文名称");
        Check(initial.Details[id].Contains("查询／模组标记"),"来源不冒充亲眼发现");
        KnownSupplyIndex.Observe(unseen,SupplySource.RobotSearch);
        Check(KnownSupplyIndex.Capture(player).Scopes[0].Results[0].BotDiscovered && KnownSupplyIndex.Capture(player).Details[id].Contains("队长即时搜寻"),"搜索来源进入结果，保留原身份");
        var searchOnly=Item(node); KnownSupplyIndex.SuppressQueryMarker=true;
        Hook("GenericMarker",searchOnly.gameObject,new NavMarker());KnownSupplyIndex.SuppressQueryMarker=false;
        Check(KnownSupplyIndex.Capture(player).Targets.Count==1,"搜索自己的标记不冒充查询发现");
        Hook("PlayerMarker",player,unseen.gameObject);
        Check(KnownSupplyIndex.Capture(player).Targets.Count==1&&KnownSupplyIndex.Capture(player).Details[id].Contains("玩家标记"),"中键标记合并来源");
        var otherRoom=new AIGraph.AIG_CourseNode{m_zone=zone,m_area=new()};var second=Item(otherRoom);
        KnownSupplyIndex.Observe(second,SupplySource.PlayerVisit);var result=KnownSupplyIndex.Capture(player);
        Check(result.Scopes[0].Results.Count==1&&result.Scopes[1].Results.Count==2,"房间与Zone不混用");
        Check(result.Targets.Count==2,"同名同类型编号不能合并实体");
        var remoteZone=new LevelGeneration.LG_Zone{Layer=zone.Layer,AliasName="ZONE_101"};
        var remoteNode=new AIGraph.AIG_CourseNode{m_zone=remoteZone,m_area=new()};var remote=Item(remoteNode);
        KnownSupplyIndex.Observe(remote,SupplySource.QueryMarker);
        Check(KnownSupplyIndex.Capture(player).Targets.Count==2,"查询远处未到访区域不能当作已开放区域");
        player.CourseNode=remoteNode;KnownSupplyIndex.Tick();player.CourseNode=node;
        Check(KnownSupplyIndex.Capture(player).Targets.Count==3,"已到访区域可按区域查询");
        var otherLayer=Item(new(){m_zone=new(){DimensionIndex=0},m_area=new()});KnownSupplyIndex.Observe(otherLayer,SupplySource.QueryMarker);
        var otherDimension=Item(new(){m_zone=new(){Layer=zone.Layer,DimensionIndex=1},m_area=new()});KnownSupplyIndex.Observe(otherDimension,SupplySource.QueryMarker);
        Check(KnownSupplyIndex.Capture(player).Targets.Count==3,"跨层跨维度物资不泄漏进列表");
        unseen.Open=false;Check(KnownSupplyIndex.PickupReason(player,unseen).Contains("尚未打开"),"关闭箱柜仍可查询但不可下令拿取");
        unseen.gameObject.activeInHierarchy=false;
        Check(KnownSupplyIndex.PickupReason(player,unseen).Contains("尚未打开"),"未启用的柜内物品提示需开柜，不误报实体丢失");
        Check(KnownSupplyIndex.Capture(player).Details[id].Contains("尚未打开"),"柜内搜索结果可查看并保留状态说明");
        unseen.Open=true;
        Check(KnownSupplyIndex.PickupReason(player,unseen).Contains("尚未启用"),"开柜后仍未启用的交互不能指定拾取");
        unseen.gameObject.activeInHierarchy=true;
        unseen.Open=true;unseen.Data.slot=Player.InventorySlot.Carry;Check(KnownSupplyIndex.PickupReason(player,unseen).Contains("原版"),"任务物品不采用普通拾取");
        unseen.Data.slot=Player.InventorySlot.ResourcePack;unseen.Placed=false;
        Check(KnownSupplyIndex.PickupReason(player,unseen).Contains("失效"),"旧菜单提交时物品已拿走明确拒绝");
        Check(KnownSupplyIndex.Capture(player).Targets.Count==2,"刷新清理已拿走条目");
        Hook("Despawn",second);Check(KnownSupplyIndex.Capture(player).Targets.Count==1,"销毁及时清理已知缓存");
        Hook("Despawn",remote);Check(!KnownSupplyIndex.Current(id,remote),"失效菜单对象不能重新提交");
        KnownSupplyIndex.Reset();Check(KnownSupplyIndex.Capture(player).Targets.Count==0,"换关卡清除所有发现");
        for(int i=0;i<85;i++)KnownSupplyIndex.Observe(Item(node),SupplySource.PlayerVisit);
        var capped=KnownSupplyIndex.Capture(player);Check(capped.Targets.Count==80&&capped.Scopes[0].Notice!=null,"大列表有界且明确告知截断");
        SNetwork.SNet.IsMaster=false;KnownSupplyIndex.Observe(Item(node),SupplySource.PlayerVisit);
        SNetwork.SNet.IsMaster=true;KnownSupplyIndex.Reset();
        Console.WriteLine("PASS: "+checks+" 项实际已知物资适配／身份／范围／失效核验断言；未运行Unity原生场景。");
    }
}

