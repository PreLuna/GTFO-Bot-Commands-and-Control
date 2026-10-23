using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BoosterImplants;
using Hikaria.BoosterTweaker.Managers;
using TheArchive.Core.FeaturesAPI;
using PreLuna.Captain;
using PreLuna.Captain.Boosters;
static class BoosterConditionHarmonyTests {
    static int checks;static void Check(bool ok,string s){checks++;if(!ok)throw new Exception(s);}
    static void Main(string[] args){AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(args[0],n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};Run(args);}
    [MethodImpl(MethodImplOptions.NoInlining)] static void Run(string[] args){
        KnownTypes.Available=args[1]!="late";BoosterConditionCompat.Install();
        var human=new PlayerBoosterImplantState();var bot=new PlayerBoosterImplantState();var manager=new BoosterImplantManager();
        Check(!human.CheckCondition(BoosterCondition.Health),"默认原版条件检查仍运行");
        KnownTypes.Available=true;UnityEngine.Time.realtimeSinceStartup=3;manager.Update();
        Check(BoosterConditionCompat.RawConditionsPreserved,"晚绑定后完整元数据与运行开关钩子可用");
        BoosterImplantTemplateManager.DisableBoosterConditions=true;
        int nativeChecks=human.NativeChecks;
        Check(human.CheckCondition(BoosterCondition.Health)&&human.CheckCondition(BoosterCondition.Infection),"真人血量与感染条件由真实Harmony跳过");
        Check(bot.CheckCondition(BoosterCondition.Carry),"机器人携带物条件使用同一真实检查入口");
        Check(human.NativeChecks==nativeChecks,"开启时不执行原条件函数");
        var stock=new BoosterImplant();BoosterImplantTemplateManager.ApplyPerfectBoosterFromTemplate(stock,new(),new(){1,2});
        Check(stock.Conditions.Count==2&&stock.Conditions[0]==1&&stock.Effect==1.15f&&stock.Uses==5,"实际Harmony模板后缀保留原条件，不变更数值与次数");
        BoosterImplantTemplateManager.DisableBoosterConditions=false;
        Check(!human.CheckCondition(BoosterCondition.Health)&&!bot.CheckCondition(BoosterCondition.Carry),"setter真实后缀立即恢复真人与机器人检查");
        Check(stock.Conditions.Count==2,"关闭后无需重建原条件");
        BoosterImplantTemplateManager.DisableBoosterConditions=true;FeatureManager.Active=false;
        UnityEngine.Time.realtimeSinceStartup=4;manager.Update();Check(!bot.CheckCondition(BoosterCondition.Carry),"实际功能停用后恢复正常检查");
        FeatureManager.Active=true;FeatureManager.Fail=true;UnityEngine.Time.realtimeSinceStartup=5;manager.Update();Check(!human.CheckCondition(BoosterCondition.Health),"实际查询异常保持原检查");
        FeatureManager.Fail=false;UnityEngine.Time.realtimeSinceStartup=6;manager.Update();Check(bot.CheckCondition(BoosterCondition.Carry),"查询恢复重新应用真实设置");
        int logs=Plugin.Logger.Logs.Count;for(int i=0;i<100;i++)manager.Update();Check(Plugin.Logger.Logs.Count==logs,"稳定循环无多次同步或日志刷屏");
        Console.WriteLine("PASS "+checks+" real Harmony condition assertions ("+args[1]+"); controlled managed fixture, native game not run.");
    }
}
