# MinePack Installer

English · [Русский](README.ru.md) · [简体中文](README.zh-CN.md)

MinePack helps you install a curated collection of mods for **official Minecraft: Java Edition** without setting up each mod by hand. It downloads the selected files, checks them, and puts the game in a separate MinePack folder.

MinePack works on Windows and installs packs for Minecraft: Java Edition 26.2. To play, you need the official Minecraft Launcher and an account with access to Java Edition.

## Choose a pack

**Vanilla Plus** includes 37 mods, 7 resource packs, and 1 shader. Mobs and villagers move more expressively, while the shader and visual additions change lighting, water, plants, and particles. Voxy displays terrain you have already explored far beyond the usual render distance; performance mods help keep the game smooth. A world map, inventory sorting, item hints, and richer sound make everyday play more convenient.

**Frontier** includes everything in Vanilla Plus: 65 mods, 12 resource packs, and 1 shader in total. Five building mods add windows, fences and walls, bridges, doors, and more stair designs. In unexplored areas you can find improved villages, ruins, towers, Nether buildings, pillager camps, and YUNG's redesigned dungeons, temples, mineshafts, strongholds, and Nether fortresses; MVS alone advertises [over 130 structure designs](https://modrinth.com/mod/moogs-voyager-structures). Villages gain sword- and crossbow-wielding guards. Spiders can climb walls and ceilings, and the world selection screen shows how long you have played each world. The unofficial MinePack port of xali's Enhanced Vanilla is enabled by default with Continuity and CIT Resewn Continuation; its ZIP credits xalixilax and is licensed CC BY-NC 4.0. Tree Harvester and its Collective dependency are pinned; Remodeled Doors 3D is enabled above xali's with an embedded blockstate compatibility pack.

MinePack Installer 1.5.0 includes Vanilla Plus 0.18.1 and Frontier 0.19.6. Low On Fire has been removed from both packs.

## Install

