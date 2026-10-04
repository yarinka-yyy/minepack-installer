# MinePack 安装程序

[English](README.md) · [Русский](README.ru.md) · 简体中文

MinePack 帮你为 **Minecraft: Java Edition**安装精选模组，无须逐个手动配置。它会下载并校验所选文件，把游戏内容放在独立的 MinePack 文件夹中。

MinePack 适用于 Windows，安装的是 Minecraft: Java Edition 26.2 整合包。游玩需要受支持的启动器，以及拥有 Java Edition 使用权的账号。已发布的安装程序 1.5.0 支持官方 Minecraft Launcher；本地版本 1.6.2 支持官方 Minecraft Launcher 和 Prism Launcher。

## 选择整合包

**Vanilla Plus** 包含 62 个模组、13 个资源包和 1 个光影包。生物和村民的动作更加生动；光影和视觉内容改变了光照、水面、植物与粒子的效果。Voxy 能让你在通常的视距之外看到已经探索过的地形，性能模组则帮助游戏保持流畅。世界地图、物品栏整理、物品提示、重新设计的统计界面和更有层次的声音让日常游玩更方便。新增世界生成内容包括改进的村庄、遗迹、高塔、下界建筑、掠夺者营地，以及 YUNG's 重新设计的地牢、神庙、矿井和要塞。村庄也会出现守卫。新建筑只会生成在尚未探索的地形；想体验完整变化，请创建新世界或探索新的区块。

