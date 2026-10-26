using System;
using System.Collections.Generic;
public class NativeObject {static int serial;public IntPtr Pointer=new(++serial);public T TryCast<T>() where T:class=>this as T;}
public class CL_ShadowGenerator:NativeObject {public bool IsForced;public bool Allocated=true;}
public static class CL_ShadowAtlas {public static CL_ShadowGenerator ForcedShadowRender;}
public struct sCL_SpotLight {public UnityEngine.Vector3 Position,Irradiance;public float InvRangeSqr,InvNearRange,ShadowIndex;}
public class CL_Light:NativeObject {public bool m_isOn=true,IsActive=true;public int ActiveID=2;public UnityEngine.Light UnityLight=new();}
public class CL_SpotLight:CL_Light {public int Transforms;public UnityEngine.Vector3 CachedPosition;public void UpdateTransform(){Transforms++;CachedPosition=UnityLight.transform.position;}public bool IsFPSFlashlight;public int ShadowID=>ShadowGenerator.Allocated?0:-1;public CL_ShadowGenerator ShadowGenerator=new();public sCL_SpotLight Data=new(){Irradiance=new(1,1,1),InvRangeSqr=.01f,InvNearRange=.5f,ShadowIndex=0};}
public static class ClusteredRendering {public static int _FPSSpotIndex=5;}
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);}
 public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public struct Quaternion {public float Marker;public static Vector3 operator *(Quaternion q,Vector3 v)=>v;}
 public class Transform {public Vector3 position {get=>localPosition;set=>localPosition=value;}public Quaternion rotation {get=>localRotation;set=>localRotation=value;}public Vector3 localPosition,forward;public Quaternion localRotation;public void SetPositionAndRotation(Vector3 p,Quaternion r){position=p;rotation=r;}}
 public class Camera {public Transform transform=new();}
 public class Renderer:NativeObject {public bool ThrowRestore;bool hidden;public bool forceRenderingOff {get=>hidden;set{if(ThrowRestore&&!value){ThrowRestore=false;throw new Exception("renderer restore");}hidden=value;}}}
 public class MeshRenderer:Renderer {}public class SkinnedMeshRenderer:Renderer {}
 public class GameObject {public List<Renderer> Renderers=new();public IEnumerable<T> GetComponentsInChildren<T>(bool inactive) where T:class {foreach(var r in Renderers)yield return r as T;}}
 public enum QueryTriggerInteraction {Ignore}
 public static class Physics {public static Func<Vector3,float,int,QueryTriggerInteraction,bool> Check=(p,r,m,q)=>false;public static bool CheckSphere(Vector3 p,float r,int m,QueryTriggerInteraction q)=>Check(p,r,m,q);}
 public class Light:NativeObject {public float range=11,intensity=.35f,spotAngle=40;public Color color=new(1,1,1,1);public Transform transform=new();}
 public static class Shader {public static int Index;public static int GetGlobalInt(int id)=>Index;public static void SetGlobalInt(int id,int v){Index=v;}}
}
namespace CullingSystem {
 public class C_Light:NativeObject {public bool IsShown=true;public CL_Light m_clusterLight=new CL_SpotLight();public UnityEngine.Light m_unityLight;}
 public class C_LightDynamic:C_Light {
  public bool m_isFPSPlayerLight,ThrowOnce,ThrowTransformOnce;public int NativeRoleWrites,TransformWrites;public void UpdateTransformData(){TransformWrites++;if(ThrowTransformOnce){ThrowTransformOnce=false;throw new Exception("native transform");}}
  public C_LightDynamic(){m_unityLight=m_clusterLight.UnityLight;}
  public bool IsFPSPlayerLight {get=>m_isFPSPlayerLight;set{NativeRoleWrites++;m_isFPSPlayerLight=value;m_clusterLight.TryCast<CL_SpotLight>().ShadowGenerator.IsForced=value;if(value&&ThrowOnce){ThrowOnce=false;throw new Exception("native role failed");}}}
  public void SetOn(bool value){m_clusterLight.m_isOn=value;}
 }
 public static class C_CullingManager {public static List<C_Light> VisibleSpotLights=new();}
}
public static class LayerManager {public const int MASK_WORLD=1,MASK_PLAYER_INTERACT_BLOCKERS=2,LAYER_DYNAMIC=3;}
public class PlayerSyncModelData {public UnityEngine.GameObject[] m_gfxHead=Array.Empty<UnityEngine.GameObject>(),m_gfxTorso=Array.Empty<UnityEngine.GameObject>(),m_gfxArms=Array.Empty<UnityEngine.GameObject>(),m_gfxLegs=Array.Empty<UnityEngine.GameObject>();}
public class PlayerInventoryBase {public UnityEngine.Vector3 m_flashlightCameraOffset;public bool FlashlightEnabled=true,WantsFlashlightEnabled=true;public CullingSystem.C_LightDynamic m_flashlightCLight=new();public UnityEngine.Light m_flashlight;public PlayerInventoryBase(){m_flashlight=m_flashlightCLight.m_unityLight;}}
namespace Player {
 public class Owner {public bool IsBot=true;}
 public class PlayerAIBot {public PlayerInventoryBase Inventory=new();}
 public class PlayerAgent:NativeObject {public PlayerSyncModelData PlayerSyncModel=new();public Owner Owner=new();public PlayerAIBot Bot=new();public PlayerInventoryBase Inventory=>Bot.Inventory;public T GetComponent<T>() where T:class=>Bot as T;}
 public static class PlayerManager {public static PlayerAgent Local;public static PlayerAgent GetLocalPlayerAgent()=>Local;}
}
