using System;
using System.Collections.Generic;
using System.Reflection;
namespace BepInEx {
 public class BepInPlugin:Attribute {public BepInPlugin(string id,string name,string version){}}
 public class BepInDependency:Attribute {public enum DependencyFlags{SoftDependency}public BepInDependency(string id,DependencyFlags flags){}}
}
namespace BepInEx.Configuration {
 public class ConfigEntry<T> {public T Value;public ConfigEntry(T value){Value=value;}}
 public class ConfigDescription {public ConfigDescription(string text,object range){}}
 public class AcceptableValueRange<T> {public AcceptableValueRange(T min,T max){}}
 public class ConfigFile {
  readonly Dictionary<(string,string),object> values=new();public int Saves;
  public void Set<T>(string section,string key,T value)=>values[(section,key)]=new ConfigEntry<T>(value);
  public T Value<T>(string section,string key)=>((ConfigEntry<T>)values[(section,key)]).Value;
  public ConfigEntry<T> Bind<T>(string section,string key,T value,object description){if(!values.TryGetValue((section,key),out var entry)){entry=new ConfigEntry<T>(value);values[(section,key)]=entry;}return (ConfigEntry<T>)entry;}
  public void Save(){Saves++;}
 }
}
namespace BepInEx.Unity.IL2CPP {
 public class Logger {public List<string> Messages=new();public void LogInfo(string s)=>Messages.Add(s);public void LogWarning(string s)=>Messages.Add(s);public void LogError(string s)=>Messages.Add(s);}
 public class BasePlugin {public Configuration.ConfigFile Config=new();public Logger Log=new();public T AddComponent<T>() where T:new()=>new();public virtual void Load(){}public virtual bool Unload()=>true;}
}
namespace HarmonyLib {
 public class Harmony {public Harmony(string id){}public void Patch(MethodInfo method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}public void UnpatchSelf(){}}
 public class HarmonyMethod {public HarmonyMethod(Type type,string name){}}
 public static class AccessTools {public static MethodInfo Method(Type t,string n,Type[] p=null)=>null;}
}
namespace Il2CppInterop.Runtime.Injection {public static class ClassInjector {public static void RegisterTypeInIl2Cpp<T>(){}}}
namespace UnityEngine {public static class Object {public static void Destroy(object o){}}public enum KeyCode{T,RightArrow,LeftArrow,RightBracket,LeftBracket,Slash}}
public class ClusteredRendering{}
public class PreLitVolume {public static List<int> s_activeFogSpheres=new();}
public enum eFocusState {FPS}
public class FocusStateManager{}
namespace Player {public class PlayerManager {}}
namespace PreLuna.HeadCamera {
 internal class CameraRuntime {internal static CameraRuntime Current;internal void ObserveFocus(){}internal void ResetLevel(){}internal void Shutdown(){Current=null;}}
 internal class CameraRenderCallbacks{}
 internal static class NativeCapture {internal static bool OwnsCluster(ClusteredRendering r)=>false;internal static bool OwnsFog(PreLitVolume f)=>false;}
 internal static class FogCompatibility {internal static void Install(HarmonyLib.Harmony h){}internal static void Uninstall(){}}
}
