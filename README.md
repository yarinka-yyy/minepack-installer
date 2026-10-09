# MinePack Installer

English · [Русский](README.ru.md) · [简体中文](README.zh-CN.md)

MinePack installs a ready-to-play pack for **Minecraft: Java Edition 26.2**, with mods, resource packs, and a shader already selected and configured. You do not need to download and set up each one yourself.

Works on **Windows x64** with the **official Minecraft Launcher** and **Prism Launcher**. Installation needs an internet connection; playing needs an account with access to Minecraft: Java Edition. No separate .NET installation is required.

**[Download MinePack](https://github.com/yarinka-yyy/minepack-installer/releases/tag/v1.7.0)**

Installer `1.7.0` includes the three additional optimization mods and updated defaults described below. It also improves interrupted-operation recovery and repeated-action handling: Repair can enter pending-operation recovery, and launcher selection no longer leaves a previous request waiting. This release contains Vanilla Plus `0.18.4` and Frontier `0.19.10`.

## Which pack should you choose?

### Vanilla Plus

The main pack for exploring the world and everyday play. Version `0.18.4` includes **65 mods, 13 resource packs, and one shader**.

- **More places to discover:** improved villages, ruins, towers, pillager camps, and new Nether structures. Temples, mineshafts, dungeons, strongholds, and fortresses become more varied.
- **Richer visuals:** lighting, water, and clouds with Complementary Reimagined, refreshed textures, 3D details, and more expressive creature animations.
- **Distant views and optimization:** Voxy displays explored terrain beyond the normal render distance. [BadOptimizations](https://modrinth.com/mod/badoptimizations), [More Culling](https://modrinth.com/mod/moreculling), and [Lithium](https://modrinth.com/mod/lithium) add targeted client and game-logic optimizations.
- **Convenience:** a world map, inventory sorting, item hints, detailed statistics, play time for each world, and easier tree harvesting.
- **More atmosphere:** richer spatial sound, rain and particle effects, village guards, and spiders that climb walls and ceilings.

New structures appear in unexplored areas. Create a new world or explore somewhere you have not visited before to see the changes.

### Frontier

**Everything in Vanilla Plus plus five Macaw's building mods:** new windows, fences and walls, bridges, doors, and stair designs. Choose Frontier if you want more details for your builds.

Frontier `0.19.10` has **70 mods, 13 resource packs, and one shader**. All other content is shared with Vanilla Plus.

## How to install

1. Download [MinePack-Installer-1.7.0-win-x64.zip](https://github.com/yarinka-yyy/minepack-installer/releases/download/v1.7.0/MinePack-Installer-1.7.0-win-x64.zip) and **extract the entire archive** into a folder. Keep all package files together.
2. Run **MinePack.Installer.exe**, choose **Vanilla Plus** or **Frontier**, and click **Install pack**.
3. If one supported launcher is found, MinePack selects it automatically. If both are found, choose the one you want. Wait for **Pack installed**.
4. Open the selected launcher, choose **MinePack for 26.2**, and start the game. Prism gets a separate instance; the official launcher gets a separate profile.

If Prism is not found, click **Browse for Prism Launcher** and select its executable or data folder. **Search for launchers again** repeats automatic detection.

The installer may close the official launcher during installation and reopen it afterwards. On first launch, the launcher may download additional Minecraft files.

The application is not digitally signed yet, so Windows may show a SmartScreen warning. Download it from the GitHub Release page linked above.

## Additional actions

- **Check and repair** — restores missing or damaged pack files.
- **Import worlds** — copies your worlds into the current pack while keeping the originals.
- **Restore Launcher profile** — sets up the launcher profile or instance again if it disappeared or installation finished without it.
- **Uninstall pack** — opens a list of installed packs and leftover data. Select an entry; the app shows what will be removed before you confirm.

## Worlds and settings

Packs are installed in separate folders. The default location is `%LOCALAPPDATA%\MinePack`; you can choose another location under **Install location**. Your regular Minecraft installation and unrelated launcher profiles are left alone.

Repair and normal pack removal keep worlds, screenshots, and other personal files. Removing leftover data is a separate action with confirmation. Worlds are not moved between packs automatically; use **Import worlds** for that. Back up a world before switching packs or Minecraft versions.

Installer `1.7.0` creates new Vanilla Plus `0.18.4` and Frontier `0.19.10` instances with the Custom graphics preset, 9-chunk render distance, 200% entity distance, and an 8 GiB maximum Java heap. It leaves simulation distance at Minecraft's default and disables VSync. Repair and reinstall keep existing game settings, launcher memory values, and JVM arguments. You can adjust or disable shaders in the game; performance depends on your computer and chosen settings.

For an external multiplayer server, mods that add structures, creatures, or building blocks also need to be installed on the server.

## Credits and licenses

MinePack's original code and documentation use the [0BSD license](LICENSE). Mods, resource packs, the shader, and the font retain their authors' licenses.

Links to mods and resource packs are available in the installer's **Included in the pack** section. Frontier's additions are made by Macaw: [windows](https://modrinth.com/mod/macaws-windows), [fences and walls](https://modrinth.com/mod/macaws-fences-and-walls), [bridges](https://modrinth.com/mod/macaws-bridges), [doors](https://modrinth.com/mod/macaws-doors), and [stairs](https://modrinth.com/mod/macaws-stairs). Details about local adaptations and their sources are in the [third-party documentation](third-party/README.md).

The [Nyf's Spiders](https://modrinth.com/mod/nyfs-spiders) adaptation is included with author Nyfaria's permission for this repository. Publishing the adaptation in another public repository or commercial modpack requires separate permission from the author.