**Frontier** 包含完整的 Vanilla Plus 基础内容，总计 67 个模组、13 个资源包和 1 个光影包。它只额外增加五个 Macaw 建筑模组：窗户、栅栏与围墙、桥梁、门和更多楼梯样式。两种整合包共享世界生成内容，这些建筑只会出现在未探索区域；仅 MVS 就宣称拥有[超过 130 种建筑结构](https://modrinth.com/mod/moogs-voyager-structures)。蜘蛛可以攀爬墙壁和天花板，世界选择界面也会显示游玩时长。MinePack 非官方移植的 xali's Enhanced Vanilla 默认启用，并配有 Continuity 和 CIT Resewn Continuation；ZIP 内注明作者 xalixilax 和 CC BY-NC 4.0 许可。Tree Harvester 及其依赖 Collective 已锁定版本；Remodeled Doors 3D 和内置的门模型兼容资源包按高于 xali's 的优先级启用。

公开版 MinePack Installer 1.5.0 包含 Vanilla Plus 0.18.1 和 Frontier 0.19.6。本地 Installer 1.6.2 候选版本包含 Vanilla Plus 0.18.2 和 Frontier 0.19.8；旧版 0.18.1 与 0.19.7 档案仍为现有实例保留。Low On Fire 已从两种整合包中移除。

新的 Vanilla Plus 基础版本加入 Better Statistics Screen、TCDCommons 和固定版本的 3D Default。Frontier 0.19.8 使用相同基础内容，并额外添加五个 Macaw 建筑模组。3D Default 的优先级低于 xali's、Remodeled Doors 和兼容资源包。新的 `.2` 兼容 ZIP 保留 `.1` 的全部条目，并为 Minecraft 26.2 添加三个最小模型父项（`door_bottom`、`door_top`、`door_bottom_rh`）；可通过 `tools/Build-DoorCompatibility.ps1` 重现，SHA-256 为 `791B1A98A8B973DE01EC9D1EBC10CBBB1D1D1D94782D3AE8CEC9F23F5227E0D7`。使用精确客户端和资源格式 88.0 的检查已通过：18 个门/活板门 blockstate 及全部 54 个受影响的 Remodeled Doors 模型（52 个方块、2 个物品）均可解析，3D Default 仍保持较低优先级。0.18.2 和 0.19.8 档案 SHA-512 分别为 `9B756275D14380848C06183CE8E693C8519ACDBD1EC13EBB3D65E1154F4E57E9EB3D03321A535FD1C5230797C3D093F925C1C24DE5B0E95D7B0B92722E4E3061` 和 `EEAB43179B7EE9B6C4A0BC1D20F2AC7E7C09F9CBDD632493D03A015B30543CECED00B96ED474B1F3420DBAA4E970FB0046BF76EC9CF201C21F514E59D59200AB`。两种整合包的离线 Smoke 及严格联网安装、修复和卸载检查均已通过；实际游戏内渲染与游玩验收仍待完成。

## 离线文档与源代码

本地 1.6.2 安装包包含安装程序的 [0BSD 许可证](LICENSE)、[Vanilla Plus 历史档案](releases/test-pack/README.md)、[Frontier 版本说明](releases/vanilla-2-plus/README.md)以及[第三方源代码和构建说明](third-party/README.md)。对应的 YUNG's 源代码位于 `third-party/yungs-sources/`，其中包含七个分支的八个版本化快照、精确的二进制映射、构建文件和许可证。1.6.2 基于 `main` 构建，只支持 Vanilla Plus 和 Frontier；不再支持实验性的 Test 整合包。1.6.2 没有 GitHub Release。

## 安装

**发行状态：**下方的已发布 Installer 1.5.0 ZIP 支持官方 Minecraft Launcher。本地 Installer 1.6.2 支持官方 Launcher 和 Prism Launcher；没有为 1.6.2 创建 GitHub Release。

1. 下载 [MinePack-Installer-1.5.0-win-x64.zip](https://github.com/yarinka-yyy/minepack-installer/releases/download/v1.5.0/MinePack-Installer-1.5.0-win-x64.zip)，并将整个 ZIP 解压到一个文件夹中。请将解压后的文件放在一起。
2. 运行 `MinePack.Installer.exe`，选择 **Vanilla Plus** 或 **Frontier**，然后点击**安装整合包**。MinePack 会准备所选整合包并更新它在 Launcher 中的配置。安装时，它可能会关闭正在运行的官方 Launcher；完成后会尝试重新打开。
3. 在官方 Minecraft Launcher 中选择 **MinePack** 配置并点击**开始游戏**。首次启动时，Launcher 可能还会下载 Minecraft 的基础文件。如果 Launcher 没有自动打开，请手动打开并选择 MinePack。

EXE 目前尚未进行数字签名，因此 Windows 可能显示 SmartScreen 警告。请只从上述版本页面下载 ZIP。

安装位置选项位于**安装整合包**按钮正下方。本地版本 1.6.2 会自动选择唯一检测到的 Launcher。如果同时安装了官方 Minecraft Launcher 和 Prism Launcher，安装程序会询问使用哪一个。确认 Launcher 和安装目录后才会显示进度；点击面板外部或按 Escape 只会隐藏面板，不会取消操作，点击**显示进度**可重新打开。完整安装成功后会显示**整合包已安装**及启动说明；完成结果也可以点击面板外部或按 Escape 关闭。Prism 会创建独立的 **MinePack for 26.2** instance；官方 Launcher 配置也使用相同名称，所有界面语言均保留此拼写。

新 instance 默认关闭 VSync；Repair 和重新安装会保留已有的 `options.txt`。如果 Prism 是便携版或尚未注册，请使用**选择 Prism Launcher**选择 EXE 或数据目录，或点击**重新搜索启动器**。安装程序不会遍历磁盘。

## 游戏文件夹与世界

MinePack 默认将文件保存在 `%LOCALAPPDATA%\MinePack`；你也可以在安装程序中选择其他文件夹。每个整合包都有独立的游戏文件夹。切换整合包时，之前的文件夹及其中的世界会保留。MinePack 不会自动在整合包之间复制世界。

检查修复和卸载整合包都会保留你的世界及其他个人文件。卸载只会移除 MinePack 管理的文件及其 Launcher 配置。**导入世界**会将世界复制到当前整合包，原文件保持不变。使用其他 Minecraft 版本打开旧世界前，请先备份。普通的 `.minecraft` 文件夹及其他 Launcher 配置不会受到影响。

## 模组与文件清单

<details>
<summary>性能与远景 (8)</summary>

- [Sodium](https://modrinth.com/mod/AANobbMI)
- [Voxy](https://modrinth.com/mod/fxxUqruK)
- [C2ME](https://modrinth.com/mod/VSNURh3q)
- [ImmediatelyFast](https://modrinth.com/mod/5ZwdcRci)
- [FerriteCore](https://modrinth.com/mod/uXXizFIs)
- [Entity Culling](https://modrinth.com/mod/NNAgCjsB)
- [Better Block Entities](https://modrinth.com/mod/ONZm0H7Y)
- [Clumps](https://modrinth.com/mod/Wnxd13zP)

</details>
<details>
<summary>画面与动画 (9)</summary>

- [Iris](https://modrinth.com/mod/YL57xq9U)
- [EMF](https://modrinth.com/mod/4I1XuqiY)
- [ETF](https://modrinth.com/mod/BVzZfTc1)
- [Punchy!](https://modrinth.com/mod/8aoMKplv)
- [Explosive Enhancement](https://modrinth.com/mod/OSQ8mw2r)
- [Dense Flowers](https://modrinth.com/mod/Ud3A1Fat)
- [Inventory Particles](https://modrinth.com/mod/XYnKrsxH)
- [Advancement Plaques](https://modrinth.com/mod/9NM0dXub)
- [Subtle Effects](https://modrinth.com/mod/4q8UOK1d) — 可调整的粒子与视觉效果

</details>

<details>
<summary>实用工具 (9)</summary>

- [Chunky](https://modrinth.com/mod/fALzjamp)
- [Inventory Sorting](https://modrinth.com/mod/5ibSyLAz)
- [Held Item Info](https://modrinth.com/mod/tEcWzCZz)
- [Pick Up Notifier](https://modrinth.com/mod/ZX66K16c)
- [Xaero's World Map](https://modrinth.com/mod/NcUtCpym)
- [Cherished Worlds](https://modrinth.com/mod/3azQ6p0W)
- [Leaf Me Alone](https://modrinth.com/mod/ppMUvsIg)
- [InvMove](https://modrinth.com/mod/REfW2AEX)
- [Mod Menu](https://modrinth.com/mod/mOgUt4GM)

</details>

<details>
<summary>音效 (2)</summary>

- [Cool Rain](https://modrinth.com/mod/iDyqnQLT)
- [Sound Physics Remastered](https://modrinth.com/mod/qyVF9oeo)

</details>

<details>
<summary>技术组件 (9)</summary>

- [Fabric API](https://modrinth.com/mod/P7dR8mSH)
- [Cloth Config API](https://modrinth.com/mod/9s6osm5g)
- [Forge Config API Port](https://modrinth.com/mod/ohNO6lps)
- [MossyLib](https://modrinth.com/mod/ffLDUGbm)
- [Puzzles Lib](https://modrinth.com/mod/QAGBst4M)
- [Iceberg](https://modrinth.com/mod/5faXoLqX)
- [Text Placeholder API](https://modrinth.com/mod/eXts2L7r)
- [Fzzy Config](https://modrinth.com/mod/hYykXjDp)
- [Fabric Language Kotlin](https://modrinth.com/mod/Ha28R6CL)

</details>

<details>
<summary>资源包 (7)</summary>

- [Fresh Animations](https://modrinth.com/resourcepack/50dA9Sha)
- [Fresh Animations: Extensions](https://modrinth.com/resourcepack/YAVTU8mK)
- [Fancy Crops](https://modrinth.com/resourcepack/UGEVQ6t9)
- [Better Flame Particles](https://modrinth.com/resourcepack/ivUZsvzp)
- [Os' Colorful Grasses](https://modrinth.com/resourcepack/O2zhH8n8)
- [GUI Retextures — Dark](https://modrinth.com/resourcepack/ZM5PH9W6)
- [Better Click Sounds](https://modrinth.com/resourcepack/XWQ6jMjk)

</details>

<details>
<summary>光影包 (1)</summary>

- [Complementary Reimagined](https://modrinth.com/shader/HVnmMxH1)

</details>

<details>
<summary>Frontier 附加内容：建筑模组 (5)</summary>

这些模组会加入上述 Vanilla Plus 的全部内容。

- [Macaw's Windows](https://modrinth.com/mod/C7I0BCni)
- [Macaw's Fences and Walls](https://modrinth.com/mod/GmwLse2I)
- [Macaw's Bridges](https://modrinth.com/mod/GURcjz8O)
- [Macaw's Doors](https://modrinth.com/mod/kNxa8z3e)
- [Macaw's Stairs](https://modrinth.com/mod/iP3wH1ha)

在外部多人服务器上游玩时，服务器也需要安装这些方块模组。单人游戏使用内置服务器。

</details>

<details>
<summary>Frontier 附加内容：世界与建筑结构 (11)</summary>

这些内容会加入上述 Vanilla Plus 和五个 Macaw's 模组。

- [Better Villages](https://modrinth.com/mod/dGVX5JbJ)
- [MNS — Moog's Nether Structures](https://modrinth.com/mod/nGUXvjTa)
- [MVS — Moog's Voyager Structures](https://modrinth.com/mod/OQAgZMH1)
- [Structory](https://modrinth.com/datapack/aKCwCJlY)（Fabric 模组版本）
- [It Takes a Pillage Continuation](https://modrinth.com/mod/QOJOg1gE)
- [YUNG's Better Desert Temples](https://github.com/YUNG-GANG/YUNGs-Better-Desert-Temples)
- [YUNG's Better Dungeons](https://github.com/YUNG-GANG/YUNGs-Better-Dungeons)
- [YUNG's Better Jungle Temples](https://github.com/YUNG-GANG/YUNGs-Better-Jungle-Temples)
- [YUNG's Better Mineshafts](https://github.com/YUNG-GANG/YUNGs-Better-Mineshafts)
- [YUNG's Better Nether Fortresses](https://github.com/YUNG-GANG/YUNGs-Better-Fortresses)
- [YUNG's Better Strongholds](https://github.com/YUNG-GANG/YUNGs-Better-Strongholds)

整合包还加入 [Voxy WorldGen](https://modrinth.com/mod/xT0lnNE9)，以及必需的 [Library Ferret](https://modrinth.com/mod/DOB2l4oJ)、[Moog's Structure Lib](https://modrinth.com/mod/1oUDhxuy)、[Resourceful Lib](https://modrinth.com/mod/G1hIVOrD) 和 [YUNG's API](https://github.com/YUNG-GANG/YUNGs-API)。七个 YUNG's 文件作为适配 Minecraft 26.2 的分支版本随整合包提供。新建筑结构只会出现在尚未探索的区块中。外部服务器也需要安装这些世界生成模组及其依赖；单人游戏使用内置服务器。

</details>

<details>
<summary>Frontier 附加内容：村民 (1)</summary>

- [Guard Villagers (Fabric/Quilt)](https://modrinth.com/mod/59rkB3YY) — 村庄守卫

两个整合包都包含 Subtle Effects，以及 Fzzy Config 和 Fabric Language Kotlin。在多人服务器上使用守卫功能时，服务器也需要安装 Guard Villagers。

Frontier 还加入 [F.M.R.P](https://modrinth.com/resourcepack/freshly-modded) 提供守卫模型，以及 [Semos Animations Lib](https://modrinth.com/resourcepack/semos-animations-lib) 提供更细致的动作。这两个资源包只在玩家客户端使用；实际外观仍需要在游戏中检查。

</details>

<details>
<summary>Frontier 附加内容：蜘蛛与游玩时长 (2)</summary>

- [Nyf's Spiders](https://modrinth.com/mod/nyfs-spiders)，原作者 Nyfaria — 适配 Minecraft 26.2 的分支版本，让蜘蛛能够攀爬墙壁和天花板。
- [World Play Time](https://modrinth.com/mod/world-play-time) — 适配 Minecraft 26.2 的分支版本，在世界选择界面显示每个世界的游玩时长。

这两个 JAR 都随 Frontier 提供。World Play Time 只在客户端运行；如需在多人服务器上改变蜘蛛行为，服务器也需要安装 Nyf's Spiders。

</details>

## 许可证

MinePack 的原创源代码和文档使用 [0BSD](LICENSE) 许可证：你可以使用、修改和分发它们，无须注明作者。模组、资源包、光影包、字体和 Minecraft 属于各自的权利人；0BSD 不会改变它们的使用条款。

随包提供的 Nyf's Spiders 分支版本仅获 Nyfaria 许可用于此仓库。若要在其他公开仓库或商业整合包中发布该分支版本，需另行取得 Nyfaria 的许可。
