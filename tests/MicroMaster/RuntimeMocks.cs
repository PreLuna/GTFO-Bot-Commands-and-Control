using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using PreLuna.MicroMaster;
#pragma warning disable CS0649
public class NativeObject {
    static int serial;
    public readonly int Instance = ++serial;
    public IntPtr Pointer;
    public NativeObject() { Pointer = new(Instance); }
    public int GetInstanceID() => Instance;
    public T TryCast<T>() where T:class => this as T;
}
namespace UnityEngine {
    public class MonoBehaviour : NativeObject { public MonoBehaviour(IntPtr pointer) { Pointer = pointer; } }
    public static class Object { public static T FindObjectOfType<T>() where T:new() => new(); }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 zero=>default;
        public Vector3 normalized => sqrMagnitude>0?new(x/MathF.Sqrt(sqrMagnitude),y/MathF.Sqrt(sqrMagnitude),z/MathF.Sqrt(sqrMagnitude)):default;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public enum KeyCode { W,A,S,D,Q,T,F7,F8,Home,Escape }
    public static class Input {
        public static readonly HashSet<KeyCode> Down=new(),Held=new();
        public static bool GetKey(KeyCode k)=>Held.Contains(k);
        public static bool GetKeyDown(KeyCode k)=>Down.Contains(k);
        public static bool GetMouseButtonDown(int n)=>false;
    }
    public static class Time { public static float unscaledTime;public static int frameCount; }
    public static class Application { public static bool isFocused=true; }
}
namespace Il2CppInterop.Runtime.Attributes { public class HideFromIl2CppAttribute:Attribute {} }
public enum eGameStateName { InLevel,Lobby }
public enum eFocusState { FPS,Menu }
public enum InputAction { LookHorizontal=1,LookVertical=2,Weapon=23,Communication=15 }
public static class GameStateManager { public static eGameStateName CurrentStateName=eGameStateName.InLevel; }
public static class FocusStateManager { public static eFocusState CurrentState=eFocusState.FPS; }
public static class InputMapper { public static bool PlayerHasFocus=true;public static float GetAxisKeyMouse(InputAction action,eFocusState focus)=>3; }
public static class PlayerChatManager { public static bool InChatMode; }
public class PUI_CommunicationMenu { public bool m_active; }
public class FPSCamera:NativeObject {}
public class PlayerInventoryLocal {}
namespace HarmonyLib {
    public class Harmony { public void Patch(MethodInfo target,HarmonyMethod prefix=null,HarmonyMethod postfix=null){} }
    public class HarmonyMethod { public int priority;public HarmonyMethod(Type type,string name){} }
    public static class Priority { public const int First=800,Last=0; }
    public static class AccessTools {
        public static MethodInfo Method(Type type,string name,Type[] args=null)=>args==null?type.GetMethod(name):type.GetMethod(name,args);
        public static FieldInfo Field(Type type,string name)=>type.GetField(name);
    }
}
public enum eGameEventChatLogType { GameEvent }
public static class GuiManager { public static Layer PlayerLayer=new(); public class Layer { public Log m_gameEventLog=new(); }public class Log { public void AddLogItem(string text,eGameEventChatLogType type) {} } }
namespace SNetwork {
    public class Owner:NativeObject { public bool IsBot; }
    public static class SNet { public static bool IsMaster=true;public static Owner Master=new(); }
}
namespace AIGraph {
    public class Layer:NativeObject {}
    public class Zone { public Layer Layer=new(); }
    public class AIG_CourseNode { public Zone m_zone=new(); }
}
namespace LevelGeneration {
    public static class Builder { public static NativeObject CurrentFloor=new(); }
    public class Dimension { public List<Agents.Agent> AgentsInDimension=new(); }
}
namespace Agents {
    public class Agent:NativeObject {
        public bool Alive=true,m_isBeingDestroyed;
        public int DimensionIndex;
        public LevelGeneration.Dimension Dimension=new();
        public AIGraph.AIG_CourseNode CourseNode=new();
        public Vector3 Position;
    }
}
namespace Enemies {
    public class EnemyAI { public int Mode=1; }
    public class EnemyAgent:Agents.Agent { public EnemyAI AI=new(); }
}
namespace Player {
    public class PlayerAgent:Agents.Agent {
        public SNetwork.Owner Owner=new();
        public int PlayerSlotIndex;
        public bool IsBeingDestroyed,IsBeingDespawned;
        public Dam_PlayerDamageLocal Damage;
        public PlayerLocomotion Locomotion=new();
        public FPSCamera FPSCamera=new();
        public Vector3 EyePosition=new(0,1.6f,0),TargetLookDir=new(0,0,1),Forward=new(0,0,1);
        public PlayerAIBot Bot;
        public T GetComponent<T>() where T:class=>Bot as T;
    }
    public class PlayerLocomotion { public int m_currentStateEnum;public PlayerAgent m_owner; }
    public class PlayerAIBot:NativeObject { public PlayerAgent Agent; public Sync SyncValues=new();public class Sync { public Vector3 LookDirection; } }
    public class PlayerBotActionBase:NativeObject { public Descriptor DescBase=new();public class Descriptor:NativeObject {} }
    public class PlayerBotActionWalk:PlayerBotActionBase { public new class Descriptor:PlayerBotActionBase.Descriptor {} }
    public class PlayerBotActionTravel:PlayerBotActionBase { public new class Descriptor:PlayerBotActionBase.Descriptor {} }
    public class PlayerBotActionFollow:PlayerBotActionBase { public new class Descriptor:PlayerBotActionBase.Descriptor {} }
    public class RootPlayerBotAction:PlayerBotActionBase { public PlayerAIBot m_bot; }
    public class PlayerManager {
        public static PlayerManager Current=new();public PlayerAgent[] Slots=new PlayerAgent[4];
        public static PlayerAgent GetLocalPlayerAgent()=>Current?.Slots[0];
        public PlayerAgent GetPlayerAgentInSlot(int slot)=>Slots[slot];
    }
}
public class Dam_PlayerDamageLocal { public Player.PlayerAgent Owner;public float Health=100;public bool GrabbedAtAll; }
namespace PreLuna.HeadCamera {
    public readonly record struct CameraTarget(int Slot,long AgentIdentity,long OwnerIdentity,string Name,bool IsBot);
    public readonly record struct ControlView(CameraTarget Target,Vector3 Position,Vector3 Direction,float Fov,bool Enlarged,bool UiBlocked);
    public readonly record struct ControlAim(CameraTarget Target,Vector3 Position,Vector3 Direction);
    public static class HeadCameraApi {
        public static bool IsVisible=true,ThrowClear;
        public static float DefaultFieldOfView=75;
        public static CameraTarget Selected;
        public static Func<CameraTarget,bool> Request;
        public static bool RegisterControlInput(object o,Func<CameraTarget,bool> request,Func<bool> busy,Action exit,Func<string> status,float window) { Request=request;return true; }
        public static bool RegisterControlView(object o,Func<CameraTarget,ControlView?> view)=>true;
        public static bool TryGetSelected(out CameraTarget target) { target=Selected;return true; }
        public static bool TryGetControlAim(out ControlAim aim) { aim=new(Selected,new(0,1.6f,0),new(0,0,1));return true; }
        public static void ClearControlView(object o) { if(ThrowClear)throw new Exception("camera cleanup"); }
        public static void UnregisterControlInput(object o) { Request=null; }
    }
}
namespace PreLuna.MicroMaster {
    public class Setting<T> { public T Value;public Setting(T value){Value=value;} }
    public class Plugin {
        public const string Guid="micro";public static Plugin Instance=new();public Logger Log=new();
        public class Logger { public List<string> Messages=new();public void LogInfo(string s)=>Messages.Add(s);public void LogWarning(string s)=>Messages.Add(s); }
        public static Setting<bool> Enabled=new(true),InvertMouse=new(false),ThreatExit=new(true);
        public static Setting<float> DoubleClickWindow=new(.3f),MouseSensitivity=new(1),ThreatRadius=new(15),ThreatConfirmation=new(.25f);
        public static Setting<int> ThreatCount=new(3);
        public static Setting<KeyCode> EmergencyKey=new(KeyCode.F8),ToggleViewKey=new(KeyCode.F7),ResetViewKey=new(KeyCode.Home);
    }
    public class NativeManualWalk:IManualWalk {
        public static bool ThrowStop;
        public Player.PlayerAIBot Bot;
        public NativeManualWalk(Player.PlayerAIBot bot){Bot=bot;}
        public bool Valid=>true;
        public bool IsOwn(Player.PlayerBotActionBase.Descriptor d)=>false;
        public bool RefreshBeforeNativeStep(Direction3 direction)=>true;
        public bool TryDirection(Direction3 direction)=>true;
        public void Stop(){if(ThrowStop)throw new Exception("walk cleanup");}
    }
}
