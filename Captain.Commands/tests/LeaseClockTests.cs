using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using PreLuna.Captain.Tasks;
using HarmonyLib;
static class LeaseClockTests
{
    static int checks;
    public static float ExternalUntil;
    static void Check(bool value,string message) { checks++; if (!value) throw new Exception(message); }
    static List<CodeInstruction> Patch(IEnumerable<CodeInstruction> code) => ((IEnumerable<CodeInstruction>)typeof(BrokerHeartbeatPatch)
        .GetMethod("Transpile",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{code})).ToList();
    static void Main()
    {
        int reproduced=0;
        for(int i=1;i<20000;i++) {
            float now=i*.01337f, until=now+15f;
            if(now<until-15f) { reproduced++; Check(LeaseClockPolicy.Valid(now,until),"新租约浮点舍入不再被当作时钟回退"); }
        }
        Check(reproduced>0,"确实复现旧下界判断错误");
        Check(!LeaseClockPolicy.Valid(116,115),"保留原15秒过期上限");
        Check(!LeaseClockPolicy.Valid(99.9f,115),"真实时钟回退仍拒绝");
        Check(!LeaseClockPolicy.Valid(float.NaN,115),"无效时钟不通过");
        var field=typeof(LeaseClockTests).GetField(nameof(ExternalUntil));
        var code=new[]{new CodeInstruction(OpCodes.Ldfld,field),new CodeInstruction(OpCodes.Ldc_R4,15f),new CodeInstruction(OpCodes.Sub),new CodeInstruction(OpCodes.Ldc_R4,15f)};
        var patched=Patch(code);
        Check((float)patched[1].operand==LeaseClockPolicy.LowerWindow&&(float)patched[3].operand==15f,"只修改ExternalUntil减法下界，不改过期时长或其他常量");
        bool refused=false;try{Patch(Array.Empty<CodeInstruction>());}catch(TargetInvocationException e){refused=e.InnerException is InvalidOperationException;}
        Check(refused,"不匹配仲裁实现必须拒绝补丁");
        Console.WriteLine("PASS: "+checks+" lease-clock float and transpiler checks; reproduced="+reproduced+"; no native game executed.");
    }
}
namespace HarmonyLib
{
    public class CodeInstruction { public OpCode opcode; public object operand;
        public CodeInstruction(OpCode op,object value=null){opcode=op;operand=value;}
        public CodeInstruction(CodeInstruction other){opcode=other.opcode;operand=other.operand;} }
    public class Harmony { public Harmony(string id){} public void Patch(MethodInfo method,HarmonyMethod transpiler=null){} public void UnpatchSelf(){} }
    public class HarmonyMethod { public HarmonyMethod(Type type,string name){} }
    public static class AccessTools { public static MethodInfo Method(Type type,string name)=>type.GetMethod(name); }
}
namespace PreLuna.Captain { public static class Plugin { public static Log Logger=new();public class Log{public void LogInfo(string message){}} } }
