using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[assembly: AssemblyVersion("0.5.43.0")]
[assembly: AssemblyFileVersion("0.5.43.0")]

namespace PreLuna.Captain.Tasks
{
    [BepInPlugin("PreLuna.Captain.Commands", "你是队长·更完善的指令", "0.5.43")]
    [BepInDependency(Plugin.Guid, "0.5.4")]
    [BepInDependency("CHTR", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("PreLuna.CoopBotsCustom", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class ModuleEntry : BasePlugin
    {
        public override void Load()
        {
            Log.LogInfo("[启动阶段] Captain.Commands 0.5.43 BEGIN pid=" + System.Environment.ProcessId);
            StartupStep("读取任务配置", () => ItemTaskAdapter.Configure(Plugin.SharedConfig));
            StartupStep("安装指令系统", ItemTaskAdapter.Install);
            if (ItemTaskAdapter.Installed) StartupStep("注册接管服务", BotControlService.Install);
            StartupStep("保存配置", Plugin.SharedConfig.Save);
            Log.LogInfo("队长指令0.5.43加载完成；手动接管不再使用AI危险预判拒绝；集火／指定击杀交接、普通战斗动作停止后接管，受保护交互短暂等待并显示原因；即时搜寻不占用机器人。");
        }
        internal static void StartupStep(string name, System.Action action)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Plugin.Logger.LogInfo("[启动阶段] Commands BEGIN " + name);
            try { action(); Plugin.Logger.LogInfo("[启动阶段] Commands END " + name + " ms=" + watch.ElapsedMilliseconds); }
            catch (System.Exception error) { Plugin.Logger.LogError("[启动阶段] Commands FAIL " + name + " ms=" + watch.ElapsedMilliseconds + "\n" + error); throw; }
        }
        public override bool Unload()
        { try { BotControlService.Shutdown(); } finally { try { ItemTaskAdapter.Uninstall(); } finally { BotTaskBridge.Uninstall(); } } return true; }
    }
}














