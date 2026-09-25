# MinePack Installer

English · [Русский](README.ru.md)

MinePack installs the pinned 0.8.0 Minecraft 26.2 modpack in a few clicks. **Requires the official Minecraft: Java Edition and the official Minecraft Launcher; your account stays in the Launcher.**

For Windows · Fabric · Minecraft 26.2 · Release 0.8.0

The pack includes 26 mods, 8 resource packs, and 1 shader. The installer downloads the pinned files, checks them, and creates a separate MinePack profile and game folder in the official Launcher.

## Install

1. A public installer download has not been confirmed yet, so there is no download link here.
2. When a complete installer package is published, download and extract it. Keep all files from the package together.
3. Close Minecraft Launcher, run `MinePack.Installer.exe`, click **Install pack**, and wait for **Pack is ready**.
4. Open the official Minecraft Launcher, choose the **MinePack** profile, and click **Play**. On the first launch, the Launcher downloads the Minecraft base files.

## Game files and worlds

By default, MinePack stores its data in `%LOCALAPPDATA%\MinePack`; you can choose another folder in the installer. Release 0.8.0 gets its own folder under `instances`, with separate `mods`, `resourcepacks`, `shaderpacks`, `saves`, and game settings.

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
<summary>Graphics &amp; Animations (7)</summary>

- [Iris](https://modrinth.com/mod/YL57xq9U)
- [EMF](https://modrinth.com/mod/4I1XuqiY)
- [ETF](https://modrinth.com/mod/BVzZfTc1)
- [Punchy!](https://modrinth.com/mod/8aoMKplv)
- [Explosive Enhancement](https://modrinth.com/mod/OSQ8mw2r)
- [Dense Flowers](https://modrinth.com/mod/Ud3A1Fat)
- [Inventory Particles](https://modrinth.com/mod/XYnKrsxH)

</details>

<details>
<summary>Tools &amp; Quality of Life (4)</summary>

- [Chunky](https://modrinth.com/mod/fALzjamp)
- [Inventory Sorting](https://modrinth.com/mod/5ibSyLAz)
- [Held Item Info](https://modrinth.com/mod/tEcWzCZz)
- [Pick Up Notifier](https://modrinth.com/mod/ZX66K16c)

</details>

<details>
<summary>Sound (2)</summary>

- [Cool Rain](https://modrinth.com/mod/iDyqnQLT)
- [Sound Physics Remastered](https://modrinth.com/mod/qyVF9oeo)

</details>

<details>
<summary>Technical Foundation (5)</summary>

- [Fabric API](https://modrinth.com/mod/P7dR8mSH)
- [Cloth Config API](https://modrinth.com/mod/9s6osm5g)
- [Forge Config API Port](https://modrinth.com/mod/ohNO6lps)
- [MossyLib](https://modrinth.com/mod/ffLDUGbm)
- [Puzzles Lib](https://modrinth.com/mod/QAGBst4M)

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

## Build from source

On Windows with the .NET 10 SDK, publish the installer with:

```powershell
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish-0.8.0
```
