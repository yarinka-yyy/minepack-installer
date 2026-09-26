# Vanilla 2 Plus 0.14.0

Minecraft `26.2` · Fabric Loader `0.19.5` · Fabric API `0.161.0+26.2`.

This candidate preserves all 59 downloads and the Iris override in [0.13.0](vanilla-2-plus-0.13.0.mrpack), then adds two client-side resource packs:

| Project | Pinned version | Downloaded file |
| --- | --- | --- |
| [F.M.R.P](https://modrinth.com/resourcepack/freshly-modded) | `3.0.5` | `Freshly Modded 3.0.5.zip` |
| [Semos Animations Lib](https://modrinth.com/resourcepack/semos-animations-lib) | `2.0.4` | `Semos Animations Lib 2.0.4.zip` |

F.M.R.P explicitly supports both Guard Villagers models and is intended to run alongside Fresh Animations. Semos is optional for F.M.R.P, but adds detailed attack, walk, swim, and idle animations. Both projects list Minecraft `26.2` compatibility. The pack already includes EMF `3.3.8` and ETF `7.2.4`. On first installation, the installer selects Semos before F.M.R.P, above the existing Fresh Animations packs; Vanilla Plus keeps its original eight resource packs. The two ZIPs are fetched from their pinned Modrinth CDN URLs and verified by SHA-512, not embedded in the installer.

This archive has 61 pinned downloads: 50 mod JARs, 10 resource packs, and 1 shader. The Iris override brings the managed path count to 62. Archive SHA-512: `6B7D4560296AE0CB79E212B4D0DB18FDF0CA739C2AB7CBBED546430CD959D78E57735EC8336136E814DAEEA8F7E9C4848722F12000C2C231F391C9BAE1F8222B`.

Packwiz export, Release publish, deterministic smoke tests, and a clean temporary download/install of all 61 files passed. That installation verified SHA-512, Repair, Uninstall, and preservation of user data. The local `0.14.0` instance is active in the MinePack Launcher profile; `options.txt` enables both ZIPs, and F.M.R.P contains Guard Villagers model files. Launcher start was requested. Static asset comparison found two identical files shared with Semos and one different vanilla Illusioner texture shared with Fresh Animations; F.M.R.P takes priority for that texture. Guard appearance and motion, as well as the Illusioner texture, remain for the user's visual check in Minecraft.

The previous `0.13.0` archive and its SHA-512 remain unchanged: `86629C7E463D51DF8AD49F32D6A9AEC1D6534769144AA6BF12AA58B324E6A3F22388AB7FD5148578C0A9544CA14EF23654B54B5554D06E0C071B034C09906EED`.

## Previous candidate: 0.13.0

Minecraft `26.2` · Fabric Loader `0.19.5` · Fabric API `0.161.0+26.2`.

This candidate preserves all 54 downloads and the Iris override in [0.12.0](vanilla-2-plus-0.12.0.mrpack), then adds:

| Project | Pinned version | Downloaded file |
| --- | --- | --- |
| [Smooth Swapping](https://modrinth.com/mod/smooth-swapping/version/aUhMczfZ) | `0.9.10-26.2` | `smoothswapping-0.9.10-26.2-fabric.jar` |
| [Subtle Effects](https://modrinth.com/mod/subtle-effects/version/1uvLyKlq) | `1.14.3` | `SubtleEffects-fabric-26.2-1.14.3.jar` |
| [Guard Villagers (Fabric/Quilt)](https://modrinth.com/mod/59rkB3YY) | `2.1.3-26.2` | `guardvillagers-2.1.3-26.2.jar` |
| [Fzzy Config](https://modrinth.com/mod/fzzy-config/version/EQSFgLYw), required by Subtle Effects | `0.7.6+26.2` | `fzzy_config-0.7.6+26.2.jar` |
| [Fabric Language Kotlin](https://modrinth.com/mod/fabric-language-kotlin/version/eRRZzGMc), required by Fzzy Config | `1.14.1+kotlin.2.4.20` | `fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar` |

All five added files are pinned to Minecraft `26.2`/Fabric, with required dependencies included and no declared direct incompatibility with the existing pack. This is preflight evidence, not proof that every feature behaves correctly in game. Smooth Swapping previously loaded but did not work in the older `0.6.0` pack; that cause was not investigated then. Mod Menu is now included and exposes its settings, but in-game animation and interactions with Inventory Sorting still need retesting.

Subtle Effects adds sparks around torches and other fire blocks, plus smoke and other optional particles. The existing Better Flame Particles resource pack changes flame particle textures; its asset paths do not overlap with Subtle Effects' smoke and spark effects. Extra visuals may coexist with Explosive Enhancement or Inventory Particles and can be adjusted in Subtle Effects' settings. Some Subtle Effects features need server installation; its client features work without it.

Guard Villagers adds a separate guard entity and villager AI changes. Fresh Animations animates vanilla villagers, not these guards; an optional third-party compatibility resource pack exists but is not included. Guard Villagers must also be installed on an external server. The pre-existing Better Villages `0.12.0` log reports three invalid village NBT templates; whether this affects guard spawning in specific villages remains to be checked in game.

The archive has 59 pinned Modrinth downloads: 50 mod JARs (including the Structory datapack packaged as a mod), 8 resource packs, and 1 shader. The Iris override brings the managed path count to 60. The installer downloads original files from exact Modrinth CDN URLs and verifies SHA-512; no third-party JAR is embedded in the `.mrpack` or EXE. Structory remains credited under the [Stardust Labs license](https://github.com/Stardust-Labs-MC/license/blob/main/license.txt); Subtle Effects is credited and linked above in accordance with its modpack terms.

Archive SHA-512: `86629C7E463D51DF8AD49F32D6A9AEC1D6534769144AA6BF12AA58B324E6A3F22388AB7FD5148578C0A9544CA14EF23654B54B5554D06E0C071B034C09906EED`.

## Validation

Packwiz export, Release build, deterministic smoke checks, and a clean temporary install of all 59 downloads passed. The temporary install verified each SHA-512, Repair, Uninstall, and preservation of user settings and worlds. Minecraft launch, Smooth Swapping animation, effects, and guard behavior remain for the user's game test.

Previous `0.12.0` archive SHA-512: `2AC6213EF9DF36C8FFE7DFC36B90DBD4CE2CEB5EB740BCDFD95EDAD46AF0420ED6CF0959C134C99AE54D0BB972D39AD2E89C9AF4A7857C1A88D18E79F76C5A86`. The `0.11.0` archive also remains unchanged.
