# MinePack Installer

English · [Русский](README.ru.md)

MinePack helps you install a curated collection of mods for **official Minecraft: Java Edition** without setting up each mod by hand. It downloads the selected files, checks them, and puts the game in a separate MinePack folder.

MinePack works on Windows and installs packs for Minecraft: Java Edition 26.2. To play, you need the official Minecraft Launcher and an account with access to Java Edition. The packs require Fabric; MinePack prepares it during installation, so you do not need to install Fabric separately.

## Choose a pack

**Vanilla Plus** includes 37 mods, 8 resource packs, and 1 shader. It focuses on performance and render distance, graphics and animations, useful tools, and sound.

**Frontier** includes everything in Vanilla Plus, plus building options, village guards, and new structures. It has 52 mods, 10 resource packs, and 1 shader.

## Install

A public download link is not available yet. If you have received the MinePack ZIP, follow these steps:

1. Extract the entire ZIP into a folder. Keep the extracted files together.
2. Run `MinePack.Installer.exe`, choose **Vanilla Plus** or **Frontier**, then click **Install pack**. MinePack prepares the selected pack and updates its Launcher profile. It may close a running official Launcher during setup, then tries to open it when installation is complete.
3. In the official Minecraft Launcher, select the **MinePack** profile and click **Play**. On the first launch, the Launcher may download Minecraft's base files. If it did not open automatically, open it yourself and select MinePack.

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
<summary>Frontier additions — world &amp; structures (4)</summary>

These are added to all Vanilla Plus files and the five Macaw's mods above.

- [Better Villages](https://modrinth.com/mod/dGVX5JbJ)
- [MNS — Moog's Nether Structures](https://modrinth.com/mod/nGUXvjTa)
- [MVS — Moog's Voyager Structures](https://modrinth.com/mod/OQAgZMH1)
- [Structory](https://modrinth.com/datapack/aKCwCJlY) (Fabric mod version)

The required libraries [Library Ferret](https://modrinth.com/mod/DOB2l4oJ) and [Moog's Structure Lib](https://modrinth.com/mod/1oUDhxuy) are included. New structures appear in unexplored chunks. External servers need these world generation mods and libraries installed; singleplayer uses the integrated server.

</details>

<details>
<summary>Frontier addition — villagers (1)</summary>

- [Guard Villagers (Fabric/Quilt)](https://modrinth.com/mod/59rkB3YY) — village guards

Subtle Effects is included in both builds, with Fzzy Config and Fabric Language Kotlin. Multiplayer servers need Guard Villagers installed for its gameplay.

Frontier also enables [F.M.R.P](https://modrinth.com/resourcepack/freshly-modded) for Guard Villagers models and [Semos Animations Lib](https://modrinth.com/resourcepack/semos-animations-lib) for the detailed movements. These are client-side resource packs; their appearance still needs an in-game check.

</details>
