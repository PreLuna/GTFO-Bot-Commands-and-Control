# GTFO-Bot-Commands-and-Control

机器人与指令。本仓包含以下MOD／共享组件，各自维护文件放在自己的目录。

[定位入口](#modules) · [依赖与联动](#relations) · [开发与排查](#development) · [来源说明](#sources)

<a id="modules"></a>
## 定位入口

| 功能／组件 | 版本 | 修改位置与输入 | 维护方式 | 测试材料 |
| --- | --- | --- | --- | --- |
| AI／机器人协同 `CoopBotsCustom` | 1.0.12 | [CoopBotsCustom/baseline](CoopBotsCustom/baseline) | 冻结DLL＋身份迁移 | 由仓级入口／配方核对 |
| 队长公共核心与中文显示 `Captain` | 0.5.4 | [Captain/src](Captain/src) | 源码＋身份迁移 | [Captain/tests](Captain/tests) |
| 队长指令与任务仲裁 `Captain.Commands` | 0.5.43 | [Captain.Commands/src](Captain.Commands/src) | 源码＋身份迁移 | [Captain.Commands/tests](Captain.Commands/tests) |
| 人机强化剂 `Captain.Boosters` | 0.5.2 | [Captain.Boosters/src](Captain.Boosters/src) | 源码＋身份迁移 | [Captain.Boosters/tests](Captain.Boosters/tests) |
| 队长兼容修复 `Captain.Fixes` | 0.5.1 | [Captain.Fixes/src](Captain.Fixes/src) | 源码＋身份迁移 | [Captain.Fixes/tests](Captain.Fixes/tests) |
| 头戴摄像头 `HeadCamera` | 0.3.13 | [HeadCamera/src](HeadCamera/src) | 源码＋身份迁移 | [HeadCamera/tests](HeadCamera/tests) |
| 微操大师／直接接管 `MicroMaster` | 0.5.14 | [MicroMaster/src](MicroMaster/src) | 源码＋身份迁移 | [MicroMaster/tests](MicroMaster/tests) |

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
| HeadCamera | 无插件硬前置声明 | FlashlightCustom（登记副相机的本地减雾，无编译引用） |
| MicroMaster | HeadCamera `>=0.3.13 <0.4.0`；Captain `0.5.4`；Captain.Commands `>=0.5.42 <0.6.0`；CoopBotsCustom `1.0.12` | FlashlightCustom（全关卡人机原生灯豁免，无编译引用） |

所有插件仍需GTFO及相应BepInEx IL2CPP环境。版本保留加载器原始声明表达式；编译引用、运行硬前置和可选桥分别判断。

当前编译引用：`Captain.Commands` → `Captain`；`Captain.Boosters` → `Captain`；`Captain.Fixes` → `Captain`；`MicroMaster` → `Captain`, `HeadCamera`, `Captain.Commands`。

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

根Test.ps1调度32组源码／替身回归；存在本仓构建清单时，额外调用[实际加载依赖检查](IntegrationTests/Loader/Verify-Dependencies.ps1)，用本机 BepInEx.BepInDependency 与 VersionRange.IsSatisfied 核对生成 DLL 的硬依赖，复现旧版精确版本拒绝并检查兼容范围边界。没有构建清单时会明确跳过这一项；版本范围检查不执行插件 Load、原生补丁或真实游戏。Captain翻译／类型、部分Commands、Boosters与Fixes仍有未接入该入口的测试；文件存在不代表已运行。

### 微操大师接管兼容与拒绝提示

MicroMaster 0.5.14配合已安装HeadCamera 0.3.13，本轮仅替换MicroMaster DLL；已安装Captain.Commands 0.5.43继续使用。摄像头范围 `>=0.3.13 <0.4.0`、指令范围 `>=0.5.42 <0.6.0`；新增展示接口要求这组最低版本，公共核心与摄像头原协议 v1 校验保留。旧 MicroMaster 0.5.0 声明的 `0.3.0` 是实际 BepInEx 的精确范围，会拒绝摄像头 0.3.1 及后续版本，不能仅凭编译成功认定运行兼容。

按 T 打开摄像头，再在配置的双击间隔内双击 T 请求接管；从关闭状态也可双击打开并申请。已打开时单击 T 会等待双击判定后关闭，微操大师缺失时也沿用这套手势，以便双击时提示“接管入口未就绪”。等待／已接管时 T 直接取消或退出。按键仍由 HeadCamera 独占识别，MicroMaster 不另注册一套 T。

拒绝原因显示在摄像头状态栏约5秒，并写入游戏事件栏和日志：包括插件未加载／入口未注册、非房主、没有观看目标或身份变化、插件关闭、本体／关卡未就绪、聊天／菜单及队长服务的控制权拒绝。输入已由聊天或菜单占用时不会把输入文本 T 当作接管请求。接管回调异常只报告接管失败，不触发摄像头拍摄熔断。入口成功注册会单独记录日志；注册失败每10秒至多记录一次重试提示。

0.5.2 删除本体附近敌群检测，不再因敌群数量、雷达标记、检测信息缺失而拒绝申请或强制退出；旧配置中的敌群退出／半径／数量／确认时间不再使用，无需手动修改CFG。0.5.40 的手动接管入口不再使用笼统的 AI 危险预判；其他自动任务及接管内物品交互的既有危险判断保持原样。

手动接管取消已有集火目标及指定击杀；若指定击杀已释放挥击，先等待核验，再取消借调返程，交还旧任务后由接管会话暂停。普通跟随、走动、攻击、换弹、切装备、闪避和扫描动作可在取得租约后停止并核验，再激活接管。救援、资源／工具补给、协同近战、已释放的原版挥击及其他未确认可中断的交互短暂等待收尾；等待期间本体仍可操作，显示具体等待原因，可用T／F8取消，沿用8秒等待上限。超时不强停受保护交互；已经取消的战斗命令不会自动恢复。

即时搜寻只统计标记，不移动机器人，完成即记录结果；没有持续占用或暂停机制，也不参与接管限制。不存在独立“追敌”队长指令，集火／指定击杀与自主战斗分别处理。

保留房主、存活机器人及本体身份、同层同维度、唯一会话、租约、原生停止确认、特殊搬运限制与生命周期清理。本体原有普通动作外额外允许 StandStill；倒地、被抓、爬梯、终端等冲突状态仍不可接管，本体实际受伤仍退出。更好 AI 的扫描预留、受保护动作或其他租约可能暂不交权，会显示等待并有界超时，不修改冻结仲裁DLL或全局自动任务策略。具体入口见[ControlRuntime.Request](MicroMaster/src/ControlRuntime.cs)、[队长接管服务](Captain.Commands/src/BotControlService.cs)。需在游戏中确认“入口已注册 → 等待接管 → 接管成功”及等待提示，不能用离线测试代替。

### 当前人机照明结论与维护入口

MicroMaster 0.5.14／HeadCamera 0.3.13已获用户反馈：本轮除武器切换未测试外，其余功能基本正常，常亮与接管／退出的核心功能通过；Q退出后的过曝此前已确认解决。当前状态是**核心功能实测通过，装备切换后的灯光恢复待补测**，不是全部项目通过。雾倍率、全地图或任意MOD组合不从该反馈推导为专项验收。

默认`[接管手电] 原生手电常亮=true`、`[人机手电] 模式=AllBots`。房主在有效关卡内管理所有已生成且未销毁的人机现有灯，独立于T/Q、AI状态及渲染就绪。0.5.14在Sync／Receive／Set实际写入入口拒绝关灯，不把每帧false改成每帧网络true；装备解绑保留异常安全的本地清理许可，绑定后下一帧恢复。ControlledOnly仅接管期间，AI或关闭总开关交还原版。真人灯不受管理，接管本身的安全条件保留。

[人机接管、照明与过曝修复维护笔记](docs/人机接管与照明维护.md)集中记录：源码与交付的对应关系、已安装DLL核对、常亮原理、原版内联漏拦截和冷却冲突、过曝的全局纹理原因、已删除的旧灯方案、仍保留的兼容／诊断代码、修改定位及后续验证顺序。它是后续开发笔记，模块与依赖登记仍以本README及repo.json为主。

工作区源码已包含上述修复。按最新本机交付约定，日常增量文件和替换清单放工作区根`本地交付/当前`，位于各源码仓之外，不进入Git；用户可直接把本次DLL复制到清单指定的游戏位置。旧外部临时目录的交付、备份和证据归入只读ZIP，恢复映射见本机维护索引。当前两个安装DLL与保留文件哈希一致，216项非Markdown构建输入与0.5.14保留源锁一致；交付整理不重建、替换游戏DLL或增加切换装备指令。

### 接管移动、手电、位置标记与Q菜单

MicroMaster 0.5.3 保留原生步行和碰撞：先把短段目标投影到导航，再沿坡面检测碰撞；长段失败会尝试更短步长，不直接写玩家坐标，不穿墙／跨层。坡面仍必须有连通导航和足够空间，不能保证所有地图小坡均可行。原生步行Update回调中发现障碍时只清路径、阻止该步移动，延后到LateUpdate停止，避免动作在自身更新中被移除。

### 已有过曝修复与显示（0.5.11／0.3.11起）

0.5.10／0.3.10实测Q退出后仍白亮，按T重新打开后恢复；关闭接管额外手电与禁用插件元数据缓存后仍出现。实际新DLL哈希、日志及57份插件内部身份已核对，无相关重复插件，不能继续归咎旧DLL缓存。此前恢复七份灯缓冲仍遗漏原生PreLitVolume.UpdateShaderParameters直接写入的全局纹理：_LitVolume、_FogVolume、_DensityNoise，其中前两者在关窗被释放。0.3.11起在原有FogShaderState作用域中保存／归还这些纹理、间接光／反射体积与_LitBufferCopy，以及对应尺寸和峰值／色调参数；初始化、拍摄finally及释放前既有RenderGlobals.Restore都使用同一快照。不以数值恢复代替纹理引用恢复，不新增补光或曝光参数。

副相机原先固定UpdateShadows=false，不能为只在远端视角可见的原生聚光灯刷新阴影。0.3.11起沿用本体ClusteredRendering.UpdateShadows设置，执行原生场景阴影流程；没有接管专用灯／阴影工厂。0.5.13保留原生初始化前有效参数输入及Sync常亮，不主动调用灯GPU UpdateData／UpdateTransform，不恢复整组旧灯参数；ControlledOnly退出接管时归还开关和RF显隐，AllBots则到关卡／配置等生命周期结束时归还。现有[接管手电]原生手电常亮=true为正常使用，false仅用于AI／隔离对照，F提示开关不能代表像素照明。

实际Player.log记录RGB111110Float到RGBAFloat读取不支持（格式74→52），此前presented统计的统一-431602080为无效读取，不能作为画面证据。诊断读取先以GPU Blit中转到ARGBFloat，再读取同格式RGBAFloat；正常显示格式不因此改变。采样保持开窗约0.5／2秒、每进程最多6组，默认关闭，不增加逐帧读取。CPU替身和生成IL检查不能代替原生／GPU验收。

配套HeadCamera>=0.3.13<0.4.0、MicroMaster0.5.14，Commands仍使用已安装0.5.43。完全退出游戏仅替换MicroMaster DLL并重启，恢复定位时暂关的常亮选项；常规T单击观察、Q菜单／事件栏／标记和驻守交接保持原机制。用户已反馈核心功能通过；装备切换后的灯光恢复尚未实测，后续修改仍需回归Q退出、本体曝光与雾、首次／重复接管和离关清理。

中键仍沿已提交的机器人相机射线记录物品、门、箱柜、敌人或原始场景命中点，并只调用一次原版机器人TriggerMarkerPing。新增只读展示接口使用同一记录与票据，视频按拍摄相机投影，复用原版通用Ping图标模板，模板未就绪时用菱形文字显示。敌人图标跟随原敌人位置；墙面没有可站立点也保留原命中和“此处不可驻守”提示。标记过期、目标死亡、票据或机器人身份失效、退出后清除；画面外不伪造中心标记。没有增加第二次网络Ping或改用本体标点。

HeadCamera保持随屏幕适配、16:9的近全屏接管布局，F7切小窗，Home复位。视频Canvas为45，左下原生事件／聊天镜像为60，原生Q菜单镜像为65。事件栏显示最近4条约7秒，并在原生聊天模式显示现有输入文本；显示层均不接收输入。镜像准备好才隐藏原文字渲染器，关闭／退出／失败／销毁按原值恢复forceRenderingOff。指令成功与拒绝提示进入摄像头状态栏，优先于常规控制提示，不再只靠被视频遮住的原生事件栏。

Q分为“结束接管并原地驻守”和“结束接管并前往标记点驻守”；后者必须有有效地面标记，墙面或过期点明确禁用。提交时核对开菜单后机器人是否移动，不再把距目标0.75米以上误当成快照变化；仍保留同层、相邻区域、导航与碰撞、任务权限等驻守预检。接单成功后才释放接管，MicroMaster识别Released，关闭视频回到本体；失败继续接管并显示原因。普通T退出行为保持原样。

HeadCamera可选接入已启用的FlashlightCustom本地视觉减雾：只把登记且身份匹配的自有副相机加入既有IsLocal白名单，原masterEnabled／ready及倍率规则有效，不改变任务雾和感染。副相机使用独立compute shader；finally调用原减雾作用域收尾并恢复拍摄前的实时雾Shader参数，不再在收尾把本体原始FogSettings重写为未减雾值。接口形状不匹配时记录兼容未启用，仍恢复自身快照。此路径对应“两边雾变浓”的定位；本轮用户反馈其余功能基本正常，但未提供独立雾倍率／全地图对照，不扩展成定量验收。

0.5.42保留0.5.41的冻结更好AI租约时钟兼容：仅起始下界10毫秒浮点余量，15秒上限与身份／来源／票据核验不变；形状不匹配拒绝应用，卸载撤销，不修改冻结或游戏DLL。源码／替身回归、实际BepInEx依赖解析和冻结／安装减雾接口核验属于离线证据，不能代表Unity、原生Hook、光照像素、联机或地图雾气已验收。

### 启动挂起与接管白屏诊断

用户报告启动／加载时无响应，Windows AppHangB1已确认；保留的Player-prev日志没有阻塞线程栈，不能锁定具体MOD。0.3.7／0.5.7／0.5.43在各自Load入口记录版本、进程ID，在配置、类型注册、兼容补丁、运行器和保存阶段记录BEGIN、END与耗时，失败记录FAIL和异常并沿用原失败处理。Commands进一步记录各任务子系统安装，MicroMaster逐个记录接管补丁目标；未改线程、等待策略或绕过依赖。若无三个模块的BEGIN，需继续查更早BepInEx／Unity启动阶段；若出现BEGIN无END，才能缩小模块内的卡点。阶段日志属于定位增强，不声称已修复未知启动挂起。

当前副相机在观察／接管状态改变时记录拍摄BEGIN和END，记录控制状态、原生场景灯／阴影更新、眼点、HDR、渲染路径与输出模式，另用原生手电诊断记录源灯与渲染就绪；CameraRuntime也记录进入／离开接管首次提交。开启既有诊断采样后，同一窗口从观察进入接管会重新安排约0.5／2秒两次采样，仍受每进程6组共享限额；不增加默认GPU读回。capture.txt附控制／光源状态，统计近白像素，输出超过95%近白时提示对照原始与输出图片。Unity GPU及真实照明效果仍需游戏确认，替身和Cecil检查不能代替像素验收。

### HeadCamera 黑屏诊断与对照

0.3.1 在原版 clustered Setup 后补设副相机 Depth/MotionVectors，每次提交该相机的投影／深度参数，并恢复原先的全局向量。正常输出使用独立的原版色调映射材质副本与无 Alpha 的 RGB111110Float 贴图，避免场景透明度隐藏视频；不复制完整 FPS 控制器或增加景深／运动模糊／TAA。新增一个全分辨率 GPU 输出步骤，不代表性能或游戏画面已经验收。

退出游戏后编辑 `BepInEx/config/PreLuna.HeadCamera.cfg`，启动游戏后按 T；每个场景保持同一房间、队友与朝向，只更改一项设置后重新启动。默认 `[诊断]` 的 `修复深度缓冲 = true`、`补齐投影参数 = true`；false 仅供分别与旧路径对照。`[画面]` 的 `显示模式` 可取：

| 值 | 用途 |
| --- | --- |
| `ToneMapped`（默认） | 正常色调处理、不透明输出。 |
| `RawOpaque` | 原始 RGB、不透明输出；排除原始 Alpha，保留与色调处理的对照。 |
| `OriginalAlpha` | 原始 RGB 和 Alpha 直接进入 UI，作为旧显示路径对照。 |
| `TestPattern` | 同一个窗口显示不透明彩条与灰阶，完全跳过副相机初始化／渲染；即使场景渲染器有故障，也能单独检查 UI。该模式不提供接管瞄准画面，也不保存场景样本。 |

将 `[诊断]` 的 `保存画面样本 = true` 后，正常开窗约 0.5 秒、2 秒各尝试一次，整个游戏进程最多 6 组；输出阶段失败时会提前尝试保留原始画面，仍计入限额。采样会同步读取 GPU，可能短暂停顿。默认关闭，无逐帧读回。输出位于 `BepInEx/HeadCamera-Diagnostics/<UTC时间>/`：

- `raw.exr` 保留原始浮点 RGB/Alpha；`raw-rgb.png` 忽略 Alpha 的普通预览；`raw-alpha.png` 单独显示 Alpha；`raw-plus4EV.png` 是固定加四档曝光的暗部对照，不用于自动提亮正常画面。
- `presented-rgb.png` 是进入 UI 前的输出纹理 RGB 预览，并非合成屏幕截图。`capture.txt` 记录相机、格式、修复开关、RGB/Alpha 范围、无效与非黑像素数；非黑像素不代表场景正确。
- 原始 RGB 有场景而窗口无画面：检查 Alpha／输出／UI；只有加曝光版本有细节：检查光照与曝光；原始画面仍黑：检查眼点、头部遮挡、裁剪和原生场景命令。应同时提供窗口截图，不能仅凭提交帧率判定成功。

验收需在游戏内确认队友面前的场景清晰、切换队友视角确实变化、主画面正常、关闭后释放资源；离线测试只能验证诊断限额／像素分析与已有接口行为。若 GPU 不支持 RGB111110Float 或原版色调映射材质不可用，将明确记录错误；可分别使用 OriginalAlpha／RawOpaque 排查，不会静默降为另一种显示路径。

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
