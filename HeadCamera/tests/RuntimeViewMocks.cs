using System;
using Player;
using UnityEngine;
public class NativeObject {
    static int serial; readonly int instance=++serial;public IntPtr Pointer;
    public NativeObject(){Pointer=new(instance);}public int GetInstanceID()=>instance;
}
namespace UnityEngine {
    public class MonoBehaviour:NativeObject {public MonoBehaviour(IntPtr p){Pointer=p;}}
    public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;}
    public class Camera{public float aspect=16f/9;}
    public class RenderTexture {public int width=1280,height=720;}
    public enum KeyCode {T,LeftArrow,RightArrow}
    public static class Time {public static float unscaledTime;}
    public static class Input {public static bool GetKeyDown(KeyCode k)=>false;}
    public static class Application {public static bool isFocused=true;}
}
namespace Il2CppInterop.Runtime.Attributes {public class HideFromIl2CppAttribute:Attribute{}}
public enum eGameStateName {InLevel,Lobby}
public enum eFocusState {FPS,Menu,ComputerTerminal}
public static class GameStateManager {public static eGameStateName CurrentStateName=eGameStateName.InLevel;}
public static class FocusStateManager {public static eFocusState CurrentState=eFocusState.FPS;}
public static class InputMapper {public static bool PlayerHasFocus=true;}
public static class PlayerChatManager {public static bool InChatMode;}
public enum eGameEventChatLogType {GameEvent}
public static class GuiManager {public static Layer PlayerLayer=new();public class Layer{public Events m_gameEventLog=new();}public class Events{public void AddLogItem(string m,eGameEventChatLogType t){}}}
public static class RenderPipe {public static bool Initialized=true;}
namespace Player {
    public class Owner:NativeObject {public bool IsBot;}
    public class PlayerAgent:NativeObject {
        public Owner Owner=new();public int PlayerSlotIndex,DimensionIndex;public bool Alive=true,IsBeingDestroyed,IsBeingDespawned;
        public string PlayerName="bot";public Vector3 Position;public FPSCamera FPSCamera=new();public Culler m_movingCuller=new();
    }
    public class FPSCamera {public Camera m_camera=new();}
    public class Culler {public object CurrentNode=new();}
    public class PlayerManager {public static PlayerManager Current=new();public PlayerAgent[] Slots=new PlayerAgent[4];
        public static PlayerAgent GetLocalPlayerAgent()=>Current?.Slots[0];public PlayerAgent GetPlayerAgentInSlot(int i)=>Slots[i];}
}
namespace PreLuna.HeadCamera {
    public enum FeedOutput {ToneMapped,TestPattern}
    internal class Setting<T> {internal T Value;internal Setting(T v){Value=v;}}
    internal class Plugin {
        internal static Plugin Instance=new();internal LogImpl Log=new();internal class LogImpl{internal void LogInfo(string s){}internal void LogWarning(string s){}}
        internal static Setting<bool> Enabled=new(true),Diagnostics=new(false);
        internal static Setting<KeyCode> ToggleKey=new(KeyCode.T),NextKey=new(KeyCode.RightArrow),PreviousKey=new(KeyCode.LeftArrow);
        internal static Setting<int> FramesPerSecond=new(30),Width=new(1280);internal static Setting<FeedOutput> OutputMode=new(FeedOutput.ToneMapped);
        internal static string SwitchHint="switch";internal static string KeyLabel(KeyCode key)=>key.ToString();
    }
    internal struct HeadHudIdentity{}
    internal static class HudViewIdentityPolicy {internal static HeadHudIdentity Resolve(params object[] args)=>default;}
    internal sealed class CameraHud {
        internal static CameraHud Last;internal bool IsVisible;internal string Status;
        internal CameraHud(){Last=this;}internal bool Ensure()=>true;
        internal void Show(RenderTexture texture,string title,string message){IsVisible=true;Status=message;}
        internal void Hide(){IsVisible=false;}internal void Dispose()=>Hide();internal void ShowTestPattern(string message=null){IsVisible=true;}
    }
    internal sealed class NativeCapture {
        internal static bool ThrowCapture,Occluded;internal static int Captures;internal RenderTexture Feed=new();internal RenderTexture Texture=>Occluded?null:Feed;
        internal static float Aspect; internal static int Disposals; internal void Initialize(Camera camera,int width,float aspect=16f/9){Aspect=aspect;}
        internal void Capture(PlayerAgent bot,Camera main,ControlView? view){Captures++;if(ThrowCapture)throw new Exception("render failed");}
        internal ControlView CapturedView(ControlView view)=>view;internal void Dispose(){Disposals++;}
    }
    internal class CaptureRestoreException:Exception{}
    public static class HeadCameraApi {
        internal static bool Busy,Suspended,HasViewProvider=true;internal static int Exits;internal static ControlView? Aim;
        internal static bool Enlarged=>Busy;internal static float DoubleClickWindow=.3f;
        internal static Func<CameraTarget,bool> RequestControl;
        internal static string LastExit;internal static void ExitControlFor(string reason){LastExit=reason;ExitControl();}
        internal static Action ExitControl=()=>{Exits++;Busy=false;};internal static Func<string> ControlStatus=()=>"control";
        internal static void BindHudThread(){}internal static void NotifyHudViewIdentityChanged(){}
        internal static void RecordControlAim(ControlView? v){Aim=v;}
        internal static bool TryView(CameraTarget selected,out ControlView view){view=new(selected,default,new(0,0,1),80,true,Suspended);return Busy&&HasViewProvider;}
    }
}
 public static class LayerManager {public const int MASK_WORLD=1,MASK_PLAYER_INTERACT_BLOCKERS=2,LAYER_DYNAMIC=3;}
namespace UnityEngine {public enum QueryTriggerInteraction{Ignore}public static class Physics {public static bool CheckSphere(Vector3 p,float r,int m,QueryTriggerInteraction q)=>false;}}
