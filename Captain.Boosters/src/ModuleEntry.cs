using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[assembly: AssemblyVersion("0.5.3.0")]
[assembly: AssemblyFileVersion("0.5.3.0")]

namespace PreLuna.Captain.Boosters
{
    [BepInPlugin("PreLuna.Captain.Boosters", "你是队长·给人机配强化剂", "0.5.3")]
    // BepInEx 6 parses this as a semantic range. A bare "0.5.0" is an
    // exact constraint, so upgrading the core to 0.5.1 disabled the whole module.
    [BepInDependency(Plugin.Guid, ">=0.5.1 <0.6.0")]
    [BepInDependency("dev.AuriRex.gtfo.TheArchive", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ModuleEntry : BasePlugin
    {
        public override void Load()
        {
            BotBoosterAdapter.Configure(Plugin.SharedConfig);
            BoosterConditionCompat.Configure(Plugin.SharedConfig);
            BotBoosterAdapter.Install();
            BoosterConditionCompat.Install();
            Plugin.SharedConfig.Save();
            Log.LogInfo("强化剂模块0.5.3已注册简化普通完美规则；等待BoosterTweaker就绪后处理库存，保留不消耗、已有快速刷取、人机三槽与局内冻结快照。实际就绪见[普通完美强化剂]日志。");
        }
    }
}
