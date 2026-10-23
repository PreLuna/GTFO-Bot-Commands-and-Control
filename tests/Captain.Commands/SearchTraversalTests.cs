using System;
using System.Collections.Generic;
using PreLuna.Captain.Tasks;
namespace UnityEngine {
    struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;
        public Vector3 normalized => this * (1f/(float)Math.Sqrt(sqrMagnitude));
        public static Vector3 up=>new(0,1,0); public static Vector3 forward=>new(0,0,1);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator -(Vector3 a)=>a * -1;
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);
        public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }
    enum QueryTriggerInteraction { Ignore }
    static class Physics { public static bool CheckCapsule(Vector3 a,Vector3 b,float c,int d,QueryTriggerInteraction e)=>false; }
}
namespace Player {
    class PlayerAgent { public AIGraph.AIG_CourseNode CourseNode; public int DimensionIndex; public UnityEngine.Vector3 Position; }
    static class PlayerAIBot { public static bool SnapPositionToNav(UnityEngine.Vector3 p,out UnityEngine.Vector3 result,int a,int b){result=p;return false;} }
}
namespace AIGraph {
    class Layer { public IntPtr Pointer = (IntPtr)1; }
    class Zone { public IntPtr Pointer; public Layer Layer=new(); public int DimensionIndex; }
    class Area { public IntPtr Pointer; }
    class AIG_CourseNode {
        public IntPtr Pointer; public Zone m_zone; public Area m_area; public List<AIG_CoursePortal> m_portals=new();
        public static bool TryGetCourseNode(int i,UnityEngine.Vector3 p,int r,out AIG_CourseNode node){node=null;return false;}
    }
    class AIG_CoursePortal { public bool IsTraversable=true,IsProgressionLocked; public AIG_CourseNode m_nodeA,m_nodeB; public LevelGeneration.iLG_Door_Core m_door; public LevelGeneration.Gate m_gate; public LevelGeneration.Gate Gate=>m_gate; }
}
namespace LevelGeneration {
    enum eDoorStatus { Closed,Unlocked,Open,Opening,Destroyed,Closed_LockedWithNoKey,Closed_LockedWithKeyItem,Closed_LockedWithChainedPuzzle_Alarm }
    enum eLG_DoorType { Weak,Security }
    enum eWeakLockType { Melee,Hackable,Other }
    class LG_WeakLock { public eWeakLockType m_lockType; public bool Locked=true; public bool IsLocked()=>Locked; }
    class iLG_Door_Core { public eDoorStatus LastStatus; public eLG_DoorType DoorType; public T TryCast<T>() where T:class=>this as T; }
    class LG_WeakDoor : iLG_Door_Core { public bool Key,IsCheckpointDoor; public bool RequiresKey()=>Key; public List<LG_WeakLock> WeakLocks=new(); }
    class Gate { public iLG_Door_Core SpawnedDoor; public bool HasGateWallRemover,IsTraversable; }
}
static class LayerManager { public static int MASK_DEFAULT; }
namespace PreLuna.Captain.Tasks {
    static class ItemRegion {
        public static bool SameSpace(Player.PlayerAgent agent,AIGraph.AIG_CourseNode node)=>node?.m_zone?.Layer != null
            && agent?.CourseNode?.m_zone?.Layer != null && node.m_zone.DimensionIndex==agent.DimensionIndex
            && node.m_zone.Layer.Pointer==agent.CourseNode.m_zone.Layer.Pointer;
    }
}
class SearchTraversalTests {
    static int checks;
    static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    static AIGraph.AIG_CourseNode Node(long id,long zone=1,long room=1)=>new(){Pointer=(IntPtr)id,m_zone=new(){Pointer=(IntPtr)zone},m_area=new(){Pointer=(IntPtr)room}};
    static AIGraph.AIG_CoursePortal Link(AIGraph.AIG_CourseNode a,AIGraph.AIG_CourseNode b,LevelGeneration.iLG_Door_Core door=null,bool walk=true,bool progression=false){
        var p=new AIGraph.AIG_CoursePortal{m_nodeA=a,m_nodeB=b,m_door=door,IsTraversable=walk,IsProgressionLocked=progression,
            m_gate=new(){SpawnedDoor=door,HasGateWallRemover=door==null,IsTraversable=walk}};a.m_portals.Add(p);b.m_portals.Add(p);return p;
    }
    static LevelGeneration.LG_WeakDoor Weak(LevelGeneration.eWeakLockType? kind=null)=>new(){DoorType=LevelGeneration.eLG_DoorType.Weak,
        LastStatus=kind.HasValue?LevelGeneration.eDoorStatus.Closed_LockedWithNoKey:LevelGeneration.eDoorStatus.Closed,
        WeakLocks=kind.HasValue?new(){new(){m_lockType=kind.Value}}:new()};
    static void Main(){
        var a=Node(1);var b=Node(2);var c=Node(3,room:2);var d=Node(4,room:2);var e=Node(5,room:2);var outside=Node(6,zone:2);
        Link(a,b,Weak(LevelGeneration.eWeakLockType.Melee),false);
        Link(b,c,Weak(LevelGeneration.eWeakLockType.Hackable),false);
        var sealedDoor=Link(c,d,new LevelGeneration.iLG_Door_Core{DoorType=LevelGeneration.eLG_DoorType.Security,LastStatus=LevelGeneration.eDoorStatus.Closed_LockedWithChainedPuzzle_Alarm},false,true);
        Link(d,e,Weak(),false);Link(c,outside,null,true);Link(c,a,null,true);
        var agent=new Player.PlayerAgent{CourseNode=a};
        var room=SearchTraversal.Reachable(agent,a,a,0,out var capped);
        Check(room.SetEquals(new long[]{1,2})&&!capped,"当前房间允许物理弱锁、限制其他房间");
        var zone=SearchTraversal.Reachable(agent,a,a,1,out capped);
        Check(zone.SetEquals(new long[]{1,2,3})&&!capped,"Zone电子锁可达，扫描封锁之后所有节点排除，循环不重复");
        sealedDoor.m_door.LastStatus=LevelGeneration.eDoorStatus.Open;sealedDoor.IsTraversable=true;
        Check(!SearchTraversal.Reachable(agent,a,a,1,out _).Contains(4),"只改外观开门、任务仍封锁，不能越门");
        sealedDoor.IsProgressionLocked=false;
        Check(SearchTraversal.Reachable(agent,a,a,1,out _).Contains(5),"已打开并解除任务锁，可经过后续普通门");
        sealedDoor.m_door.LastStatus=LevelGeneration.eDoorStatus.Closed_LockedWithKeyItem;sealedDoor.IsTraversable=false;
        Check(!SearchTraversal.Reachable(agent,a,a,1,out _).Contains(4),"重新封锁钥匙门，重新查询立即排除");
        foreach(var kind in new[]{LevelGeneration.eWeakLockType.Melee,LevelGeneration.eWeakLockType.Hackable,LevelGeneration.eWeakLockType.Other}){
            var x=Node(10);var y=Node(11);Link(x,y,Weak(kind),false);agent.CourseNode=x;
            Check(SearchTraversal.Reachable(agent,x,x,1,out _).Contains(11)==(kind!=LevelGeneration.eWeakLockType.Other),"只接受物理／电子弱锁类型 "+kind);
        }
        foreach(bool key in new[]{false,true})foreach(bool checkpoint in new[]{false,true}){
            var x=Node(20);var y=Node(21);var weak=Weak();weak.Key=key;weak.IsCheckpointDoor=checkpoint;Link(x,y,weak,false);agent.CourseNode=x;
            Check(SearchTraversal.Reachable(agent,x,x,1,out _).Contains(21)==(!key&&!checkpoint),"普通门分类仍排除钥匙与检查点 "+key+checkpoint);
        }
        {
            var x=Node(30);var y=Node(31);Link(x,y,new LevelGeneration.iLG_Door_Core{DoorType=LevelGeneration.eLG_DoorType.Security,LastStatus=LevelGeneration.eDoorStatus.Unlocked},true);agent.CourseNode=x;
            Check(!SearchTraversal.Reachable(agent,x,x,1,out _).Contains(31),"刚完成扫描但安全门未打开，不提前跳过开门");
        }
        {
            var x=Node(40);var y=Node(41);var z=Node(42);y.m_zone.DimensionIndex=2;z.m_zone.Layer.Pointer=(IntPtr)3;Link(x,y);Link(x,z);agent.CourseNode=x;
            Check(SearchTraversal.Reachable(agent,x,x,1,out _).Count==1,"不跨维度和分层");
            Check(SearchTraversal.Reachable(agent,y,x,1,out _).Count==0,"起点无效不猜测位置");
        }
        {
            var x=Node(50);var y=Node(51);var p=Link(x,y);p.m_gate=new();agent.CourseNode=x;
            Check(SearchTraversal.Reachable(agent,x,x,1,out _).Count==1,"关卡门未完成生成不当成无门通路");
            p.m_gate=null;
            Check(SearchTraversal.Reachable(agent,x,x,1,out _).Count==1,"门／Gate均未初始化时不调用原版空引用属性");
            p.m_door=Weak();
            Check(SearchTraversal.Reachable(agent,x,x,1,out _).Count==1,"门实体存在但Gate未完成关联仍排除");
        }
        {
            var x=Node(100);var last=x;for(int i=101;i<=400;i++){var next=Node(i);Link(last,next);last=next;}agent.CourseNode=x;
            Check(SearchTraversal.Reachable(agent,x,x,1,out capped).Count==256&&capped,"超大图有限处理，明确截断");
        }
        agent.CourseNode=a;var before=SearchTraversal.Reachable(agent,a,a,1,out _);agent.CourseNode=d;
        var fromBot=SearchTraversal.Reachable(agent,d,a,1,out _);
        Check(before.Contains(1)&&!fromBot.Contains(1),"同Zone机器人仍不得从封锁门另一侧传送进搜索范围");
        agent.CourseNode=a;var nodeMap=new Dictionary<long,AIGraph.AIG_CourseNode>{{999,a}};
        var withNodes=SearchTraversal.Reachable(agent,a,a,1,out _,nodeMap);
        Check(new HashSet<long>(nodeMap.Keys).SetEquals(withNodes)&&!nodeMap.ContainsKey(999),"门查询只获得同一可达遍历的真实节点，并清理旧映射");
        Check(nodeMap[1]==a,"门查询保留真实节点身份，而非根据坐标猜测另一侧");
        Console.WriteLine("PASS: "+checks+" 项实际 SearchTraversal 门分类／多段可达图／维度／范围／动态图／限额断言；场景对象为替身，未运行原生导航和碰撞。");
    }
}
