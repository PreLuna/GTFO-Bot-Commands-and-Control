using System;
using System.Collections.Generic;
using System.Linq;
using AIGraph;
using UnityEngine;
using UnityEngine.AI;
using PreLuna.Captain.Tasks;
enum eGameStateName { Lobby, InLevel }
static class GameStateManager { public static eGameStateName CurrentStateName = eGameStateName.InLevel; }
namespace SNetwork { static class SNet { public static bool IsMaster = true; } }
namespace UnityEngine.Rendering { enum ShadowCastingMode {Off} }
namespace UnityEngine
{
    class Object { public bool Destroyed; public static void Destroy(Object value){value.Destroyed=true;} }
    class Shader : Object { public static bool Available=true; public static Shader Find(string name)=>Available?new():null; }
    class Material : Object {public string name; public Material(Shader shader){} }
    class GameObject : Object {
        public static List<GameObject> Created=new(); public string name; public LineRenderer Line;
        public GameObject(string value){name=value;Created.Add(this);}
        public T AddComponent<T>() where T:new(){var t=new T();Line=t as LineRenderer;return t;}
    }
    class LineRenderer {public bool enabled,useWorldSpace,loop,receiveShadows; public Material sharedMaterial;
        public float startWidth,endWidth;public Color startColor,endColor;public int numCornerVertices,numCapVertices,positionCount;
        public Rendering.ShadowCastingMode shadowCastingMode;public List<Vector3> Points=new();
        public void SetPosition(int i,Vector3 p){while(Points.Count<=i)Points.Add(default);Points[i]=p;}
    }
    struct Color {public Color(float r,float g,float b,float a){} }
    struct Vector3 {
        public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 up=>new(0,1,0);public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
        public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*t;
    }
    static class Mathf { public const float PI=(float)Math.PI; public static float Cos(float f)=>(float)Math.Cos(f);public static float Sin(float f)=>(float)Math.Sin(f); }
    static class Time {public static float time;}
}
namespace UnityEngine.AI
{
    enum NavMeshPathStatus {PathComplete,PathPartial,PathInvalid}
    struct NavMeshHit {public Vector3 position;}
    class NavMeshPath {public static int Destroyed;public IntPtr m_Ptr=new(9); public Vector3[] corners;public NavMeshPathStatus status;
        public static void DestroyNavMeshPath(IntPtr pointer){Destroyed++;}
    }
    static class NavMesh {
        public static int Calculations,Samples;public static bool SampleOk=true,CalculateOk=true;
        public static NavMeshPathStatus Status=NavMeshPathStatus.PathComplete;public static Vector3[] Corners;public static Func<Vector3,Vector3> Snap;
        public static bool SamplePosition(Vector3 p,out NavMeshHit h,float range,int mask){Samples++;h=new(){position=Snap==null?p:Snap(p)};return SampleOk;}
        public static bool CalculatePath(Vector3 a,Vector3 b,int mask,NavMeshPath path){Calculations++;path.status=Status;path.corners=Corners??new[]{a,b};return CalculateOk;}
    }
}
namespace LevelGeneration
{
    class Floor {public IntPtr Pointer=new(10);}
    static class Builder {public static Floor CurrentFloor=new();}
    class LG_Zone {public IntPtr Pointer;public int DimensionIndex;public Layer Layer=new();}
    class Layer {public IntPtr Pointer=new(20);}
    class LG_Area {public AIG_CourseNode m_courseNode;}
    class Gate {public bool IsTraversable=true;}
}
namespace AIGraph
{
    class AIG_CoursePortal {public IntPtr Pointer;public AIG_CourseNode m_nodeA,m_nodeB;public LevelGeneration.Gate m_gate=new();
        public bool IsProgressionLocked,IsTraversable=true,Allowed=true;public Vector3 Position;}
    class AIG_CourseNode {public IntPtr Pointer;public LevelGeneration.LG_Zone m_zone;public List<AIG_CoursePortal> m_portals=new();
        public static Func<Vector3,AIG_CourseNode> Locate;
        public static bool TryGetCourseNode(int dimension,Vector3 p,float range,out AIG_CourseNode node){node=Locate(p);return node!=null;}
    }
}
namespace Player
{
    class Owner {public IntPtr Pointer=new(11);}
    class PlayerAgent {public Owner Owner=new();public AIG_CourseNode CourseNode;public Vector3 Position;public int DimensionIndex;}
    static class PlayerManager {public static PlayerAgent Local;public static PlayerAgent GetLocalPlayerAgent()=>Local;}
}
namespace ChainedPuzzles
{
    enum eChainedPuzzleStatus {Disabled,Active,Solved}
    struct State {public bool isActive,isSolved;public eChainedPuzzleStatus status;}
    class Replicator {public State m_currentState;}
    class ChainedPuzzleInstance {public Replicator m_stateReplicator=new(); public LevelGeneration.LG_Area m_sourceArea;}
    class ChainedPuzzleManager {public static ChainedPuzzleManager Current=new();public List<ChainedPuzzleInstance> m_instances=new();}
}
namespace PreLuna.Captain {static class Plugin {public static Logger Logger=new();}class Logger {public void LogWarning(string t){} } }
namespace PreLuna.Captain.Tasks
{
    static class ItemTaskAdapter {public static List<string> Messages=new();public static void Feedback(string t)=>Messages.Add(t);}
    static class ItemRegion {public static bool SameSpace(Player.PlayerAgent p,AIG_CourseNode n)=>p!=null&&n?.m_zone?.Layer!=null
        &&n.m_zone.DimensionIndex==p.DimensionIndex&&n.m_zone.Layer.Pointer==p.CourseNode.m_zone.Layer.Pointer;}
    // The production traversal boundary has its own regression suite. This fixture
    // supplies world topology, not the route planner, renderer, or lifecycle logic.
    static class SearchTraversal {
        public static bool InScope(AIG_CourseNode n,AIG_CourseNode origin,int scope)=>n?.m_zone!=null&&origin?.m_zone!=null
            &&n.m_zone.Pointer==origin.m_zone.Pointer&&n.m_zone.DimensionIndex==origin.m_zone.DimensionIndex&&n.m_zone.Layer.Pointer==origin.m_zone.Layer.Pointer;
        public static bool Passable(AIG_CoursePortal p)=>p!=null&&p.Allowed&&!p.IsProgressionLocked;
        public static HashSet<long> Reachable(Player.PlayerAgent player,AIG_CourseNode start,AIG_CourseNode origin,int scope,out bool truncated,Dictionary<long,AIG_CourseNode> nodes){
            truncated=false;nodes.Clear();var queue=new Queue<AIG_CourseNode>();queue.Enqueue(start);
            while(queue.Count>0){var n=queue.Dequeue();if(nodes.ContainsKey(n.Pointer.ToInt64()))continue;nodes[n.Pointer.ToInt64()]=n;
                foreach(var portal in n.m_portals){var other=portal.m_nodeA==n?portal.m_nodeB:portal.m_nodeA;
                    if(Passable(portal)&&InScope(other,origin,scope)&&ItemRegion.SameSpace(player,other)&&!nodes.ContainsKey(other.Pointer.ToInt64()))queue.Enqueue(other);}
            }return nodes.Keys.ToHashSet();
        }
    }
}
static class NavigationTests
{
    static int assertions;static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
    static AIG_CourseNode a,b,c;static AIG_CoursePortal ab,bc;static Player.PlayerAgent player;static RouteDestination destination;
    static bool valid;static int reads;
    static AIG_CoursePortal Link(AIG_CourseNode x,AIG_CourseNode y,int id,float at){var p=new AIG_CoursePortal{Pointer=new(id),m_nodeA=x,m_nodeB=y,Position=new(at,0,0)};x.m_portals.Add(p);y.m_portals.Add(p);return p;}
    static void Scene(){
        RouteNavigation.Reset();Time.time=0;GameStateManager.CurrentStateName=eGameStateName.InLevel;SNetwork.SNet.IsMaster=true;
        LevelGeneration.Builder.CurrentFloor=new();Shader.Available=true;GameObject.Created.Clear();ItemTaskAdapter.Messages.Clear();
        var zone=new LevelGeneration.LG_Zone{Pointer=new(1)};a=new(){Pointer=new(101),m_zone=zone};b=new(){Pointer=new(102),m_zone=zone};c=new(){Pointer=new(103),m_zone=zone};
        ab=Link(a,b,201,5);bc=Link(b,c,202,15);
        AIG_CourseNode.Locate=p=>p.x<5?a:p.x<15?b:c;
        player=new(){CourseNode=a,Position=new(0,0,0)};Player.PlayerManager.Local=player;
        NavMesh.Calculations=NavMesh.Samples=0;NavMesh.SampleOk=NavMesh.CalculateOk=true;NavMesh.Status=NavMeshPathStatus.PathComplete;NavMesh.Corners=null;NavMesh.Snap=null;
        destination=new(c,new(20,0,0));valid=true;reads=0;ChainedPuzzles.ChainedPuzzleManager.Current=new();
    }
    static RouteTarget Target()=>new("物资#1",()=>{reads++;return valid?destination:null;});
    static GameObject Latest()=>GameObject.Created[^1];
    static void Tick(float time){Time.time=time;RouteNavigation.Tick();}
    static void Main(){
        Graph();Scene();
        var plan=RouteTraversal.Build(player,c,new(20,0,0),null,a,out var reason);
        Check(plan!=null&&!plan.WaitingForDoor&&plan.Points.Length==2,"完整步行路线验证并绘制拐点");
        Check(NavMeshPath.Destroyed==1,"临时原生路径缓冲释放一次");
        ab.IsTraversable=false;
        plan=RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason);
        Check(plan?.WaitingForDoor==true&&plan.EndNode==a&&plan.Door==ab,"未开普通门只导航到当前侧");
        Check(plan.Points[^1].x<5,"终点没有跨过关闭门");
        ab.Allowed=false;
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null&&reason.Contains("封锁"),"扫描／钥匙／剧情封锁不能跨越");
        Scene();NavMesh.Status=NavMeshPathStatus.PathPartial;
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"不把部分导航路径说成完整路线");
        NavMesh.Status=NavMeshPathStatus.PathComplete;NavMesh.Corners=new[]{new Vector3(0,0,0),new Vector3(float.NaN,0,0)};
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"非有限路径点拒绝");
        Scene();NavMesh.Corners=new[]{new Vector3(0,0,0),new Vector3(30,0,0),new Vector3(20,0,0)};
        var outside=new AIG_CourseNode{Pointer=new(104),m_zone=new(){Pointer=new(2)}};
        AIG_CourseNode.Locate=p=>p.x>25?outside:p.x<5?a:p.x<15?b:c;
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null&&reason.Contains("超出"),"逐段验证，不仅验证首尾，拒绝跨Zone捷径");
        Scene();ab.IsTraversable=false;
        NavMesh.Corners=new[]{new Vector3(0,0,0),new Vector3(8,0,0),new Vector3(3,0,0)};
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"原生路径绕行穿过关门后返回也被拒绝");
        Scene();NavMesh.SampleOk=false;
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"没有正确步行网格时拒绝");
        Scene();NavMesh.Snap=p=>new(p.x,0,p.z);
        Check(RouteTraversal.Build(player,c,new(20,1.8f,0),null,a,out reason)!=null,"高于地面的物品／按钮位置可以定位到正确节点地面");
        Check(RouteTraversal.Build(player,c,new(20,5,0),null,a,out reason)==null,"跨楼层高度不能随意贴到地面");
        Scene();NavMesh.Corners=new[]{new Vector3(0,0,0),new Vector3(float.MaxValue,0,0),new Vector3(20,0,0)};
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"巨大有限坐标引起距离溢出也必须拒绝");
        Scene();ab.IsProgressionLocked=true;
        Check(!RouteTraversal.Open(ab)&&RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"可通行标志不能绕过剧情锁");
        Scene();c.m_zone=new(){Pointer=new(1),DimensionIndex=1};
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"相同Zone指针但维度不同也拒绝");
        c.m_zone=new(){Pointer=new(1),Layer=new(){Pointer=new(99)}};
        Check(RouteTraversal.Build(player,c,new(20,0,0),null,a,out reason)==null,"相同Zone指针但层级不同也拒绝");
        Scene();destination=new(new(){Pointer=new(500),m_zone=new(){Pointer=new(50)}},new(20,0,0));
        RouteNavigation.Start(Target());Check(!RouteNavigation.HasState&&GameObject.Created.Count==0,"跨Zone目标不能留下显示对象");
        Scene();RouteNavigation.Start(Target());var first=Latest();var mat=first.Line.sharedMaterial;
        Check(RouteNavigation.HasState&&first.Line.enabled&&first.Line.Points.Count==2,"独立对象实际显示路线");
        int calc=NavMesh.Calculations,resolve=reads;
        for(int i=0;i<100;i++)Tick(i*.001f);
        Check(NavMesh.Calculations==calc&&reads<=resolve+1,"高帧率不会重算路径或每帧解析目标");
        player.Position=new(3.5f,0,0);Tick(.6f);Check(NavMesh.Calculations==calc,"移动后仍遵守2秒重算下限");
        Tick(2.1f);Check(NavMesh.Calculations==calc,"沿路线正常行走不会反复重算");
        player.Position=new(3.5f,0,4);Tick(2.7f);
        Check(NavMesh.Calculations==calc+1&&GameObject.Created.Count==1,"偏离路线超过3米重算，复用独立渲染对象");
        Check(ItemTaskAdapter.Messages.Count==1,"普通路线更新不会连续刷提示");
        var puzzle=new ChainedPuzzles.ChainedPuzzleInstance{m_sourceArea=new(){m_courseNode=a}};
        puzzle.m_stateReplicator.m_currentState=new(){isActive=true};ChainedPuzzles.ChainedPuzzleManager.Current.m_instances.Add(puzzle);
        Tick(3.3f);Check(!first.Line.enabled&&RouteNavigation.HasState,"扫描时仅隐藏自己的线，保留导航目标");
        puzzle.m_stateReplicator.m_currentState=new(){isSolved=true,status=ChainedPuzzles.eChainedPuzzleStatus.Solved};
        Tick(3.9f);Check(first.Line.enabled,"扫描完成恢复自己的线");
        Check(puzzle.m_stateReplicator.m_currentState.isSolved,"扫描状态没有被导航修改");
        RouteNavigation.Control(32);Check(ItemTaskAdapter.Messages[^1].Contains("物资#1"),"可以只读查看当前目标");
        RouteNavigation.Control(34);Check(!RouteNavigation.HasState&&first.Destroyed&&mat.Destroyed,"取消只释放自己GameObject及材质");
        Scene();RouteNavigation.Start(Target());first=Latest();valid=false;Tick(.6f);
        Check(!RouteNavigation.HasState&&first.Destroyed,"物资已拾取失效时清理路线");
        Scene();RouteNavigation.Start(Target());first=Latest();player.Owner=new(){Pointer=new(999)};Tick(.6f);
        Check(!RouteNavigation.HasState&&first.Destroyed,"玩家身份替换取消");
        Scene();RouteNavigation.Start(Target());first=Latest();player.CourseNode=outside;Tick(.6f);
        Check(!RouteNavigation.HasState&&first.Destroyed,"离开本Zone自动清理");
        Scene();RouteNavigation.Start(Target());first=Latest();LevelGeneration.Builder.CurrentFloor=new(){Pointer=new(999)};Tick(.6f);
        Check(!RouteNavigation.HasState&&first.Destroyed,"关卡切换清理");
        Scene();RouteNavigation.Start(Target());first=Latest();SNetwork.SNet.IsMaster=false;Tick(.6f);
        Check(!RouteNavigation.HasState&&first.Destroyed,"失去房主清理");
        Scene();RouteNavigation.Start(Target());first=Latest();Time.time=1.1f;RouteNavigation.Start(new("终端",()=>destination));
        Check(first.Destroyed&&GameObject.Created.Count==2&&RouteNavigation.HasState,"新目标替换旧路线，不叠加");
        Scene();Shader.Available=false;RouteNavigation.Start(Target());
        Check(!RouteNavigation.HasState&&GameObject.Created.Count==0,"着色器失效安全清理并诊断");
        Scene();RouteNavigation.Start(Target());first=Latest();bc.IsTraversable=false;Tick(.6f);
        Check(!first.Line.enabled,"路径中门关闭立刻隐藏过期路线");
        Tick(2.1f);Check(RouteNavigation.HasState&&first.Line.enabled&&first.Line.Points[^1].x<15,"门关闭后重算为门前停靠");
        bc.IsTraversable=true;Tick(4.2f);Check(first.Line.Points[^1].x==20,"普通门打开后继续到原目标");
        player.CourseNode=c;player.Position=new(20,0,0);Tick(4.8f);
        Check(!RouteNavigation.HasState&&first.Destroyed,"到达后清理，不自动交互目标");
        Console.WriteLine($"PASS: {assertions} 项生产导航算法／路径验证／独立显示／限频／扫描避让／清理断言；场景及Unity接口为夹具，未执行游戏原生环境。");
    }
    static void Graph(){
        var edges=new Dictionary<long,RouteEdge[]>{[1]=new[]{new RouteEdge(2,12,true),new RouteEdge(3,13,false)},[2]=new[]{new RouteEdge(4,24,false)},[3]=new[]{new RouteEdge(5,35,false)},[5]=new[]{new RouteEdge(4,54,false)},[4]=Array.Empty<RouteEdge>()};
        IEnumerable<RouteEdge> Edges(long node)=>edges.TryGetValue(node,out var list)?list:Array.Empty<RouteEdge>();
        var p=RoutePolicy.Find(1,4,Edges,false);Check(p.Count==3&&p[0].To==3,"优先更长但全部开门的路径");
        p=RoutePolicy.Find(1,4,Edges,true);Check(p.Count==2&&p[0].Closed,"允许普通关闭门的备用路径能定位第一扇门");
        Check(RoutePolicy.Find(1,9,Edges,true)==null,"断开目标不宣称可达");
        Check(RoutePolicy.Find(1,300,n=>new[]{new RouteEdge(n+1,n+1000,false)},false)==null,"256节点预算有界");
        Check(RoutePolicy.Find(1,1,Edges,false).Count==0,"同房间没有虚构门段");
        Check(RoutePolicy.ValidateSamples(new long[]{1,2,3},1,3,new(){1,2,3},(a,b)=>b==a+1),"合法逐段样本通过");
        Check(!RoutePolicy.ValidateSamples(new long[]{1,3},1,3,new(){1,2,3},(a,b)=>b==a+1),"穿过非相邻房间拒绝");
        Check(!RoutePolicy.ValidateSamples(new long[]{1,2,3},1,3,new(){1,3},(a,b)=>true),"超出允许节点拒绝");
        Check(!RoutePolicy.Replan(1.9f,0,true,true,100,100)&&RoutePolicy.Replan(2,0,false,true,0,0),"最短重算间隔与门变化策略");
    }
}
