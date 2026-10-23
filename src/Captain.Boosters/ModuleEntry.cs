using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[assembly: AssemblyVersion("0.5.2.0")]
[assembly: AssemblyFileVersion("0.5.2.0")]

namespace PreLuna.Captain.Boosters
{
    [BepInPlugin("PreLuna.Captain.Boosters", "你是队长·给人机配强化剂", "0.5.2")]
    // BepInEx 6 parses this as a semantic range. A bare "0.5.0" is an
    // exact constraint, so upgrading the core to 0.5.1 disabled the whole module.
    [BepInDependency(Plugin.Guid, ">=0.5.1 <0.6.0")]
    [BepInDependency("dev.AuriRex.gtfo.TheArchive", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ModuleEntry : BasePlugin
    {
        public override void Load()
        {
            BotBoosterAdapter.Configure(Plugin.SharedConfig);
            BotBoosterAdapter.Install();
            BoosterConditionCompat.Install();
            Plugin.SharedConfig.Save();
            Log.LogInfo("强化剂模块0.5.2加载完成；本局槽位快照跨机器人重加入保留，安全调用原生清理；真人与机器人统一可逆条件开关，保留局内只读及界面缓存。");
        }
    }
}
