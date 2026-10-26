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
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);
    }
    public class Component : NativeObject { public Transform transform = new(); public T GetComponentInParent<T>() where T:class => transform.Item as T; }
    public class Behaviour : Component { public bool enabled; }
    public class Texture : NativeObject {}
    public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
    public struct Quaternion {}
    public class Camera {public float fieldOfView=67;}
    public class Transform : NativeObject { public Vector3 position; public Vector3 TransformPoint(Vector3 p)=>new(position.x+p.x,position.y+p.y,position.z+p.z); public ItemEquippable Item;public Light Light;public Transform parent;public Vector3 localPosition,localScale;public Quaternion localRotation;public void SetParent(Transform p,bool world){parent=p;}public T GetComponent<T>() where T:class=>Light as T; public GameObject gameObject; public Dictionary<string,Transform> Children=new(); public Transform(){gameObject=new(this);} public Transform Find(string n)=>Children.GetValueOrDefault(n); }
    public class GameObject : NativeObject { public Transform transform;public bool activeSelf=true;public GameObject(Transform t){transform=t;}public void SetActive(bool v){activeSelf=v;} }
    public class Light : Behaviour {
        float lampIntensity=1,lampRange=20,lampAngle=50;
        public float intensity {get=>lampIntensity;set{ControlRuntime.Current?.FilterNativeLightWrite(this,ref value,2);lampIntensity=value;}}
        public float range {get=>lampRange;set{ControlRuntime.Current?.FilterNativeLightWrite(this,ref value,0);lampRange=value;}}
        public float spotAngle {get=>lampAngle;set{ControlRuntime.Current?.FilterNativeLightWrite(this,ref value,1);lampAngle=Math.Max(1,value);}}
        public float innerSpotAngle;public Color color=new(1,1,1,1);public Texture cookie;public int cullingMask=-1;
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
public enum InputAction { LookHorizontal=1,LookVertical=2,Weapon=23,Communication=15,Flashlight=32 }
public static class GameStateManager { public static eGameStateName CurrentStateName=eGameStateName.InLevel; }
namespace Globals {public class Global {public void OnApplicationQuit(){Player.NativeSwitchHooks.Call("ApplicationQuitting");Player.FlashlightInventory.AssetsAvailable=false;}}}
public static class FocusStateManager { public static eFocusState CurrentState=eFocusState.FPS; }
public static class InputMapper { public static bool PlayerHasFocus=true,FlashlightDown;public static bool GetButtonDownKeyMouse(InputAction a,eFocusState f)=>a==InputAction.Flashlight&&FlashlightDown;public static float GetAxisKeyMouse(InputAction action,eFocusState focus)=>3; }
public static class PlayerChatManager { public static bool InChatMode; }
public class PUI_CommunicationMenu { public bool m_active; }
public class FPSCamera:NativeObject { public UnityEngine.Camera m_camera=new(); }
public class PlayerInventoryLocal {}
public class ItemEquippable:NativeObject { public bool m_isWielded=true,HasFlashlight=true; public Player.PlayerAgent Owner;public Gear.GearPartFlashlight AttachedFlashlight=new(); public Light m_lt; }
namespace Gear {public class GearPartFlashlight {public uint m_settingsID=2;}public class GearManager {public void LoadOfflineGearDatas(){}}}
public struct sCL_SpotLight {public Vector3 Position,Irradiance;public float InvRangeSqr,InvNearRange,ShadowIndex;}
public class CL_ShadowGenerator:NativeObject {public bool Allocated,IsForced;}
public static class CL_ShadowAtlas {public static void TempDisableLight(CL_Light light){ControlRuntime.Current?.NativeLightRenderDisabled(light);}}
public class CL_Light : NativeObject {
 public UnityEngine.Light UnityLight;public bool m_isOn,IsActive;public int ActiveID=2;public int Refreshes,DataUpdates,Skipped,NonFiniteWrites;public float InvRangeSqr;
 public sCL_SpotLight Data;
 public void UpdateTransform(){Refreshes++;UpdateData();}
 public void UpdateData(){if(ControlRuntime.Current?.AllowNativeLightData(UnityLight)==false){Skipped++;return;}DataUpdates++;InvRangeSqr=1f/(UnityLight.range*UnityLight.range);if(!float.IsFinite(InvRangeSqr))NonFiniteWrites++;Data=new(){Irradiance=new(UnityLight.color.r*UnityLight.intensity,UnityLight.color.g*UnityLight.intensity,UnityLight.color.b*UnityLight.intensity),InvRangeSqr=InvRangeSqr,InvNearRange=.5f,ShadowIndex=0};}
}
public class CL_SpotLight:CL_Light {public CL_ShadowGenerator ShadowGenerator=new();public int ShadowID=>ShadowGenerator.Allocated?0:-1;}
namespace CullingSystem {
 public class C_Light:NativeObject {public bool IsShown;public UnityEngine.Light m_unityLight;public CL_Light m_clusterLight=new CL_SpotLight();public void SetOn(bool v){if(!v||ControlRuntime.Current?.AllowNativeLightData(m_unityLight)!=false)m_clusterLight.m_isOn=v;}}
 public class C_LightDynamic:C_Light {public void UpdateTransformData(){} }
 public static class C_CullingManager {public static System.Collections.Generic.List<C_Light> VisibleSpotLights=new();}
}
public class PlayerInventoryBase {
 public Player.PlayerAgent Owner;public UnityEngine.Light m_flashlight=new();public CullingSystem.C_LightDynamic m_flashlightCLight=new();
 public ItemEquippable WieldedItem;public bool FlashlightEnabled,WantsFlashlightEnabled;
 public virtual void OnItemEquippableFlashlightWielded(ItemEquippable i,Gear.GearPartFlashlight p){}public virtual void PrepareHelmetFlashlight(){}
 public virtual void SetFlashlightEnabled(bool value,bool helmet,bool sound){}public virtual void ReceiveSetFlashlightStatus(bool value,bool sound){}public virtual void UnWieldFlashlight(){}
}
namespace GameData {
 public class PlayerDataBlock {public UnityEngine.Vector3 camPosDefault=new(0,1.8f,0),camPosCrouch=new(0,1,0);}
 public class FlashlightSettingsDataBlock:NativeObject {public uint persistentID;public float range,angle,intensity;public UnityEngine.Color color=new(1,1,1,1);}
 public class GameDataBlockBase<T> {public static WrapperData Wrapper=new();public class WrapperData{public System.Collections.Generic.List<T> Blocks=new();}}
}
namespace HarmonyLib {
    public class Harmony {public Harmony(string id){} public void Patch(MethodInfo target,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod finalizer=null){} public void UnpatchSelf(){} }
    public class HarmonyMethod { public int priority;public HarmonyMethod(Type type,string name){} }
    public static class Priority { public const int First=800,Last=0; }
    public static class AccessTools {
        public static MethodInfo PropertySetter(Type type,string name)=>type.GetProperty(name)?.SetMethod;
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
    public class PlayerAgent:Agents.Agent { public GameData.PlayerDataBlock PlayerData=new(); public Transform transform=new();
        public SNetwork.Owner Owner=new();
        public int PlayerSlotIndex;
        public bool IsBeingDestroyed,IsBeingDespawned;
        public Dam_PlayerDamageLocal Damage;
        public PlayerLocomotion Locomotion=new();
        public FPSCamera FPSCamera=new();
        public PlayerSync Sync=new();
        public Vector3 EyePosition=new(0,1.6f,0),TargetLookDir=new(0,0,1),Forward=new(0,0,1);
        public PlayerAIBot Bot;
        public PlayerInventoryBase Inventory=>Bot?.Inventory;
        public T GetComponent<T>() where T:class=>Bot as T;
    }
    public class PlayerLocomotion {public float Crouch; public float GetCrouch()=>Crouch; public int m_currentStateEnum;public PlayerAgent m_owner; }
    public class PlayerAIBot:NativeObject { public PlayerAgent Agent; public FlashlightInventory Inventory=new(); public Sync SyncValues=new();public class Sync { public Vector3 LookDirection; } }
    public static class NativeSwitchHooks {
        internal static object Call(string name,params object[] args)=>typeof(ControlHooks).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        internal static bool Allows(string name,object instance,bool value)=>(bool)Call(name,instance,value);
    }
    public class PlayerSync {public PlayerAgent Agent;public PlayerAgent m_agent=>Agent;public int Requests;public bool ThrowRequest,BroadcastOnly,LastValue;public void WantsToSetFlashlightEnabled(bool value,bool broadcastOnly){if(!NativeSwitchHooks.Allows("FlashlightRequest",this,value))return;Requests++;LastValue=value;BroadcastOnly=broadcastOnly;if(ThrowRequest)throw new Exception("sync failed");if(!broadcastOnly)Agent.Bot?.Inventory.ReceiveSetFlashlightStatus(value,false);}}
    public class FlashlightInventory:PlayerInventoryBase {
        public FlashlightInventory(){m_flashlightCLight.m_unityLight=m_flashlight;m_flashlightCLight.m_clusterLight.UnityLight=m_flashlight;}
        public static Action OnReceive;public static bool AssetsAvailable=true;public static int MissingAssetAttempts;public bool RequestOnly,IgnoreReceive,ThrowSet,FailShadow,ThrowDetach;public int Calls,Receives;
        public int WeaponSetups,HelmetSetups;
        public override void OnItemEquippableFlashlightWielded(ItemEquippable item,Gear.GearPartFlashlight part){
            if(!(bool)NativeSwitchHooks.Call("BeforeWieldLight",this,item,part))return;WeaponSetups++;RequireAssets();ApplyBlock(part.m_settingsID);m_flashlightCLight.UpdateTransformData();ControlRuntime.Current?.AfterNativeLightSetup(Owner);
        }
        public override void PrepareHelmetFlashlight(){if(!(bool)NativeSwitchHooks.Call("BeforeHelmetLight",this))return;HelmetSetups++;RequireAssets();ApplyBlock(1);m_flashlightCLight.UpdateTransformData();ControlRuntime.Current?.AfterNativeLightSetup(Owner);}
        static void RequireAssets(){if(!AssetsAvailable){MissingAssetAttempts++;throw new KeyNotFoundException("AssetShardManager helmet resource missing");}}
        void ApplyBlock(uint id){var block=GameData.GameDataBlockBase<GameData.FlashlightSettingsDataBlock>.Wrapper.Blocks.Find(b=>b.persistentID==id);m_flashlight.intensity=block?.intensity??0;m_flashlight.range=block?.range??0;m_flashlight.spotAngle=Math.Max(1,block?.angle??0);}
        public override void SetFlashlightEnabled(bool value,bool helmet,bool sound){if(!NativeSwitchHooks.Allows("FlashlightSet",this,value))return;Calls++;if(ThrowSet)throw new Exception("native flashlight failed");FlashlightEnabled=value;if((WieldedItem==null||!WieldedItem.HasFlashlight)&&helmet)PrepareHelmetFlashlight();m_flashlightCLight.SetOn(value);}
        public override void ReceiveSetFlashlightStatus(bool value,bool sound){if(!NativeSwitchHooks.Allows("FlashlightReceive",this,value))return;OnReceive?.Invoke();Receives++;if(!IgnoreReceive){WantsFlashlightEnabled=value;SetFlashlightEnabled(value,true,sound);}}
        public override void UnWieldFlashlight(){
            object[] args={this,null};NativeSwitchHooks.Call("BeforeLightHandoff",args);Exception error=null;
            try {SetFlashlightEnabled(false,false,false);if(ThrowDetach)throw new Exception("detach failed");}
            catch(Exception e){error=e;throw;}
            finally {NativeSwitchHooks.Call("FinishLightHandoff",error,args[1]);}
        }
        public void Render(){
            var cluster=m_flashlightCLight.m_clusterLight;var spot=cluster.TryCast<CL_SpotLight>();
            CullingSystem.C_CullingManager.VisibleSpotLights.Remove(m_flashlightCLight);
            m_flashlightCLight.IsShown=cluster.IsActive=false;
            if(!cluster.m_isOn||spot==null)return;
            if(FailShadow){spot.ShadowGenerator.Allocated=false;cluster.m_isOn=false;CL_ShadowAtlas.TempDisableLight(cluster);return;}
            spot.ShadowGenerator.Allocated=true;cluster.UpdateTransform();
            m_flashlightCLight.IsShown=cluster.IsActive=true;CullingSystem.C_CullingManager.VisibleSpotLights.Add(m_flashlightCLight);
        }

    }
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
    public readonly record struct ControlView(CameraTarget Target,Vector3 Position,Vector3 Direction,float Fov,bool Enlarged,bool UiBlocked,bool Illuminate=false);
    public readonly record struct ControlMarker(CameraTarget Target,Vector3 Position,string Label,bool CanStation);
    public readonly record struct ControlAim(CameraTarget Target,Vector3 Position,Vector3 Direction);
    public static class HeadCameraApi {
        public static bool IsVisible=true,ThrowClear;
        public static float DefaultFieldOfView=75;
        public static CameraTarget Selected;
        public static Func<CameraTarget,bool> Request;
        public static string LastNotice;
        public static Func<CameraTarget,ControlView?> View;
        public static bool ReportControlFeedback(object o,string text){LastNotice=text;return true;}
        public static bool RegisterControlMarker(object o,Func<ControlMarker?> m)=>true;
        public static Action<string> ExitReason;public static bool RegisterControlExitReason(object o,Action<string> exit){ExitReason=exit;return true;}
        public static bool TrySetVisible(bool value){IsVisible=value;return true;}
        public static bool ReportControlNotice(object owner,string message) { LastNotice=message;return true; }
        public static bool RegisterControlInput(object o,Func<CameraTarget,bool> request,Func<bool> busy,Action exit,Func<string> status,float window) { Request=request;return true; }
        public static bool RegisterControlView(object o,Func<CameraTarget,ControlView?> view){View=view;return true;}
        public static bool TryGetSelected(out CameraTarget target) { target=Selected;return true; }
        public static bool TryGetControlAim(out ControlAim aim) { aim=new(Selected,new(0,1.6f,0),new(0,0,1));return true; }
        public static void ClearControlView(object o) { if(ThrowClear)throw new Exception("camera cleanup"); }
        public static void UnregisterControlInput(object o) { Request=null; }
    }
}
namespace PreLuna.MicroMaster {
    public class Setting<T> { public T Value;public Setting(T value){Value=value;} }
    public class Plugin {
        public static Setting<BotFlashlightMode> LightMode=new(BotFlashlightMode.ControlledOnly);public static Setting<bool> NativeLight=new(true);
        public static void LoadStep(string name,Action action)=>action();
        public const string Guid="micro";public static Plugin Instance=new();public Logger Log=new();
        public class Logger { public List<string> Messages=new();public void LogInfo(string s)=>Messages.Add(s);public void LogWarning(string s)=>Messages.Add(s); }
        public static Setting<bool> Enabled=new(true),InvertMouse=new(false),ThreatExit=new(true);
        public static Setting<float> DoubleClickWindow=new(.3f),MouseSensitivity=new(1),ThreatRadius=new(15),ThreatConfirmation=new(.25f);
        public static Setting<int> ThreatCount=new(3);
        public static Setting<KeyCode> EmergencyKey=new(KeyCode.F8),ToggleViewKey=new(KeyCode.F7),ResetViewKey=new(KeyCode.Home);
    }
    public class NativeManualWalk:IManualWalk {
        public static bool ThrowStop,Blocked;public static int Stops,Clears;public static Player.PlayerBotActionBase.Descriptor Own;
        public Player.PlayerAIBot Bot;
        public NativeManualWalk(Player.PlayerAIBot bot){Bot=bot;}
        public bool Valid=>true;
        public bool IsOwn(Player.PlayerBotActionBase.Descriptor d)=>d!=null&&ReferenceEquals(d,Own);public void ClearPath(){Clears++;}
        public bool RefreshBeforeNativeStep(Direction3 direction)=>!Blocked;
        public bool TryDirection(Direction3 direction)=>true;
        public void Stop(){Stops++;if(ThrowStop)throw new Exception("walk cleanup");}
    }
}

namespace PreLuna.Captain.Tasks {
    public readonly record struct ControlMarkView(float X,float Y,float Z,string Label,bool CanStation);
    public static class ControlPresentation {
        public static bool TryGetMark(long ticket,out ControlMarkView mark){mark=default;return false;}
    }
}