1. Download [MinePack-Installer-1.5.0-win-x64.zip](https://github.com/yarinka-yyy/minepack-installer/releases/download/v1.5.0/MinePack-Installer-1.5.0-win-x64.zip) and extract the entire ZIP into a folder. Keep the extracted files together.
2. Run `MinePack.Installer.exe`, choose **Vanilla Plus** or **Frontier**, then click **Install pack**. MinePack prepares the selected pack and updates its Launcher profile. It may close a running official Launcher during setup, then tries to open it when installation is complete.
3. In the official Minecraft Launcher, select the **MinePack** profile and click **Play**. On the first launch, the Launcher may download Minecraft's base files. If it did not open automatically, open it yourself and select MinePack.

The EXE is not digitally signed yet, so Windows may show a SmartScreen warning. Download the ZIP only from the release page linked above.

## Game folders and worlds

MinePack keeps its files in `%LOCALAPPDATA%\MinePack` by default; you can choose another folder in the installer. Each pack has a separate game folder. Switching packs keeps the previous folder and its worlds. MinePack does not automatically copy worlds between packs.

Repair and uninstall keep your worlds and other personal files. Uninstall removes files managed by MinePack and its Launcher profile. **Import worlds** copies worlds into the active pack and leaves the originals in place. Back up a world before opening it in another Minecraft version. The regular `.minecraft` folder and other Launcher profiles are left alone.

## Included mods and files

<details>
<summary>Performance &amp; Render Distance (8)</summary>

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
<summary>Graphics &amp; Animations (9)</summary>

- [Iris](https://modrinth.com/mod/YL57xq9U)
- [EMF](https://modrinth.com/mod/4I1XuqiY)
- [ETF](https://modrinth.com/mod/BVzZfTc1)
- [Punchy!](https://modrinth.com/mod/8aoMKplv)
- [Explosive Enhancement](https://modrinth.com/mod/OSQ8mw2r)
- [Dense Flowers](https://modrinth.com/mod/Ud3A1Fat)
- [Inventory Particles](https://modrinth.com/mod/XYnKrsxH)
- [Advancement Plaques](https://modrinth.com/mod/9NM0dXub)
- [Subtle Effects](https://modrinth.com/mod/4q8UOK1d) — optional ambient particles and visual effects

</details>

<details>
<summary>Tools &amp; Quality of Life (9)</summary>

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
<summary>Sound (2)</summary>

- [Cool Rain](https://modrinth.com/mod/iDyqnQLT)
- [Sound Physics Remastered](https://modrinth.com/mod/qyVF9oeo)

</details>

<details>
<summary>Technical Foundation (9)</summary>

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
<summary>Resource Packs (7)</summary>

- [Fresh Animations](https://modrinth.com/resourcepack/50dA9Sha)
- [Fresh Animations: Extensions](https://modrinth.com/resourcepack/YAVTU8mK)
- [Fancy Crops](https://modrinth.com/resourcepack/UGEVQ6t9)
- [Better Flame Particles](https://modrinth.com/resourcepack/ivUZsvzp)
- [Os' Colorful Grasses](https://modrinth.com/resourcepack/O2zhH8n8)
- [GUI Retextures — Dark](https://modrinth.com/resourcepack/ZM5PH9W6)
- [Better Click Sounds](https://modrinth.com/resourcepack/XWQ6jMjk)

</details>

<details>
<summary>Shader (1)</summary>

- [Complementary Reimagined](https://modrinth.com/shader/HVnmMxH1)

</details>

<details>
<summary>Frontier additions — building mods (5)</summary>

These are added to all Vanilla Plus files above.

- [Macaw's Windows](https://modrinth.com/mod/C7I0BCni)
- [Macaw's Fences and Walls](https://modrinth.com/mod/GmwLse2I)
- [Macaw's Bridges](https://modrinth.com/mod/GURcjz8O)
- [Macaw's Doors](https://modrinth.com/mod/kNxa8z3e)
- [Macaw's Stairs](https://modrinth.com/mod/iP3wH1ha)

For an external multiplayer server, these block mods must also be installed on that server. Singleplayer uses the integrated server.

</details>

<details>
<summary>Frontier additions — world &amp; structures (11)</summary>

These are added to all Vanilla Plus files and the five Macaw's mods above.

- [Better Villages](https://modrinth.com/mod/dGVX5JbJ)
- [MNS — Moog's Nether Structures](https://modrinth.com/mod/nGUXvjTa)
- [MVS — Moog's Voyager Structures](https://modrinth.com/mod/OQAgZMH1)
- [Structory](https://modrinth.com/datapack/aKCwCJlY) (Fabric mod version)
- [It Takes a Pillage Continuation](https://modrinth.com/mod/QOJOg1gE)
- [YUNG's Better Desert Temples](https://github.com/YUNG-GANG/YUNGs-Better-Desert-Temples)
- [YUNG's Better Dungeons](https://github.com/YUNG-GANG/YUNGs-Better-Dungeons)
- [YUNG's Better Jungle Temples](https://github.com/YUNG-GANG/YUNGs-Better-Jungle-Temples)
- [YUNG's Better Mineshafts](https://github.com/YUNG-GANG/YUNGs-Better-Mineshafts)
- [YUNG's Better Nether Fortresses](https://github.com/YUNG-GANG/YUNGs-Better-Fortresses)
- [YUNG's Better Strongholds](https://github.com/YUNG-GANG/YUNGs-Better-Strongholds)

The pack also adds [Voxy WorldGen](https://modrinth.com/mod/xT0lnNE9) and the required libraries [Library Ferret](https://modrinth.com/mod/DOB2l4oJ), [Moog's Structure Lib](https://modrinth.com/mod/1oUDhxuy), [Resourceful Lib](https://modrinth.com/mod/G1hIVOrD), and [YUNG's API](https://github.com/YUNG-GANG/YUNGs-API). The seven YUNG's files are bundled compatibility forks for Minecraft 26.2. New structures appear in unexplored chunks. External servers need these world generation mods and libraries installed; singleplayer uses the integrated server.

</details>

<details>
<summary>Frontier addition — villagers (1)</summary>

- [Guard Villagers (Fabric/Quilt)](https://modrinth.com/mod/59rkB3YY) — village guards

Subtle Effects is included in both builds, with Fzzy Config and Fabric Language Kotlin. Multiplayer servers need Guard Villagers installed for its gameplay.

Frontier also enables [F.M.R.P](https://modrinth.com/resourcepack/freshly-modded) for Guard Villagers models and [Semos Animations Lib](https://modrinth.com/resourcepack/semos-animations-lib) for the detailed movements. These are client-side resource packs; their appearance still needs an in-game check.

</details>

<details>
<summary>Frontier additions — spiders and play time (2)</summary>

- [Nyf's Spiders](https://modrinth.com/mod/nyfs-spiders) by Nyfaria — a Minecraft 26.2 compatibility fork that lets spiders climb walls and ceilings.
- [World Play Time](https://modrinth.com/mod/world-play-time) — a Minecraft 26.2 compatibility fork that shows each world's play time in the world selection screen.

Both JAR files are bundled with Frontier. World Play Time runs on the client; a multiplayer server needs Nyf's Spiders for its spider behavior.

</details>

## License

MinePack's original source code and documentation are available under [0BSD](LICENSE): you may use, modify, and distribute them without attribution. Mods, resource packs, the shader, the font, and Minecraft belong to their respective owners; 0BSD does not change their terms.

The bundled Nyf's Spiders fork is included with Nyfaria's permission for this repository. Publishing it in another public repository or commercial modpack requires separate permission from Nyfaria.
