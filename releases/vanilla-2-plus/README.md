# Frontier releases

## 0.19.7 — local resource and advancement candidate

Minecraft `26.2` · Fabric Loader `0.19.5` · 65 mods, 12 resource packs, 1 shader. This local candidate replaces only the xali `.minepack.1` override with `.minepack.2` and YUNG's Better Desert Temples `5.1.1-minepack.1` with the source-built `5.1.1-minepack.2`. The remaining indexed pins and overrides match `0.19.6`; the historical archive remains available. Archive SHA-512: `11555813B812AC8469F82C10C91A2530D065BD53A71B2EF57E05718FA18F12B88175A6F2DFBDF65FF72C86BE44E83C2FB9D96DC17DC38AAC5B6EEBE5EF95AB2E`. Resource/source validators and deterministic smoke are local checks only. This candidate is not part of the published Installer 1.5.0 package; gameplay, resource rendering, and fresh advancement-log validation remain pending.

The corresponding YUNG's source snapshots, licenses, and build mapping are included in this repository and the audit-candidate bundle under `third-party/yungs-sources/`; see [the source-kit notes](../../third-party/README.md). A new installer GitHub Release and final legal review remain separate.

## 0.19.6 — Frontier for MinePack Installer 1.5.0

Minecraft `26.2` · Fabric Loader `0.19.5` · 65 mods, 12 resource packs, 1 shader. Removes only Low On Fire from `0.19.5`; all other files retain their exact versions and hashes. This release keeps Tree Harvester `26.2.0-9.4`, Collective `26.2.0-8.40`, Remodeled Doors 3D `2.2.1`, and the embedded `Remodeled-Doors-26.2-xalis-blockstates.1.zip`. A fresh install enables xali's Enhanced Vanilla, then Remodeled Doors, then the compatibility ZIP at higher priority. Tree Harvester's settings remain unchanged. The former `0.19.5` archive is retained for Repair and Uninstall. Archive SHA-512: `5FEC73C5E023C251D35E20A32C181C0679669972D8945642D36725FFE575969473CA2F7AB75699D4E7ED91CB78AE57628570189A7548FE3898968BD663467D2B`. Deterministic smoke, local-cache installation, and direct network Install/Repair/Uninstall in clean temporary instances passed. Gameplay acceptance was supplied by the user for the existing mod and door combination; this release only removes one resource pack.

## 0.19.5 — Tree Harvester and 3D doors

