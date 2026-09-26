# MinePack Installer

English · [Русский](README.ru.md)

MinePack installs a pinned modpack for **official Minecraft: Java Edition** through the official Minecraft Launcher. Your account stays in the Launcher.

For Windows · Fabric · Minecraft 26.2 · Official Minecraft Launcher

Choose one build: **Vanilla Plus 0.15.0** has 38 mods, 8 resource packs, and 1 shader, including Smooth Swapping and Subtle Effects. **Vanilla 2 Plus 0.14.0** includes those files and settings, five Macaw's building mods, four world and structure projects, Guard Villagers, and their required libraries (50 mods total). It also has two extra resource packs for animated guards (10 resource packs total). The installer downloads pinned files, checks their hashes, and uses one MinePack profile with a separate game folder for the active build.

## Install

1. A public installer download has not been confirmed yet, so there is no download link here.
2. When a complete installer package is published, download and extract it. Keep all files from the package together.
3. Run `MinePack.Installer.exe`, choose **Vanilla Plus** or **Vanilla 2 Plus**, and click **Install pack**. MinePack closes an open official Launcher, installs the selected build and updates the MinePack profile, then sends a request to open the Launcher.
4. Choose the **MinePack** profile and click **Play**. On the first launch, the Launcher downloads the Minecraft base files.

## Game files and worlds

By default, MinePack stores its data in `%LOCALAPPDATA%\MinePack`; you can choose another folder in the installer. Each build gets its own folder under `instances`, with separate `mods`, `resourcepacks`, `shaderpacks`, `saves`, and game settings. The MinePack profile points to the active folder. Switching builds keeps the previous folder and its worlds; MinePack does not copy worlds between builds.

The vanilla `.minecraft` folder, its worlds, and other Launcher profiles are left alone. An older MinePack version stays in its own folder until a new installation succeeds. Repair and uninstall keep worlds and other user files. World import copies worlds and leaves the originals in place. Back up a world before opening it in a different Minecraft version.

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
<summary>Tools &amp; Quality of Life (10)</summary>

- [Chunky](https://modrinth.com/mod/fALzjamp)
- [Inventory Sorting](https://modrinth.com/mod/5ibSyLAz)
- [Held Item Info](https://modrinth.com/mod/tEcWzCZz)
- [Pick Up Notifier](https://modrinth.com/mod/ZX66K16c)
- [Xaero's World Map](https://modrinth.com/mod/NcUtCpym)
- [Cherished Worlds](https://modrinth.com/mod/3azQ6p0W)
- [Leaf Me Alone](https://modrinth.com/mod/ppMUvsIg)
- [InvMove](https://modrinth.com/mod/REfW2AEX)
- [Mod Menu](https://modrinth.com/mod/mOgUt4GM)
- [Smooth Swapping](https://modrinth.com/mod/ydZic5r4) — animated item movement in inventories

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
<summary>Resource Packs (8)</summary>

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
<summary>Shader (1)</summary>

- [Complementary Reimagined](https://modrinth.com/shader/HVnmMxH1)

</details>

<details>
<summary>Vanilla 2 Plus additions — building mods (5)</summary>

These are added to all Vanilla Plus files above.

- [Macaw's Windows](https://modrinth.com/mod/C7I0BCni)
- [Macaw's Fences and Walls](https://modrinth.com/mod/GmwLse2I)
- [Macaw's Bridges](https://modrinth.com/mod/GURcjz8O)
- [Macaw's Doors](https://modrinth.com/mod/kNxa8z3e)
- [Macaw's Stairs](https://modrinth.com/mod/iP3wH1ha)

For an external multiplayer server, these block mods must also be installed on that server. Singleplayer uses the integrated server.

</details>

<details>
<summary>Vanilla 2 Plus additions — world &amp; structures (4)</summary>

These are added to all Vanilla Plus files and the five Macaw's mods above.

- [Better Villages](https://modrinth.com/mod/dGVX5JbJ)
- [MNS — Moog's Nether Structures](https://modrinth.com/mod/nGUXvjTa)
- [MVS — Moog's Voyager Structures](https://modrinth.com/mod/OQAgZMH1)
- [Structory](https://modrinth.com/datapack/aKCwCJlY) (Fabric mod version)

The required libraries [Library Ferret](https://modrinth.com/mod/DOB2l4oJ) and [Moog's Structure Lib](https://modrinth.com/mod/1oUDhxuy) are included. New structures appear in unexplored chunks. External servers need these world generation mods and libraries installed; singleplayer uses the integrated server.

</details>

<details>
<summary>Vanilla 2 Plus addition — villagers (1)</summary>

- [Guard Villagers (Fabric/Quilt)](https://modrinth.com/mod/59rkB3YY) — village guards

Smooth Swapping and Subtle Effects are included in both builds, with Fzzy Config and Fabric Language Kotlin for Subtle Effects. Smooth Swapping is being retested after it did not work in an earlier build; Mod Menu provides its settings. Multiplayer servers need Guard Villagers installed for its gameplay.

Vanilla 2 Plus also enables [F.M.R.P](https://modrinth.com/resourcepack/freshly-modded) for Guard Villagers models and [Semos Animations Lib](https://modrinth.com/resourcepack/semos-animations-lib) for the detailed movements. These are client-side resource packs; their appearance still needs an in-game check.

</details>

## Build from source

On Windows with the .NET 10 SDK, publish the installer with:

```powershell
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish-0.15.0
```
