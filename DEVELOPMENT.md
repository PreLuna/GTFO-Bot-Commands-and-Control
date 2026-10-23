# 开发与关联

## 修改入口

| MOD | 源码／输入 | 测试 |
| --- | --- | --- |
| AI／机器人协同 | `CoopBotsCustom/baseline/WangLocal.CoopBotsCustom.dll` | 当前无独立源码测试入口 |
| 队长公共核心与中文显示 | `Captain/src` | `Captain/tests` |
| 队长指令与任务仲裁 | `Captain.Commands/src` | `Captain.Commands/tests` |
| 人机强化剂 | `Captain.Boosters/src` | `Captain.Boosters/tests` |
| 队长兼容修复 | `Captain.Fixes/src` | `Captain.Fixes/tests` |
| 头戴摄像头 | `HeadCamera/src` | `HeadCamera/tests` |
| 微操大师／直接接管 | `MicroMaster/src` | `MicroMaster/tests` |

Captain 提供队长公共接口与中文显示，Commands、Boosters、Fixes分别扩展指令、强化剂与兼容修复。MicroMaster直接使用Captain、Commands、HeadCamera与CoopBotsCustom，因此它们接口变化时共同回归。Captain核心对AI、ChatterReborn、SmartPing是软依赖；CoopBotsCustom对UnityExplorer、ChatterReborn也是软依赖，不能把同仓收纳理解为全部必装。HeadCamera与第三人称仓通过可选视角身份接口联动。

仓库根Build.ps1/Test.ps1负责共同调度；IntegrationTests保存跨MOD场景和测试配方，单MOD测试主体在各自tests目录。文件存在不表示已被执行。

## 构建与测试

PowerShell 7需支持C#12编译，使用.NET 6 runtime和本机GTFO/BepInEx托管引用。将GTFO-Mod-Build-and-Verification-Tools放在本仓旁边。

```powershell
./Build.ps1 -GameRoot 'D:/Games/GTFO' -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
./Test.ps1 -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
```

路径换成本机目录，可指定BuildToolsRoot/DependencyRoot。输入路径由repo.json显式登记，不搜索旧根src或历史工作区。输出在忽略的out，重复构建前清理或移走out。构建不安装到游戏。

冻结DLL主体没有完整源码；本轮只移动baseline并保留SHA256，没有导入历史补丁。未来改行为需先恢复并核对补丁输入链。

现有根Test.ps1运行21组回归；Captain翻译/类型、部分Commands、Boosters与Fixes目录内尚有未登记到该入口的测试，必须分别说明，不能把全部文件宣称为已执行。历史测试恢复和补登记另行验证。

## 暂存与维护锁

审查Git差异并暂存有意提交的输入，再运行工具仓tools/Update-Lock.ps1 -RepositoryRoot <仓库>，暂存更新后的maintenance.lock.json，最后运行tools/Check-Commit.ps1。只检查一致性不等于构建或游戏实测通过。

新克隆用工具仓tools/Enable-Hooks.ps1 -CheckoutRoot <9仓目录>安装本机检查；Git不自动携带.git/hooks。本机钩子能被绕过，仍需人工审核与回归。不要向源码仓加入ZIP、日志、个人CFG或框架。必要的冻结DLL只允许repo.json登记路径。

## 安装包与提交状态

在干净源码提交上构建测试，用tools/Package-Release.ps1 -RepositoryRoot <仓库> -DestinationRoot <源码仓库之外> -Version <新标签>生成ZIP和包含准确源码提交的清单。

本地尚未提交时，仅可显式用-Preview做验收预览；清单中SourceCommit为空、ReleaseReady=false，BaseCommit只是修改前提交。预览不作为正式发布，不覆盖已发版本。构建输出记录当前输入锁与工具锁。

整合仓默认验证已发布精确提交；-LocalWorkspace验证未提交整理的当前锁、暂存内容及基准HEAD。未来获准提交后，再登记真实新提交并重新下载验收；保留原Releases标签和旧验收记录。
