using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib
{
    public static class Priority { public const int First=800, Last=0; }
    public sealed class HarmonyMethod { public int priority; public string[] before,after; public HarmonyMethod(Type type,string name){} }
    public sealed class Harmony { public static Dictionary<string,HarmonyMethod> Patches=new(); public Harmony(string id){} public void Patch(MethodInfo method,HarmonyMethod prefix=null,HarmonyMethod postfix=null) { if(method==null)throw new Exception("missing target"); Patches[method.Name]=prefix??postfix; } public void UnpatchSelf(){} }
    public static class AccessTools { public static Type TypeByName(string name)=>typeof(AccessTools); public static MethodInfo Method(Type type,string name)=>type.GetMethod(name); }
}
namespace PreLuna.Captain { internal static class KnownTypes { public static Type Get(string assembly,string name)=>typeof(HarmonyLib.AccessTools); } }
namespace Localization {
    public class LocalizedText {
        string text; public uint Id; public bool FailWrite;
        public string UntranslatedText {get=>text;set {if(FailWrite)throw new InvalidOperationException("invalid native text");text=value;}}
        // Reproduce the native wrapper's unsafe comparison: null checks must not call it.
        public static bool operator !=(LocalizedText left,LocalizedText right)=>throw new NullReferenceException("unsafe native comparison");
        public static bool operator ==(LocalizedText left,LocalizedText right)=>throw new NullReferenceException("unsafe native comparison");
        public override bool Equals(object other)=>ReferenceEquals(this,other);
        public override int GetHashCode()=>base.GetHashCode();
    }
}
namespace LevelGeneration
{
    public enum TERM_Command { None=0, InvalidCommand=10, Ping=20, Unique1=38 }
    public enum TERM_CommandRule { Any=0 }
    public sealed class LG_ComputerTerminalCommandInterpreter
    {
        public Dictionary<string,TERM_Command> m_commandsPerString=new();
        public Dictionary<TERM_Command,string> m_commandsPerEnum=new();
        public bool SlotAvailable=true, AddAllowed=true;
        public int Added;
        public void SetupCommands(){} public void EvaluateInput(string input){} public void ReceiveCommand(TERM_Command command,string text,string param1,string param2){}
        public bool TryGetUniqueCommandSlot(out TERM_Command command) { command=TERM_Command.Unique1;return SlotAvailable; }
        public bool AddCommand(TERM_Command command,string name,Localization.LocalizedText text,TERM_CommandRule rule)
        {
            Added++; PreLuna.Captain.TerminalHelpTranslation.Apply(name,text,_=>{});
            if(!AddAllowed||m_commandsPerEnum.ContainsKey(command))return false;
            var key=name.ToLowerInvariant();m_commandsPerString[key]=command;m_commandsPerEnum[command]=key;return true;
        }
    }
}
namespace PreLuna.Captain
{
    public static class Plugin { public const string Guid="PreLuna.Captain"; public static LoggerMock Logger=new(); }
    public sealed class LoggerMock { public void LogWarning(object message){} public void LogError(object message){} public void LogInfo(object message){} }
}