Minecraft `26.2` · Fabric Loader `0.19.5` · 65 mods, 13 resource packs, 1 shader. Adds pinned [Tree Harvester `26.2.0-9.4`](https://modrinth.com/mod/tree-harvester/version/3VcDfhZP) and required [Collective `26.2.0-8.40`](https://modrinth.com/mod/collective), with the mod's default sneak-to-harvest settings unchanged. [Remodeled Doors 3D `2.2.1`](https://modrinth.com/resourcepack/remodeled-doors/version/2.2.1) is downloaded from Modrinth. The embedded `Remodeled-Doors-26.2-xalis-blockstates.1.zip` restores the Minecraft 26.2 door and trapdoor blockstate links to Remodeled's models above xali's Enhanced Vanilla; both ZIPs are enabled by default on a fresh install. Frontier `0.19.4` remains pinned for Repair. Archive SHA-512: `36115F782120B2F98D37C5294E112FD72E995F35394FA165D5ADDC36305E1AD1B11D0A1650B868572E01B62970ED389975C01B30500653B2E7531BD1C20C6DEC`. This candidate has no GitHub Release. In-game door rendering and Tree Harvester behavior still need user validation.

## 0.19.4 — xali's Enhanced Vanilla for Minecraft 26.2

Minecraft `26.2` · Fabric Loader `0.19.5` · 63 mods, 11 resource packs, 1 shader. Adds the user-tested, noncommercial MinePack port of [xali's Enhanced Vanilla](https://modrinth.com/resourcepack/xalis-enhanced-vanilla) as an embedded `resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip`, enabled by default in new installations. The ZIP contains `CREDITS.txt` naming xalixilax, the original v1.11.2b release, the changes made for 26.2, and the CC BY-NC 4.0 license. It is an unofficial adaptation. [Continuity `3.0.1+26.2`](https://modrinth.com/mod/continuity/version/mgUN5Xz2) and [CIT Resewn Continuation `1.2.2-fork.13+26.2`](https://modrinth.com/mod/cit-resewn-continuation/version/VftogBGR) are pinned client-side downloads. Both JAR manifests require the already pinned Fabric API `0.161.0+26.2`; CIT bundles its defaults module and declares a conflict with Canvas, which is absent from this pack. Frontier `0.19.3` remains available for Repair. Archive SHA-512: `C456ED02D11506C04D3281D95189A0178B403071EF721F377A85053D3A69A81F7103A538A5CAE7A48F8C81A54715A1510FC20976058E1154F6D0F8F4E10C163F`. This candidate is on `main` and has no GitHub Release. Full-pack gameplay and resource rendering still need validation.

## 0.19.3 — spiders and world play time

Minecraft `26.2` · Fabric Loader `0.19.5` · 61 mods, 10 resource packs, 1 shader. The 63 pinned downloads and initial configs from `0.19.2` remain unchanged. Two more Minecraft 26.2 compatibility forks are bundled in `overrides/mods/`: Nyf's Spiders by Nyfaria and World Play Time. The former `0.19.2` archive remains available for Repair. SHA-512: `5D62203DD9A729207BB509F08BCF67F81A27F87C68E86913FB2ABBA043CB4FC7EAAB65A31AB4E72E134E0EDDB393B1BC9B58B136ADD4A0F46C8D3FA5C1BBFAF7`.

## 0.19.2 — YUNG's structure mods for Minecraft 26.2

Minecraft `26.2` · Fabric Loader `0.19.5` · 59 mods, 10 resource packs, 1 shader. The 63 pinned downloads and initial configs from `0.19.1` remain unchanged. Seven local LGPLv3 compatibility forks are bundled in `overrides/mods/`: YUNG's API, Better Desert Temples, Better Dungeons, Better Jungle Temples, Better Mineshafts, Better Nether Fortresses, and Better Strongholds. The former `0.19.1` archive remains available for Repair. SHA-512: `BEE5575B637E697C96DE783E8D252644BD24E92330346B2BF30BC55FDBCC2E166B27798139A3202C3E7E39B20EFD662A9D56EF3FCDF091BBB1075D4518D32A87`.

## 0.19.1 — lower Voxy WorldGen background load

The initial Voxy WorldGen config keeps `generationRadius=128` and changes `maxActiveTasks` from 6 to 3, based on the user's Prism Launcher play test. Downloads, mods, and other settings match 0.19.0. The config is applied to new installations only; later player changes remain untouched by Repair and Uninstall. SHA-512: `3C4CA97D8B469D750F852D16EBD402E8C6AD328ACB16AB267B87AB6CDDAF03505103EC933AFA03172A8E8704498898B8E05C6140D27F831A0F051A36CA309823`.

## 0.19.0 — remove Smooth Swapping and reset Voxy WorldGen radius

Minecraft `26.2` · Fabric Loader `0.19.5` · 52 mods, 10 resource packs, 1 shader. Compared with `0.17.0`, only Smooth Swapping is removed from the 64 downloads. The initial `config/voxyworldgenv2.json` sets `generationRadius` to the upstream default of `128` chunks; other settings and the Guard Villagers override are unchanged. This override is applied only on a fresh instance and remains unmanaged so later player edits survive Repair and Uninstall. The archive has 63 pinned downloads and three overrides. SHA-512: `EDA5F98DA58A14BE7C5A608946559483E0DB38C60F85E98B64B45C9C7F7FB4063B5270A578E00C47139026BAA41AD23B6521804B4FD2C939CDE6340D4B755C67`. Packwiz export, deterministic smoke, real temporary Install/Repair/Uninstall with SHA-512 verification, Release publish, and extracted ZIP hash checks passed. The `0.17.0` archive is retained to recognize and repair existing installations. Gameplay and FPS remain for the user's check.

## Previous candidate: 0.17.0

Minecraft `26.2` · Fabric Loader `0.19.5` · 53 mods, 10 resource packs, 1 shader.

This release preserves all 61 downloads and Guard Villagers settings from [0.16.0](vanilla-2-plus-0.16.0.mrpack). It adds [It Takes a Pillage Continuation `1.0.12`](https://modrinth.com/mod/it-takes-a-pillage-continuation), its required Resourceful Lib `5.0.4`, and [Voxy WorldGen `2.4.3`](https://modrinth.com/mod/voxy-worldgen). The existing F.M.R.P `3.0.5` archive contains Archer, Legioner, and Skirmisher models under `assets/takesapillage/`; its priority remains above Fresh Animations. The new `config/voxyworldgenv2.json` sets `generationRadius` to the supported maximum of `512` chunks, preserving the other first-run defaults. Both Guard Villagers and Voxy WorldGen configs are initial overrides and stay unmanaged after installation so player changes survive Repair and Uninstall.

The `.mrpack` contains 64 pinned downloads and three overrides. SHA-512: `4631317E04F547AD72E93B9C40E8C6BEB2FDDDD081D9A63F36C62D24BB9EAD6A0FFC52257260FE584223C6E134120DF7E8B4B05C1E3D5BF24631EDB37F488014`. Packwiz export, deterministic smoke, clean temporary downloads with SHA-512 checks, Install/Repair/Uninstall, and Release publish passed. The local `0.17.0` instance has 65 verified managed files, both initial configs, and is selected in the MinePack Launcher profile; `0.16.0` remains installed. The WPF installer now runs Launcher checks and install/repair work off its UI thread and limits progress redraws to ten per second. Gameplay, generation at radius 512, animation appearance, and visible UI responsiveness require user observation. Upstream has open reports of [high idle CPU](https://github.com/iSeeEthan/voxy_worldgen_v2/issues/98) and [hanging on world exit](https://github.com/iSeeEthan/voxy_worldgen_v2/issues/99) with Voxy WorldGen; keep the previous 0.16.0 instance available during testing.

## Previous candidate: 0.16.0

Minecraft `26.2` · Fabric Loader `0.19.5` · Fabric API `0.161.0+26.2`.

This release preserves all 61 pinned downloads from [0.14.0](vanilla-2-plus-0.14.0.mrpack) and adds `config/guardvillagers.json` as an initial override. Guard Villagers `2.1.3-26.2` starts with `followHero=false` and `reputationRequirement=-2147483648`, allowing guard inventory access, equipment changes, patrol, and following without Hero of the Village. The existing `giveGuardStuffHotv=false` and `setGuardPatrolHotv=false` remain unchanged. The config is installed before first launch and is intentionally absent from the managed-file manifest, so Repair and Uninstall preserve later player edits. A dedicated server needs the same server-side config.

The archive has 61 pinned downloads and two overrides (Iris and Guard Villagers). Archive SHA-512: `19E98E3D10001E163FEA20870B0D63B462F62B717A0422D4DD87F7AF11B07160325BDBF851D3B3E581F13F389741A239B88CA9630C17927A96DCC7D83951B0AD`. Packwiz export, deterministic smoke, real temporary downloads and SHA-512 checks, Install/Repair/Uninstall, and Release publish passed. The local `0.16.0` instance was installed with 62 managed files; the user confirmed in-game that the guard interactions work without Hero of the Village.

## Previous candidate: 0.14.0

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
