using System;
using System.Collections.Generic;
namespace UnityEngine {
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new(0,1,0); public float sqrMagnitude=>x*x+y*y+z*z;
        public void Normalize(){float n=MathF.Sqrt(sqrMagnitude);if(n>0){x/=n;y/=n;z/=n;}}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float f)=>new(a.x*f,a.y*f,a.z*f);
    }
    public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public float sqrMagnitude=>x*x+y*y;}
    public enum QueryTriggerInteraction{Ignore}
    public struct RaycastHit{public float distance;}
    public static class Physics {
        public static bool Wall,Occupied,FloorOnHorizontal;public static float WallDistance;public static int Casts;
        public static bool CapsuleCast(Vector3 a,Vector3 b,float r,Vector3 d,out RaycastHit hit,float length,int mask,QueryTriggerInteraction q){Casts++;hit=new(){distance=WallDistance};return Wall||FloorOnHorizontal&&Math.Abs(d.y)<.001f;}
        public static bool CheckCapsule(Vector3 a,Vector3 b,float r,int mask,QueryTriggerInteraction q)=>Occupied;
    }
}
namespace UnityEngine.AI {
    public struct NavMeshHit{public UnityEngine.Vector3 position;}
    public static class NavMesh {
        public static bool Missing,Blocked;public static float Slope;public static UnityEngine.Vector3 Shift;
        public static bool SamplePosition(UnityEngine.Vector3 p,out NavMeshHit hit,float radius,int mask){hit=new(){position=p.sqrMagnitude>.001f?p+Shift:p};if(Slope!=0)hit.position.y=p.z*Slope;return !Missing&&Math.Abs(hit.position.y-p.y)<=radius;}
        public static bool Raycast(UnityEngine.Vector3 from,UnityEngine.Vector3 to,out NavMeshHit hit,int mask){hit=default;return Blocked;}
    }
}
public class NativeObject{static int next;public IntPtr Pointer=new(++next);}
public static class LayerManager{public const int MASK_WORLD=1,MASK_PLAYER_INTERACT_BLOCKERS=2;}
namespace AIGraph {
    public class AIG_CourseNode {
        public class Zone{public NativeObject Layer=new();public int DimensionIndex;}
        public Zone m_zone=new();public static bool Missing;public static AIG_CourseNode Next;
        public static bool TryGetCourseNode(int dim,UnityEngine.Vector3 p,int mode,out AIG_CourseNode node){node=Next;return !Missing&&node!=null;}
    }
}
namespace Il2CppSystem.Collections.Generic {
    public class LinkedList<T>{readonly List<T> items=new();public int Count=>items.Count;public T First=>items[0];public void AddLast(T p)=>items.Add(p);public void Clear()=>items.Clear();}
}
namespace Player {
    public class PlayerAgent:NativeObject{public NativeObject Owner=new();public bool Alive=true;public UnityEngine.Vector3 Position;public int DimensionIndex;public AIGraph.AIG_CourseNode CourseNode=new();}
    public class PlayerBotActionBase {
        public class Descriptor:NativeObject{public PlayerAIBot Bot;public bool Terminated;public bool IsTerminated()=>Terminated;}
    }
    public class PlayerBotActionWalk {
        public class Descriptor:PlayerBotActionBase.Descriptor {
            public enum PostureEnum{Stand}public PostureEnum Posture;public float Prio,Haste;
            public Il2CppSystem.Collections.Generic.LinkedList<UnityEngine.Vector3> Path;
            public Descriptor(PlayerAIBot bot){Bot=bot;}
        }
    }
    public class PlayerAIBot:NativeObject {
        public PlayerAgent Agent=new();public int Requests,Stops;public bool StopThrows;
        public PlayerBotActionWalk.Descriptor Last;
        public bool RequestAction(PlayerBotActionWalk.Descriptor d){Requests++;Last=d;return PreLuna.MicroMaster.ControlRuntime.Current.AllowRequests&&PreLuna.MicroMaster.ControlRuntime.Current.Walk.IsOwn(d);}
        public void StopAction(PlayerBotActionWalk.Descriptor d){Stops++;if(StopThrows)throw new Exception("stop failed");d.Terminated=true;}
    }
}
namespace PreLuna.MicroMaster {
    public static class Plugin {public class Setting{public float Value;public Setting(float v){Value=v;}}public static Setting WalkHaste=new(.25f);}
    internal class ControlRuntime {
        public static ControlRuntime Current=new();public bool Owned=true,AllowRequests=true;public NativeManualWalk Walk;
        public bool Controls(Player.PlayerAgent agent)=>Owned;
    }
}
