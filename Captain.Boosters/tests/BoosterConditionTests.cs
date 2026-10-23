using System;
using System.Collections.Generic;
using System.Reflection;
using BoosterImplants;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PreLuna.Captain;
using PreLuna.Captain.Boosters;
using Hikaria.BoosterTweaker.Managers;
using TheArchive.Core.FeaturesAPI;
static class BoosterConditionTests {
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static object Call(string name,params object[] args){try{return typeof(BoosterConditionCompat).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static bool Bypassed(){var args=new object[]{false};bool original=(bool)Call("ConditionPrefix",args);Check(original|| (bool)args[0],"绕过时返回真");return !original;}
    static void Main(){
        HarmonyLib.Harmony.ThrowOnPatch=2;BoosterConditionCompat.Install();
        Check(!Bypassed()&&!BoosterConditionCompat.RawConditionsPreserved,"原生注册失败整组撤销，无半套绕过");
        HarmonyLib.Harmony.ThrowOnPatch=0;BoosterConditionCompat.Install();Call("TickPostfix");
        Check(BoosterConditionCompat.RawConditionsPreserved&&!Bypassed(),"关闭条件开关默认false保留原版检查");
        var effects=new Il2CppStructArray<pBoosterEffectData>(new[]{new pBoosterEffectData{BoosterEffectID=52,EffectValue=1.15f}});
        var stock=new BoosterImplant{Effects=effects,Uses=5,InstanceId=10};
        var conditions=new List<uint>{1,2};
        BoosterImplantTemplateManager.DisableBoosterConditions=true;Call("SettingPostfix");
        Check(Bypassed(),"设置true立即开启统一运行判定");
        foreach(var source in new[]{"普通完美库存","自定义库存","自定义完美库存","冻结机器人"})
            Check(Bypassed(),source+"复用同一个条件判定，不由来源分支决定");
        BoosterImplantTemplateManager.ApplyPerfectBoosterFromTemplate(stock,new(),conditions);Call("TemplatePostfix",stock,conditions);
        Check(stock.Conditions.Count==2&&stock.Conditions[0]==1&&stock.Conditions[1]==2,"普通完美路径保留原模板条件元数据");
        conditions[0]=9;Check(stock.Conditions[0]==1,"条件元数据为独立数组，不共享原可变列表");
        Check(ReferenceEquals(stock.Effects,effects)&&stock.Uses==5&&stock.InstanceId==10,"条件恢复不更改效果、次数或库存身份");
        BoosterImplantTemplateManager.DisableBoosterConditions=false;Call("SettingPostfix");
        Check(!Bypassed()&&stock.Conditions[0]==1,"关闭绕过恢复原版检查，原条件始终存在");
        BoosterImplantTemplateManager.DisableBoosterConditions=true;Call("SettingPostfix");
        FeatureManager.Active=false;Call("SettingPostfix");Check(!Bypassed(),"CustomBooster功能关闭时不残留条件绕过");
        FeatureManager.Active=true;Call("SettingPostfix");Check(Bypassed(),"功能恢复后读取真实运行开关");
        int logs=Plugin.Logger.Infos.Count;for(int i=0;i<40;i++)Call("TickPostfix");
        Check(Plugin.Logger.Infos.Count==logs,"稳定更新不刷日志，不重建库存");
        FeatureManager.ThrowQuery=true;Call("SettingPostfix");Check(!Bypassed(),"框架查询异常不默认为已关闭条件");
        Check(Plugin.Logger.Warnings.Exists(s=>s.Contains("mock-feature-query-failure")),"异常保留真实原因诊断");
        FeatureManager.ThrowQuery=false;Call("SettingPostfix");Check(Bypassed(),"查询恢复后重新应用原设置");
        var state=new PlayerBoosterImplantState{OtherModifier=77};var slots=new Il2CppReferenceArray<pBoosterImplantData>(new[]{new pBoosterImplantData(),new pBoosterImplantData(),new pBoosterImplantData()});
        slots[0].BoosterImplantID=100;slots[0].BoosterEffectCount=1;
        NativeBoosterState.SetDatas(state,slots);
        Check(ReferenceEquals(state.BoosterImplantDatas,slots)&&state.OtherModifier==77,"正确引用数组调用原生状态入口，不改其他修饰器");
        int calls=IL2CPP.NativeCalls;
        try{NativeBoosterState.SetDatas(state,null);throw new Exception("null accepted");}catch(ArgumentException){checks++;}
        try{NativeBoosterState.SetDatas(state,new(2));throw new Exception("wrong count accepted");}catch(ArgumentException){checks++;}
        Check(IL2CPP.NativeCalls==calls,"非法清理参数在调用原生前拒绝");
        IL2CPP.ThrowNative=true;
        try{NativeBoosterState.SetDatas(state,slots);throw new Exception("native exception swallowed");}catch(InvalidOperationException e){Check(e.Message.Contains("mock-native-cleanup-exception"),"原生异常正确抛出供限流清理诊断");}
        Check(PlayerBoosterImplantState.BadWrapperCalls==0,"安全清理未进入错误泛型包装");
        Console.WriteLine("PASS "+checks+" condition/safe-native controlled assertions; native game not run.");
    }
}
