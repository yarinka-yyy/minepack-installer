# MinePack 安装程序

[English](README.md) · [Русский](README.ru.md) · 简体中文

MinePack 为 **Minecraft: Java Edition 26.2** 安装开箱即玩的整合包。模组、资源包和光影包已经选好并配置完成，无须逐个下载和手动安装。

支持 **Windows x64**、**官方 Minecraft Launcher** 和 **Prism Launcher**。安装需要联网，游玩需要拥有 Minecraft: Java Edition 使用权的账号。无须单独安装 .NET。

**[下载 MinePack](https://github.com/yarinka-yyy/minepack-installer/releases/tag/v1.6.0)**

## 选择哪个整合包？

### Vanilla Plus

适合探索世界和日常游玩的主要整合包，包含 **62 个模组、13 个资源包和一个光影包**。

- **更多值得探索的地方：**改进的村庄、遗迹、高塔、掠夺者营地和新的下界建筑。神庙、矿井、地牢和要塞也更加丰富多样。
- **更丰富的画面：**Complementary Reimagined 带来光照、水面和云层效果，还有更新的纹理、立体细节和更加生动的生物动画。
- **远景与性能优化：**Voxy 可以显示普通视距之外已经探索过的地形，性能模组则帮助游戏运行得更流畅。
- **更方便的操作：**世界地图、物品栏整理、物品提示、详细统计、每个世界的游玩时长，以及更方便的伐木功能。
- **更有氛围的世界：**空间音效、雨水与粒子效果、村庄守卫，以及能够攀爬墙壁和天花板的蜘蛛。

新建筑只会生成在尚未探索的区域。创建新世界或前往以前没有去过的地方，即可看到这些变化。

### Frontier

**Vanilla Plus 的全部内容，加上五个 Macaw's 建筑模组：**新的窗户、栅栏与围墙、桥梁、门和楼梯样式。想为自己的建筑添加更多细节，可以选择 Frontier。

总计 **67 个模组、13 个资源包和一个光影包**。两个整合包的其他内容完全相同。

## 如何安装

1. 下载 [MinePack-Installer-1.6.0-win-x64.zip](https://github.com/yarinka-yyy/minepack-installer/releases/download/v1.6.0/MinePack-Installer-1.6.0-win-x64.zip)，并将**整个压缩包解压**到一个文件夹中。请将安装包内的所有文件保留在一起。
2. 运行 **MinePack.Installer.exe**，选择 **Vanilla Plus** 或 **Frontier**，然后点击**安装整合包**。
3. 如果只找到一个受支持的启动器，MinePack 会自动选择它。如果两个启动器都找到了，请选择要使用的启动器。等待出现**整合包已安装**。
4. 打开所选启动器，选择 **MinePack for 26.2**，然后启动游戏。Prism 中会创建独立实例，官方启动器中会创建独立配置。

如果没有找到 Prism，请点击**选择 Prism Launcher**，选择其程序或数据文件夹。**重新搜索启动器**会再次执行自动检测。

安装程序可能会在安装过程中关闭官方启动器，完成后再将其打开。首次启动时，启动器可能还需要下载额外的 Minecraft 文件。

应用程序尚未进行数字签名，因此 Windows 可能显示 SmartScreen 警告。请从上方链接的 GitHub Release 页面下载。

## 其他操作

- **检查并修复** — 恢复缺失或损坏的整合包文件。
- **导入存档** — 将世界复制到当前整合包，同时保留原文件。
- **恢复启动器配置** — 如果启动器中的配置或实例消失，或安装结束时没有完成配置，则重新进行设置。
- **卸载整合包** — 打开已安装整合包和残留数据的列表。选择一项后，应用程序会在确认前显示要移除的内容。

## 世界与设置

整合包安装在独立文件夹中。默认位置是 `%LOCALAPPDATA%\MinePack`；也可以在**安装位置**中选择其他位置。普通 Minecraft 安装和其他启动器配置不会受到影响。

修复和正常卸载整合包会保留世界、截图和其他个人文件。移除残留数据是需要单独确认的操作。世界不会自动在整合包之间转移；请使用**导入存档**。切换整合包或 Minecraft 版本前，请先备份世界。

新安装默认关闭 VSync。重新安装和修复会保留已有的游戏设置。可以在游戏中调整或关闭光影；流畅程度取决于电脑和所选设置。

在外部多人服务器上游玩时，添加建筑结构、生物或建筑方块的模组也需要安装在服务器上。

## 作者与许可证

MinePack 的原创代码和文档使用 [0BSD 许可证](LICENSE)。模组、资源包、光影包和字体保留各自作者的许可证。

模组和资源包的链接位于安装程序的**整合包内容**中。Frontier 的附加模组由 Macaw 制作：[窗户](https://modrinth.com/mod/macaws-windows)、[栅栏与围墙](https://modrinth.com/mod/macaws-fences-and-walls)、[桥梁](https://modrinth.com/mod/macaws-bridges)、[门](https://modrinth.com/mod/macaws-doors)和[楼梯](https://modrinth.com/mod/macaws-stairs)。本地适配版本及其源代码信息见[第三方文档](third-party/README.md)。

[Nyf's Spiders](https://modrinth.com/mod/nyfs-spiders) 适配版本获作者 Nyfaria 许可用于此仓库。在其他公开仓库或商业整合包中发布该适配版本，需要另行获得作者许可。
