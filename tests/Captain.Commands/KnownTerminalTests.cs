using System;
using System.Collections.Generic;
using System.Linq;
using PreLuna.Captain.Tasks;
namespace UnityEngine {
    class GameObject { public bool activeInHierarchy=true; }
    struct Vector3 { public float x,y,z; public override string ToString()=>"("+x+","+y+","+z+")"; }
    class Transform { public Vector3 position; }
    static class Time { public static float time; }
}
namespace AIGraph {
    class Layer { public IntPtr Pointer=(IntPtr)1; }
    class Zone { public IntPtr Pointer=(IntPtr)10; public Layer Layer=new(); public int DimensionIndex; public string AliasName="ZONE_100"; }
    class NavInfo { public int Number=100; }
    class Area { public IntPtr Pointer=(IntPtr)20; public NavInfo m_navInfo=new(); }
    class AIG_CourseNode { public Zone m_zone=new(); public Area m_area=new(); }
}
namespace Player { class PlayerAgent { public AIGraph.AIG_CourseNode CourseNode=new(); } }
namespace SNetwork { static class SNet { public static bool IsMaster=true; } }
enum eGameStateName { InLevel, Lobby }
static class GameStateManager { public static eGameStateName CurrentStateName=eGameStateName.InLevel; }
namespace LevelGeneration {
    class Floor { public IntPtr Pointer=(IntPtr)123; }
    static class Builder { public static Floor CurrentFloor=new(); }
    enum TERM_State { Sleeping,Awake,PlayerInteracting,DataMining,Hacked,CodePuzzle,InputTest,ReactorError,AskToPlayLogAudio,DoPlayAudioFile,AudioLoopError,Ping,PasswordProtected,EnterPassword }
    enum eCM_GuiObjectStatus { Normal }
    class Revealer { public uint MapGUIObjID=10; }
    class LG_ComputerTerminal {
        static long next=100;
        public IntPtr Pointer=(IntPtr)(++next);
        UnityEngine.GameObject root=new(); public bool Throw;
        public UnityEngine.GameObject gameObject { get { if(Throw)throw new Exception("destroyed component");return root; } }
        public UnityEngine.Transform transform=new();
        public bool m_isSetup=true,IsPasswordProtected; public object m_terminalItem=new();
        public uint SyncID; public AIGraph.AIG_CourseNode SpawnNode=new();
        public string ItemKey="TERMINAL_123"; public int m_serialNumber=123;
        public TERM_State CurrentStateName; public Revealer m_mapLookatRevealer=new();
    }
    class LG_ComputerTerminalManager {
        public static LG_ComputerTerminalManager Current=new();
        public Dictionary<uint,LG_ComputerTerminal> m_terminals=new();
    }
}
enum NavMarkerOption { Empty, TerminalTitleDistance }
class NavMarker { public string name="terminal"; public object m_fadeRoutine; public string Title;public bool Visible,Initialized;
    public void SetVisualStates(NavMarkerOption a,NavMarkerOption b,NavMarkerOption c,NavMarkerOption d){Initialized=true;}
    public void SetTitle(string title){Title=title;}public void SetVisible(bool visible){if(!Initialized)throw new NullReferenceException("stateOptions");Visible=visible;} }
