# GTFO-Bot-Commands-and-Control

机器人与指令。本仓包含以下MOD／共享组件，各自维护文件放在自己的目录。

[定位入口](#modules) · [依赖与联动](#relations) · [开发与排查](#development) · [来源说明](#sources)

<a id="modules"></a>
## 定位入口

| 功能／组件 | 版本 | 修改位置与输入 | 维护方式 | 测试材料 |
| --- | --- | --- | --- | --- |
| AI／机器人协同 `CoopBotsCustom` | 1.0.12 | [CoopBotsCustom/baseline](CoopBotsCustom/baseline) | 冻结DLL＋身份迁移 | 由仓级入口／配方核对 |
| 队长公共核心与中文显示 `Captain` | 0.5.4 | [Captain/src](Captain/src) | 源码＋身份迁移 | [Captain/tests](Captain/tests) |
| 队长指令与任务仲裁 `Captain.Commands` | 0.5.39 | [Captain.Commands/src](Captain.Commands/src) | 源码＋身份迁移 | [Captain.Commands/tests](Captain.Commands/tests) |
| 人机强化剂 `Captain.Boosters` | 0.5.2 | [Captain.Boosters/src](Captain.Boosters/src) | 源码＋身份迁移 | [Captain.Boosters/tests](Captain.Boosters/tests) |
| 队长兼容修复 `Captain.Fixes` | 0.5.1 | [Captain.Fixes/src](Captain.Fixes/src) | 源码＋身份迁移 | [Captain.Fixes/tests](Captain.Fixes/tests) |
| 头戴摄像头 `HeadCamera` | 0.3.0 | [HeadCamera/src](HeadCamera/src) | 源码＋身份迁移 | [HeadCamera/tests](HeadCamera/tests) |
| 微操大师／直接接管 `MicroMaster` | 0.5.0 | [MicroMaster/src](MicroMaster/src) | 源码＋身份迁移 | [MicroMaster/tests](MicroMaster/tests) |

具体路径、版本、GUID、资源和依赖的机器登记见[repo.json](repo.json)。src是实现，tests是测试；IntegrationTests用于跨MOD场景或调度配方，Shared仅保存真正共享的接口。

仓级测试配方与共享场景见[IntegrationTests](IntegrationTests)。测试文件是否实际执行，以根Test.ps1及配方为准。

普通玩家从[Releases](https://github.com/PreLuna/GTFO-Bot-Commands-and-Control/releases)下载已经构建的安装ZIP，按对应版本说明安装并准备前置；不需要构建工具仓。退出游戏后将ZIP中的BepInEx合并到已配置前置的游戏目录，替换同功能旧版以免重复加载；个人CFG不包含在安装包中。GitHub的Source code／Code ZIP用于开发，不能把baseline原件直接当安装DLL。

<a id="relations"></a>
## 依赖与联动

Captain提供队长公共接口；Commands、Boosters、Fixes扩展指令、强化剂与兼容处理。MicroMaster的运行硬前置包含Captain、Commands、HeadCamera和CoopBotsCustom，涉及接管、任务交接和摄像头接口时共同回归。各MOD能否单独安装以硬前置为准，同仓不表示全部必装。

| 模块 | 插件硬前置／声明版本 | 可选联动／软依赖 |
| --- | --- | --- |
| CoopBotsCustom | 无插件硬前置声明 | UnityExplorer；ChatterReborn |
| Captain | 无插件硬前置声明 | ChatterReborn；CoopBotsCustom；SmartPing |
| Captain.Commands | Captain `0.5.4` | ChatterReborn；CoopBotsCustom |
| Captain.Boosters | Captain `>=0.5.1 <0.6.0` | TheArchive |
| Captain.Fixes | Captain `>=0.5.1 <0.6.0` | SmartPing |
| HeadCamera | 无插件硬前置声明 | 无插件软依赖声明 |
| MicroMaster | HeadCamera `0.3.0`；Captain `0.5.4`；Captain.Commands `0.5.39`；CoopBotsCustom `1.0.12` | 无插件软依赖声明 |

所有插件仍需GTFO及相应BepInEx IL2CPP环境。版本保留加载器原始声明表达式；编译引用、运行硬前置和可选桥分别判断。

当前编译引用：`Captain.Commands` → `Captain`；`Captain.Boosters` → `Captain`；`Captain.Fixes` → `Captain`；`MicroMaster` → `Captain`, `HeadCamera`。

跨仓关联：[机器人／摄像头与第三人称另一侧仓库](https://github.com/PreLuna/GTFO-Third-Person-Camera-and-HUD)。修改视角身份和契约时两侧联测；仅凭同仓分组不能推导安装依赖。

<a id="development"></a>
## 开发与排查

| 要修改／排查的功能 | 先读哪里 | 联动范围 |
| --- | --- | --- |
| 接管、紧急退出、手动移动 | [MicroMaster/src/ControlRuntime.cs](MicroMaster/src/ControlRuntime.cs) | 同时查ControlSafety、ControlHooks，以及Commands的任务交接；修改摄像头身份时连查HeadCamera。 |
| 指令菜单与任务仲裁 | [Captain.Commands/src](Captain.Commands/src) | 菜单、物品／开门／战斗／驻守分别定位对应Adapter或Policy；共享接口在Captain。 |
| 头戴摄像头与视角身份 | [HeadCamera/src](HeadCamera/src) | 同时核对MicroMaster；跨仓可选桥还涉及第三人称镜头／HUD。 |
| 强化剂与兼容 | [Captain.Boosters/src](Captain.Boosters/src) | 兼容修复另在Captain.Fixes/src；两者使用Captain公共能力。 |

冻结baseline保留原始WangLocal文件名、内部身份和SHA256，构建后输出PreLuna版；它不是完整主体源码。行为调整须先证明补丁输入链，不能只改DLL文件名。维护者本地历史冷档不随本仓提供，未据此宣称源码已经恢复。

开发环境：支持C#12编译的PowerShell 7、.NET 6 runtime、GTFO/BepInEx的core与生成的interop。将[共用工具仓](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools)放在本仓旁边，可通过BuildToolsRoot覆盖位置。

先审查并登记输入／维护锁，再在本仓目录执行（路径换成本机实际值，重复构建前显式清理本仓忽略的out）：

```powershell
./Build.ps1 -GameRoot 'D:/Games/GTFO' -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
./Test.ps1 -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
```

根Test.ps1调度21组回归。Captain翻译／类型、部分Commands、Boosters与Fixes仍有未接入该入口的测试；文件存在不代表已运行。

构建与离线测试不安装到游戏。`.cs`是功能／测试／辅助源码，`.ps1`是维护脚本，编译所得`.dll`才供游戏加载；运行CFG不加入源码仓。

维护锁与提交步骤见[共用维护流程](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#maintenance)，打包要求见[发布与打包](https://github.com/PreLuna/GTFO-Mod-Build-and-Verification-Tools#packaging)。仅使用Releases的玩家不需要这些开发步骤。

<a id="sources"></a>
## 来源说明

- CoopBotsCustom：BetterBots；作者 easternunit100；来源版本 0.1.9（工作区说明；当前构建输入为已魔改的r8冻结基线）。
- Captain：工作区你是队长项目；兼容ChatterReborn、Fridos Smart Ping及更好AI；作者 本分支自维护；兼容对象不等于被合并原作；来源版本 各子模块独立版本见当前条目。
- Captain.Commands：工作区你是队长项目；兼容ChatterReborn、Fridos Smart Ping及更好AI；作者 本分支自维护；兼容对象不等于被合并原作；来源版本 各子模块独立版本见当前条目。
- Captain.Boosters：工作区你是队长项目；兼容ChatterReborn、Fridos Smart Ping及更好AI；作者 本分支自维护；兼容对象不等于被合并原作；来源版本 各子模块独立版本见当前条目。
- Captain.Fixes：工作区你是队长项目；兼容ChatterReborn、Fridos Smart Ping及更好AI；作者 本分支自维护；兼容对象不等于被合并原作；来源版本 各子模块独立版本见当前条目。
- HeadCamera：工作区头戴摄像头实现；作者 本分支自维护；来源版本 0.3.0。
- MicroMaster：工作区微操大师实现；作者 本分支自维护；复用本分支队长和摄像头接口；来源版本 0.5.0。

保留源文件中的作者与版权声明。部分模组包含第三方派生实现或冻结基线，本说明不替代上游许可证，也不新增再分发授权；本仓库没有统一重授权声明。
