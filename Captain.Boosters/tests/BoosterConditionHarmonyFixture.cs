using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {public class Il2CppStructArray<T>:List<T>{public Il2CppStructArray(T[] a):base(a){}}}
public class BoosterImplant {public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<uint> Conditions;public float Effect=1.15f;public int Uses=5;}
namespace BoosterImplants {
    public enum BoosterCondition {Health,Infection,Carry}
    public class PlayerBoosterImplantState {
        public int NativeChecks;
        [MethodImpl(MethodImplOptions.NoInlining)] public bool CheckCondition(BoosterCondition condition){NativeChecks++;return false;}
    }
    public class BoosterImplantManager {[MethodImpl(MethodImplOptions.NoInlining)] public void Update(){}}
}
namespace UnityEngine {public static class Time {public static float realtimeSinceStartup;}}
namespace PreLuna.Captain {
    public class TestLogger {public List<string> Logs=new();public void LogInfo(string s){Logs.Add(s);Console.WriteLine(s);}public void LogWarning(string s){Logs.Add(s);Console.WriteLine(s);}public void LogError(string s){Logs.Add(s);Console.WriteLine(s);}}
    public static class Plugin {public const string Guid="PreLuna.Captain";public static TestLogger Logger=new();}
    internal static class KnownTypes {public static bool Available=true;public static Type Get(string assembly,string name)=>Available?Type.GetType(name):null;}
}
namespace Hikaria.BoosterTweaker.Managers {
    public static class BoosterImplantTemplateManager {
        static bool flag;
        public static bool DisableBoosterConditions {
            [MethodImpl(MethodImplOptions.NoInlining)] get=>flag;
            [MethodImpl(MethodImplOptions.NoInlining)] set=>flag=value;
        }
        public class EffectTemplate {}
        [MethodImpl(MethodImplOptions.NoInlining)] public static void ApplyPerfectBoosterFromTemplate(BoosterImplant implant,List<EffectTemplate> effects,List<uint> conditions){implant.Conditions=new(flag?Array.Empty<uint>():conditions.ToArray());}
    }
}
namespace TheArchive.Core.FeaturesAPI {
    public static class FeatureManager {public static bool Active=true,Fail;
        [MethodImpl(MethodImplOptions.NoInlining)] public static bool IsFeatureEnabled(string guid,bool useGuid){if(Fail)throw new InvalidOperationException("harmony-query-failure");return Active;}
    }
}
