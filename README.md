# GTFO-Bot-Commands-and-Control

机器人与指令。各MOD的维护文件直接放在自己的目录内。

| MOD／目录 | 版本 | 维护方式 | 必要前置／声明版本 | 可选联动／软依赖 |
| --- | --- | --- | --- | --- |
| [AI／机器人协同](CoopBotsCustom/) | 1.0.12 | 冻结DLL＋身份迁移 | 无插件硬前置声明 | UnityExplorer、ChatterReborn |
| [队长公共核心与中文显示](Captain/) | 0.5.4 | 源码＋身份迁移 | 无插件硬前置声明 | ChatterReborn、AI／机器人协同、SmartPing |
| [队长指令与任务仲裁](Captain.Commands/) | 0.5.39 | 源码＋身份迁移 | 队长公共核心与中文显示 `0.5.4` | ChatterReborn、AI／机器人协同 |
| [人机强化剂](Captain.Boosters/) | 0.5.2 | 源码＋身份迁移 | 队长公共核心与中文显示 `>=0.5.1 <0.6.0` | TheArchive |
| [队长兼容修复](Captain.Fixes/) | 0.5.1 | 源码＋身份迁移 | 队长公共核心与中文显示 `>=0.5.1 <0.6.0` | SmartPing |
| [头戴摄像头](HeadCamera/) | 0.3.0 | 源码＋身份迁移 | 无插件硬前置声明 | 无插件软依赖声明 |
| [微操大师／直接接管](MicroMaster/) | 0.5.0 | 源码＋身份迁移 | 头戴摄像头 `0.3.0`；队长公共核心与中文显示 `0.5.4`；队长指令与任务仲裁 `0.5.39`；AI／机器人协同 `1.0.12` | 无插件软依赖声明 |

所有插件均需GTFO与相应BepInEx IL2CPP环境。无插件硬依赖声明不表示可脱离游戏运行；软依赖也可能对应某项功能的启用条件。版本列保留加载器声明表达式。

## MOD之间的关系

Captain 提供队长公共接口与中文显示，Commands、Boosters、Fixes分别扩展指令、强化剂与兼容修复。MicroMaster直接使用Captain、Commands、HeadCamera与CoopBotsCustom，因此它们接口变化时共同回归。Captain核心对AI、ChatterReborn、SmartPing是软依赖；CoopBotsCustom对UnityExplorer、ChatterReborn也是软依赖，不能把同仓收纳理解为全部必装。HeadCamera与第三人称仓通过可选视角身份接口联动。

## 下载与安装

普通玩家从 [Releases](https://github.com/PreLuna/GTFO-Bot-Commands-and-Control/releases) 下载安装ZIP，退出游戏后将BepInEx合并到已配置前置的游戏目录，替换同功能旧版以免重复加载。个人配置不包含在安装包中。Source code ZIP用于开发。已发布标签固定对应原提交；本地目录整理不覆盖旧安装包。

构建、测试及修改入口见 [DEVELOPMENT.md](DEVELOPMENT.md)，来源见 [NOTICE.md](NOTICE.md)，机器可读路径和依赖见 [repo.json](repo.json)。