class NavMarkerLayer {
    public int Calls; public bool Throw; public NavMarker Last;
    public NavMarker PrepareGenericMarker(UnityEngine.GameObject root){Calls++;if(Throw)throw new Exception("marker");return Last=new();}
    public object FadeMarkerOverTime(NavMarker a,string b,float c,float d,bool e)=>null;
    public void RemoveMarker(NavMarker marker) { }
}
static class GuiManager { public static NavMarkerLayer NavMarkerLayer=new(); }
static class CoroutineManager { public static object StartCoroutine(object a,object b)=>null; public static void StopCoroutine(object a) { } }
static class MapDataManager {
    public static int Calls; public static bool Throw;
    public static void WantToSetGUIObjVisible(uint id,LevelGeneration.eCM_GuiObjectStatus state){if(Throw)throw new Exception("map");Calls++;}
}
namespace PreLuna.Captain.Tasks {
    sealed record CaptainTerminalResult(int Id,string Label,bool Notice=false);
    static class KnownSupplyIndex { public static bool SuppressQueryMarker; }
    static class ItemRegion {
        public static bool SameSpace(Player.PlayerAgent source,AIGraph.AIG_CourseNode node)=>node?.m_zone?.Layer != null
            && source?.CourseNode?.m_zone?.Layer != null && node.m_zone.DimensionIndex==source.CourseNode.m_zone.DimensionIndex
            && node.m_zone.Layer.Pointer==source.CourseNode.m_zone.Layer.Pointer;
    }
    static class SearchTraversal {
        public static bool InScope(AIGraph.AIG_CourseNode node,AIGraph.AIG_CourseNode origin,int scope)=>node?.m_zone?.Layer != null && origin?.m_zone?.Layer != null
            && node.m_zone.DimensionIndex==origin.m_zone.DimensionIndex&&node.m_zone.Layer.Pointer==origin.m_zone.Layer.Pointer
            && SearchAccessPolicy.InScope(node.m_zone.Pointer.ToInt64(),node.m_area?.Pointer.ToInt64()??0,origin.m_zone.Pointer.ToInt64(),origin.m_area?.Pointer.ToInt64()??0,scope==0);
    }
}
namespace PreLuna.Captain { static class Plugin { public static Logger Logger=new(); } class Logger { public int Warnings; public void LogWarning(string text){Warnings++;} } }
static class KnownTerminalTests {
    static int checks; static uint sync;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    static LevelGeneration.LG_ComputerTerminal Terminal(AIGraph.AIG_CourseNode node=null){
        var t=new LevelGeneration.LG_ComputerTerminal{SyncID=++sync,SpawnNode=node??new()};
        LevelGeneration.LG_ComputerTerminalManager.Current.m_terminals[t.SyncID]=t;return t;
    }
    static void Main(){
        var player=new Player.PlayerAgent();var t=Terminal();
        Check(KnownTerminalIndex.Resolve(t,out var node)&&node==t.SpawnNode,"仅已注册且初始化完成的实体有效");
        Check(KnownTerminalIndex.Candidates(out var truncated).Count==1&&!truncated,"复用真实注册表，无全场景扫描");
        Check(KnownTerminalIndex.Capture(player).Results.Count==0&&!KnownTerminalIndex.HasState,"预读取候选并不记录发现");
        Check(KnownTerminalIndex.Observe(t),"实际到达事件记录终端");
        var snapshot=KnownTerminalIndex.Capture(player);var id=snapshot.Targets.Keys.Single();var saved=snapshot.Targets[id];
        Check(snapshot.Results.Single().Label.Contains("TERMINAL_123")&&snapshot.Details[id].Contains("ZONE_100"),"结果编号与所在位置来自实际对象");
        Check(snapshot.Details[id].Contains("仅定位")&&snapshot.Details[id].Contains("队长即时搜寻"),"结果不冒充交互或玩家访问");
        Check(KnownTerminalIndex.Current(id,saved),"实际登记身份匹配");
        KnownTerminalIndex.Observe(t);Check(KnownTerminalIndex.Capture(player).Targets.Keys.Single()==id,"重复搜寻去重并保持结果身份");
        foreach(var state in Enum.GetValues<LevelGeneration.TERM_State>()){
            t.CurrentStateName=state;
            Check(!KnownTerminalIndex.State(t).Contains("未知")&&KnownTerminalIndex.State(t).Length>0,"原版终端状态均有中文说明 "+state);
        }
        t.CurrentStateName=LevelGeneration.TERM_State.Awake;t.IsPasswordProtected=true;
        Check(KnownTerminalIndex.State(t).Contains("密码保护"),"密码保护只读展示，不读取密码内容");
        t.ItemKey="";Check(KnownTerminalIndex.Name(t)=="终端 TERMINAL_123","编号缺省回退实际序号");t.ItemKey="TERMINAL_123";
        Check(KnownTerminalIndex.Mark(t)&&GuiManager.NavMarkerLayer.Last.Visible&&MapDataManager.Calls==1,"临时标记与原版地图图标分开调用");
        Check(GuiManager.NavMarkerLayer.Last.Title.Contains("TERMINAL_123")&&!KnownSupplyIndex.SuppressQueryMarker,"标记标题包含编号且恢复来源抑制状态");
        KnownTerminalIndex.Mark(t);Check(MapDataManager.Calls==1&&GuiManager.NavMarkerLayer.Calls==2,"重新标记不重复揭示地图容器");
        KnownSupplyIndex.SuppressQueryMarker=true;KnownTerminalIndex.Mark(t);Check(KnownSupplyIndex.SuppressQueryMarker,"嵌套标记保留之前抑制状态");KnownSupplyIndex.SuppressQueryMarker=false;
        GuiManager.NavMarkerLayer.Throw=true;
        Check(!KnownTerminalIndex.Mark(t),"标记失败返回不可用，不中断统计");
        Check(!KnownSupplyIndex.SuppressQueryMarker&&KnownTerminalIndex.Current(id,saved),"导航标记异常恢复标志且保留发现结果");GuiManager.NavMarkerLayer.Throw=false;
        var other=Terminal();KnownTerminalIndex.Observe(other);MapDataManager.Throw=true;
        Check(KnownTerminalIndex.Mark(other),"地图异常不阻断已创建的导航标记");
        Check(!KnownSupplyIndex.SuppressQueryMarker,"地图接口异常也恢复标志");MapDataManager.Throw=false;
        KnownTerminalIndex.Mark(other);Check(MapDataManager.Calls==2,"失败地图揭示允许后续重试");
        var remoteNode=new AIGraph.AIG_CourseNode();remoteNode.m_zone.Pointer=(IntPtr)11;var remote=Terminal(remoteNode);KnownTerminalIndex.Observe(remote);
        Check(KnownTerminalIndex.Capture(player).Targets.Count==2,"已知远处终端也不会混入当前Zone列表");
        var layerNode=new AIGraph.AIG_CourseNode();layerNode.m_zone.Layer.Pointer=(IntPtr)2;KnownTerminalIndex.Observe(Terminal(layerNode));
        var dimensionNode=new AIGraph.AIG_CourseNode();dimensionNode.m_zone.DimensionIndex=1;KnownTerminalIndex.Observe(Terminal(dimensionNode));
        Check(KnownTerminalIndex.Capture(player).Targets.Count==2,"跨维度、分层排除");
        var oldPointer=t.Pointer;var oldSync=t.SyncID;
        LevelGeneration.LG_ComputerTerminalManager.Current.m_terminals.Remove(oldSync);
        Check(!KnownTerminalIndex.Current(id,saved)&&!KnownTerminalIndex.Mark(t),"注销终端拒绝旧菜单重新标记");
        var replacement=Terminal();replacement.Pointer=oldPointer;KnownTerminalIndex.Observe(replacement);
        Check(!KnownTerminalIndex.Current(id,saved),"地址重用但同步身份改变，不把旧菜单指向新终端");
        var replaceSnapshot=KnownTerminalIndex.Capture(player);var newId=replaceSnapshot.Targets.Single(p=>p.Value.Value==replacement).Key;
        Check(newId!=id,"替换实体获得新菜单编号");
        KnownTerminalIndex.Mark(replacement);Check(MapDataManager.Calls==3,"替换实体清除原地址地图标记缓存");
        var currentSaved=replaceSnapshot.Targets[newId];LevelGeneration.Builder.CurrentFloor.Pointer=(IntPtr)124;
        Check(!KnownTerminalIndex.Current(newId,currentSaved),"切换关卡即使指针仍有效也拒绝旧会话");
        KnownTerminalIndex.Reset();Check(!KnownTerminalIndex.HasState&&KnownTerminalIndex.Capture(player).Results.Count==0,"离开关卡清空发现与标记缓存");
        KnownTerminalIndex.Observe(replacement);Check(KnownTerminalIndex.Capture(player).Targets.Keys.Single()!=newId,"清理后不复用过期菜单ID");
        foreach(int invalid in Enumerable.Range(0,6)){
            var bad=Terminal();if(invalid==0)bad.gameObject.activeInHierarchy=false;else if(invalid==1)bad.m_isSetup=false;
            else if(invalid==2)bad.m_terminalItem=null;else if(invalid==3)bad.SpawnNode=null;else if(invalid==4)bad.SpawnNode.m_zone.Layer=null;
            else LevelGeneration.LG_ComputerTerminalManager.Current.m_terminals[bad.SyncID]=Terminal();
            Check(!KnownTerminalIndex.Resolve(bad,out _)&&!KnownTerminalIndex.Observe(bad),"无效实体不发现 "+invalid);
        }
        PreLuna.Captain.Plugin.Logger.Warnings=0;
        var corrupt=Terminal();corrupt.Throw=true;
        Check(!KnownTerminalIndex.Resolve(corrupt,out _)&&PreLuna.Captain.Plugin.Logger.Warnings==1,"损坏组件异常保留诊断并跳过单个实体");
        KnownTerminalIndex.Resolve(corrupt,out _);Check(PreLuna.Captain.Plugin.Logger.Warnings==1,"重复损坏诊断有频率限制");
        UnityEngine.Time.time=11;KnownTerminalIndex.Resolve(corrupt,out _);
        Check(PreLuna.Captain.Plugin.Logger.Warnings==2,"诊断不是永久静音，冷却后继续报告");
        Check(KnownTerminalIndex.Candidates(out _).Contains(replacement),"个别坏实体不阻断其他有效终端候选");
        SNetwork.SNet.IsMaster=false;Check(!KnownTerminalIndex.Observe(replacement)&&KnownTerminalIndex.Capture(player).Targets.Count==0,"非房主不登记或显示主机私有结果");SNetwork.SNet.IsMaster=true;
        GameStateManager.CurrentStateName=eGameStateName.Lobby;Check(KnownTerminalIndex.Candidates(out _).Count==0,"大厅不读取关卡实体");GameStateManager.CurrentStateName=eGameStateName.InLevel;
        var manager=LevelGeneration.LG_ComputerTerminalManager.Current;LevelGeneration.LG_ComputerTerminalManager.Current=null;
        Check(KnownTerminalIndex.Candidates(out _).Count==0&&!KnownTerminalIndex.Resolve(replacement,out _),"注册器未建立时不触发空引用");LevelGeneration.LG_ComputerTerminalManager.Current=manager;
        KnownTerminalIndex.Reset();manager.m_terminals.Clear();for(int i=0;i<70;i++)KnownTerminalIndex.Observe(Terminal());
        var capped=KnownTerminalIndex.Capture(player);Check(capped.Targets.Count==64&&capped.Results.Count==65&&capped.Results.Last().Notice,"结果64件上限，截断提示不可执行");
        for(int i=0;i<1000;i++)Terminal();var candidates=KnownTerminalIndex.Candidates(out truncated);
        Check(truncated&&candidates.Count==1024,"极大注册表有限处理且明确提示截断");
        Console.WriteLine("PASS: "+checks+" 项实际终端索引／注册身份／发现去重／范围／状态／失效／地图及标记异常断言；使用场景替身，未运行原生Unity与网络。");
    }
}
