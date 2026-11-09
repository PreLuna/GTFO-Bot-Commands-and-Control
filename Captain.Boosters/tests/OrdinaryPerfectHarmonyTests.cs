using System;
using System.IO;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using BoosterImplants;
using PreLuna.Captain;
using PreLuna.Captain.Boosters;
using Hikaria.BoosterTweaker.Features;
using M=Hikaria.BoosterTweaker.Managers.BoosterImplantTemplateManager;
static class OrdinaryPerfectHarmonyTests
{
    static int checks;
    static void Check(bool ok,string s){checks++;if(!ok)throw new Exception(s);}
    static void Main(string[] args){AssemblyLoadContext.Default.Resolving+=(_,n)=>{string p=Path.Combine(args[0],n.Name+".dll");return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;};Run(args);}
    [MethodImpl(MethodImplOptions.NoInlining)] static void Run(string[] args)
    {
        bool off=args[1]=="off", absent=args[1]=="absent"; KnownTypes.Available=args[1]!="late"&&!absent;
        BoosterConditionCompat.Configure(new BepInEx.Configuration.ConfigFile{Mode=!off});
        BoosterConditionCompat.Install();
        var b=new BoosterImplant();var originalEffects=b.Effects;var originalConditions=b.Conditions;
        PersistentInventoryManager.Stock[2].Add(new(){Implant=b});
        var manager=new BoosterImplantManager(); manager.Update();
        Check(b.Effects.Count==2 && b.Conditions.Count==1,"初始化未完成前不改库存");
        if(absent){PersistentInventoryManager.Current.PrepareBoosterImplantForInjection(1);Check(PersistentInventoryManager.Current.Prepared==1,"没有BoosterTweaker时原库存照常可装配");Check(BoosterImplantManager.ActiveBoosterImplantToData(b).ConditionCount==1,"可选模块不存在时不伪造完美效果");Console.WriteLine("PASS "+checks+" ordinary perfect Harmony assertions (absent).");return;}
        KnownTypes.Available=true;PerfectBooster.Settings=new();M.LoadTemplateData();
        UnityEngine.Time.realtimeSinceStartup=3;manager.Update();
        if(off){Check(b.Effects.Count==2&&b.Conditions.Count==1,"关闭简化模式保留原行为");PerfectBooster.ApplyPerfectBoosterImplants();Check(PerfectBooster.OriginalCalls==1,"关闭模式不拦原完美路径");Console.WriteLine("PASS "+checks+" ordinary perfect Harmony assertions (off).");return;}
        Check(b.Effects.Count==1&&b.Effects[0].Value==1.3f&&b.Conditions.Count==0,"实际Harmony使库存无条件无负面且当前模板满值");
        Check(originalEffects.Count==2&&originalConditions.Count==1,"不原地改源数组");
        Check(b.InstanceId==1&&b.TemplateId==42&&b.Uses==3,"保留库存身份与次数，无扣库存调用");
        Check(PerfectBooster.Settings.EnablePerfectBooster&&PerfectBooster.Settings.DisableBoosterConsume&&PerfectBooster.Settings.EnableBoosterFarmer,"启用已有完美不消耗快速刷取");
        M.EnableCustomBooster=true;M.EnableCustomPerfectBooster=true;M.DisableBoosterConditions=false;M.DisableBoosterNegativeEffects=false;
        PerfectBooster.Settings.EnablePerfectBooster=false;PerfectBooster.Settings.DisableBoosterConsume=false;
        Check(!M.EnableCustomBooster&&!M.EnableCustomPerfectBooster&&M.DisableBoosterConditions&&M.DisableBoosterNegativeEffects,"配置重载不能切回自定义／关闭清理");
        Check(PerfectBooster.Settings.EnablePerfectBooster&&PerfectBooster.Settings.DisableBoosterConsume,"普通完美和不消耗setter受实际Harmony保护");
        var human=new PlayerBoosterImplantState{Implant=b};human.Update();
        Check(human.ConditionsMet&&human.Checks==0&&!human.CheckCondition(5),"内联原生式检查满足；独立CheckCondition仍为false，未靠旧绕过");
        var data=BoosterImplantManager.ActiveBoosterImplantToData(b);
        Check(data.BoosterEffectCount==1&&data.ConditionCount==0&&data.BoosterEffectDatas[0].EffectValue==1.3f,"人机与真人原版同步数据相同");
        PersistentInventoryManager.Current.PrepareBoosterImplantForInjection(1);Check(PersistentInventoryManager.Current.Prepared==1,"准备走原版一次");
        PerfectBooster.ApplyPerfectBoosterImplants();Check(PerfectBooster.OriginalCalls==0&&b.Effects.Count==1,"重复刷新不误认被去负面的效果组");
        M.Max=1.35f;M.LoadTemplateData();PerfectBooster.ApplyPerfectBoosterImplants();
        Check(b.Effects[0].Value==1.35f&&b.Effects.Count==1,"模板重载用保存原始组重新匹配");
        var effects=b.Effects;for(int i=0;i<50;i++)BoosterImplantManager.ActiveBoosterImplantToData(b);
        Check(ReferenceEquals(effects,b.Effects),"稳定同步不重复分配／重写效果数组");
        var bad=new BoosterImplant{InstanceId=2,TemplateId=999};PersistentInventoryManager.Stock[2].Add(new(){Implant=bad});
        PersistentInventoryManager.Current.PrepareBoosterImplantForInjection(2);Check(PersistentInventoryManager.Current.Prepared==1,"模板未知时拒绝准备而非静默带入坏效果");
        Check(!BoosterConditionCompat.NormalizeItem(bad),"未知模板被人机目录的相同入口拒绝");
        Check(!BoosterConditionCompat.RawConditionsPreserved,"旧人机指纹不猜测迁移");
        TheArchive.Core.FeaturesAPI.FeatureManager.Active=false;UnityEngine.Time.realtimeSinceStartup=10;manager.Update();
        Check(!BoosterConditionCompat.NormalizeItem(b),"完美Feature关闭时不伪装成已正常化的有效同步");
        TheArchive.Core.FeaturesAPI.FeatureManager.Active=true;UnityEngine.Time.realtimeSinceStartup=13;manager.Update();
        Check(BoosterConditionCompat.NormalizeItem(b)&&BoosterImplantManager.ActiveBoosterImplantToData(b).BoosterImplantID==42,"Feature恢复后用相同库存恢复有效同步");
        Console.WriteLine("PASS "+checks+" ordinary perfect real Harmony assertions ("+args[1]+"); managed fixture, native gameplay not run.");
    }
}
