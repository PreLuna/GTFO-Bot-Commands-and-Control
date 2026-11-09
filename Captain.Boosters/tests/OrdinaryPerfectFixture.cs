using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppStructArray<T> : List<T> { public Il2CppStructArray(T[] a):base(a){} public Il2CppStructArray(int n){for(int i=0;i<n;i++)Add(default);} public int Length=>Count; }
}
public enum BoosterImplantCategory { Muted, Bold, Aggressive }
public class BoosterImplant
{
    static int next; public IntPtr Pointer=new(++next); public uint InstanceId=1,TemplateId=42; public int Uses=3;
    public BoosterImplantCategory Category=BoosterImplantCategory.Aggressive;
    public struct Effect { public uint Id; public float Value; }
    public Il2CppStructArray<Effect> Effects=new(new[]{new Effect{Id=10,Value=1.2f},new Effect{Id=34,Value=.9f}});
    public Il2CppStructArray<uint> Conditions=new(new uint[]{5});
}
public class BoosterImplantInventoryItem { public BoosterImplant Implant; }
public class PersistentInventoryManager
{
    public static PersistentInventoryManager Current=new();
    public static List<BoosterImplantInventoryItem>[] Stock={new(),new(),new()}; public int Prepared;
    public static List<BoosterImplantInventoryItem> GetBoosterImplantInventory(BoosterImplantCategory c)=>Stock[(int)c];
    [MethodImpl(MethodImplOptions.NoInlining)] public void PrepareBoosterImplantForInjection(uint id){Prepared++;}
}
public enum eGameStateName { Lobby, InLevel }
public static class GameStateManager { public static eGameStateName CurrentStateName; public static bool IsReady; }
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup; } }
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; }
    public class ConfigFile { public bool Mode=true; public ConfigEntry<T> Bind<T>(string s,string k,T v,string d)=>new(){Value=k=="简化普通模式"?(T)(object)Mode:v}; }
}
namespace BoosterImplants
{
    public struct pBoosterEffectData { public uint BoosterEffectID; public float EffectValue; }
    public class pBoosterImplantData
    {
        public const int IMPLANT_EFFECT_SYNC_COUNT=8,IMPLANT_CONDITION_SYNC_COUNT=8;
        public uint BoosterImplantID; public int BoosterEffectCount,ConditionCount;
        public Il2CppStructArray<pBoosterEffectData> BoosterEffectDatas; public Il2CppStructArray<uint> Conditions;
    }
    public class BoosterImplantManager
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update(){}
        [MethodImpl(MethodImplOptions.NoInlining)] public static pBoosterImplantData ActiveBoosterImplantToData(BoosterImplant b)
            =>new(){BoosterImplantID=b.TemplateId,BoosterEffectCount=b.Effects.Count,ConditionCount=b.Conditions.Count,
                Conditions=new(b.Conditions.ToArray()),BoosterEffectDatas=new(b.Effects.ConvertAll(e=>new pBoosterEffectData{BoosterEffectID=e.Id,EffectValue=e.Value}).ToArray())};
    }
    public class PlayerBoosterImplantState
    {
        public BoosterImplant Implant; public bool ConditionsMet; public int Checks;
        [MethodImpl(MethodImplOptions.NoInlining)] public bool CheckCondition(uint id){Checks++;return false;}
        // Real game performs this dictionary lookup inline; never calls CheckCondition.
        [MethodImpl(MethodImplOptions.NoInlining)] public void Update(){ConditionsMet=Implant.Conditions.Count==0;}
    }
}
namespace PreLuna.Captain
{
    public class TestLog { public void LogInfo(string s)=>Console.WriteLine(s); public void LogWarning(string s)=>Console.WriteLine(s); public void LogError(string s)=>Console.WriteLine(s); }
    public static class Plugin { public const string Guid="PreLuna.Captain"; public static TestLog Logger=new(); }
    public static class KnownTypes { public static bool Available=true; public static Type Get(string a,string n)=>Available?Type.GetType(n):null; }
}
namespace Hikaria.BoosterTweaker.Managers
{
    public static class BoosterImplantTemplateManager
    {
        public static bool EnableCustomPerfectBooster { [MethodImpl(MethodImplOptions.NoInlining)]get; [MethodImpl(MethodImplOptions.NoInlining)]set; }
        public static bool EnableCustomBooster { [MethodImpl(MethodImplOptions.NoInlining)]get; [MethodImpl(MethodImplOptions.NoInlining)]set; }
        public static bool DisableBoosterConditions { [MethodImpl(MethodImplOptions.NoInlining)]get; [MethodImpl(MethodImplOptions.NoInlining)]set; }
        public static bool DisableBoosterNegativeEffects { [MethodImpl(MethodImplOptions.NoInlining)]get; [MethodImpl(MethodImplOptions.NoInlining)]set; }
        public class Effect { public uint BoosterImplantEffect{get;set;} public float EffectMaxValue{get;set;} }
        public class Template
        {
            public uint BoosterImplantID{get;set;}=42; public BoosterImplantCategory ImplantCategory{get;set;}=BoosterImplantCategory.Aggressive;
            public List<List<Effect>> EffectGroups{get;set;}=new(){new(){new(){BoosterImplantEffect=10,EffectMaxValue=1.3f},new(){BoosterImplantEffect=34,EffectMaxValue=.95f}}};
            public List<List<uint>> ConditionGroups{get;set;}=new(){new(){5}};
        }
        public static List<Template> BoosterImplantTemplates{get;}=new();
        public static float Max=1.3f;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void LoadTemplateData(){BoosterImplantTemplates.Clear();var t=new Template();t.EffectGroups[0][0].EffectMaxValue=Max;BoosterImplantTemplates.Add(t);}
    }
}
namespace Hikaria.BoosterTweaker.Features
{
    public static class PerfectBooster
    {
        public static SettingsType Settings{get;set;}
        public class SettingsType
        {
            public bool EnablePerfectBooster { [MethodImpl(MethodImplOptions.NoInlining)]get; [MethodImpl(MethodImplOptions.NoInlining)]set; }
            public bool DisableBoosterConsume { [MethodImpl(MethodImplOptions.NoInlining)]get; [MethodImpl(MethodImplOptions.NoInlining)]set; }
            public bool EnableBoosterFarmer { [MethodImpl(MethodImplOptions.NoInlining)]get; [MethodImpl(MethodImplOptions.NoInlining)]set; }
        }
        public static int OriginalCalls;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void ApplyPerfectBoosterImplants(){OriginalCalls++;}
    }
}
namespace TheArchive.Core.FeaturesAPI
{
    public static class FeatureManager { public static bool Active=true; public static bool IsFeatureEnabled(string s,bool g){if(s!="Hikaria.BoosterTweaker.Features.PerfectBooster"||!g)throw new Exception("Wrong feature query");return Active;} }
}
