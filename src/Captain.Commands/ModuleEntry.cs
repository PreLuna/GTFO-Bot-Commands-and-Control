using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[assembly: AssemblyVersion("0.5.39.0")]
[assembly: AssemblyFileVersion("0.5.39.0")]

namespace PreLuna.Captain.Tasks
{
    [BepInPlugin("PreLuna.Captain.Commands", "你是队长·更完善的指令", "0.5.39")]
    [BepInDependency(Plugin.Guid, "0.5.4")]
    [BepInDependency("CHTR", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("PreLuna.CoopBotsCustom", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ModuleEntry : BasePlugin
    {
        public override void Load()
        {
            ItemTaskAdapter.Configure(Plugin.SharedConfig);
            ItemTaskAdapter.Install();
            if (ItemTaskAdapter.Installed) BotControlService.Install();
            Plugin.SharedConfig.Save();
            Log.LogInfo("队长指令0.5.39加载完成；第五批接管生命周期与身份失效清理、原子交互退出收尾；接管指定击杀／集火与确认接单后退出驻守；既有任务及驻守规则保留。");
        }
        public override bool Unload()
        { try { BotControlService.Shutdown(); } finally { ItemTaskAdapter.Uninstall(); } return true; }
    }
}














