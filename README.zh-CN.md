# MinePack 安装程序

[English](README.md) · [Русский](README.ru.md) · 简体中文

MinePack 帮你为**正版 Minecraft: Java Edition**安装精选模组，无须逐个手动配置。它会下载并校验所选文件，把游戏内容放在独立的 MinePack 文件夹中。

MinePack 适用于 Windows，安装的是 Minecraft: Java Edition 26.2 整合包。游玩需要官方 Minecraft Launcher，以及拥有 Java Edition 使用权的账号。

## 选择整合包

**Vanilla Plus** 包含 37 个模组、8 个资源包和 1 个光影包。生物和村民的动作更加生动；光影和视觉内容改变了光照、水面、植物与粒子的效果。Voxy 能让你在通常的视距之外看到已经探索过的地形，性能模组则帮助游戏保持流畅。世界地图、物品栏整理、物品提示和更有层次的声音让日常游玩更方便。

**Frontier** 包含 Vanilla Plus 的全部内容，总计 61 个模组、10 个资源包和 1 个光影包。五个建筑模组带来窗户、栅栏与围墙、桥梁、门和更多楼梯样式。在尚未探索的区域，你还能发现改进的村庄、遗迹、高塔、下界建筑、掠夺者营地，以及由 YUNG's 模组重新设计的地牢、神庙、矿井、要塞和下界堡垒；仅 MVS 就宣称拥有[超过 130 种建筑结构](https://modrinth.com/mod/moogs-voyager-structures)。村庄里会出现持剑或弩的守卫。蜘蛛可以攀爬墙壁和天花板，世界选择界面也会显示每个世界的游玩时长。

## 安装

下方链接指向尚未包含这些 Frontier 新内容的 1.0.0 版本。1.0.3 ZIP 已在本地准备，但尚未发布为 GitHub Release。

1. 从首个版本页面下载 [MinePack-Installer-1.0.0-win-x64.zip](https://github.com/yarinka-yyy/minepack-installer/releases/download/v1.0.0/MinePack-Installer-1.0.0-win-x64.zip)，并将整个 ZIP 解压到一个文件夹中。请将解压后的文件放在一起。
2. 运行 `MinePack.Installer.exe`，选择 **Vanilla Plus** 或 **Frontier**，然后点击**安装整合包**。MinePack 会准备所选整合包并更新它在 Launcher 中的配置。安装时，它可能会关闭正在运行的官方 Launcher；完成后会尝试重新打开。
3. 在官方 Minecraft Launcher 中选择 **MinePack** 配置并点击**开始游戏**。首次启动时，Launcher 可能还会下载 Minecraft 的基础文件。如果 Launcher 没有自动打开，请手动打开并选择 MinePack。

EXE 目前尚未进行数字签名，因此 Windows 可能显示 SmartScreen 警告。请只从上述版本页面下载 ZIP。

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
<summary>资源包 (8)</summary>

- [Fresh Animations](https://modrinth.com/resourcepack/50dA9Sha)
- [Fresh Animations: Extensions](https://modrinth.com/resourcepack/YAVTU8mK)
- [Low On Fire](https://modrinth.com/resourcepack/RRxvWKNC)
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
