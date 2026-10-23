namespace UnityEngine {
    public struct Vector3 { public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z; }
    public static class Time { public static float unscaledTime; }
}
namespace PreLuna.HeadCamera {
    internal sealed class Plugin {
        internal sealed class Setting { public float Value=80; }
        internal static Setting FieldOfView=new();
        internal static Plugin Instance=new();
        internal readonly TestLog Log=new();
        internal sealed class TestLog { internal void LogWarning(string message){} }
    }
    internal sealed class CameraRuntime {
        internal static CameraRuntime Current=new();internal bool OnMainThread=true,Opened=true;internal CameraTarget? Selected;
        internal bool SelectSlot(int slot)=>false;internal bool SetVisible(bool visible){Opened=visible;return true;}
        internal bool TryReadHudIdentity(out HeadHudIdentity identity){
            identity=HudViewIdentityPolicy.Resolve(true,Opened,Opened,true,HeadCameraApi.Busy,HeadCameraApi.Enlarged,
                Selected?.AgentIdentity??0,0,Selected?.OwnerIdentity??0,Selected?.Slot??-1);
            return true;
        }
    }
}
