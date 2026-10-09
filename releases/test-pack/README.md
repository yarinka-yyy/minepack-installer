# Test pack releases

This retained `test-pack` path contains Vanilla Plus release archives for installer compatibility. It is not the separate experimental Test 0.20.x pack.

## 0.18.4 — pinned performance additions and graphics defaults fix

Included unchanged in the local Installer `1.6.5` candidate; this immutable archive was already present in the local `1.6.4` candidate. Minecraft `26.2`, Fabric Loader `0.19.5`: 65 mods, 13 resource packs, and 1 shader. Its composition, Modrinth pins, 13-entry resource-pack order, and all 14 overrides are byte-identical to optimized release `0.18.3`. The existing overrides include Better Desert Temples `.2` with the corrected advancement predicate and xali `.2` with the axolotl CIT update and three targeted snowy-grass CTM rules. This scope does not fix BetterVillage NBT or unrelated log findings. New instances use `graphicsPreset:"custom"`, render distance 9, entity distance 200%, and at most 8 GiB Java heap; simulation distance remains at the Minecraft default. Repair preserves existing settings. Archive SHA-512: `789C9D3E0D712715A8E515F782657E373AF0E76605E46EB285F81C4883E7304BC23BCE2FC8C0860B44610F02D34405FF52096F22D189665019CADD55C14D303C`. Optimized `0.18.3` and former-current `0.18.2` archives remain unchanged. Automated and in-game acceptance status is recorded with the local build result; gameplay remains a separate user check.

## 0.18.2 — shared 26.2 base and statistics

Local candidate for Minecraft `26.2` and Fabric Loader `0.19.5`: 62 mods, 13 resource packs, and 1 shader. Adds pinned Better Statistics Screen `5.5.6` with TCDCommons `5.5.6` and 3D Default `1.16.0`; the shared Vanilla Plus base also now contains the world-generation set previously found only in Frontier. The 13 initial resource packs place 3D Default below the existing xali → Remodeled Doors → compatibility order. Compatibility ZIP `.2` preserves all 19 entries in `.1` and adds the three minimal alias models with parent `minecraft:block/block` and ambient occlusion disabled; `tools/Build-DoorCompatibility.ps1` rebuilds it deterministically. ZIP `.2` SHA-256: `791B1A98A8B973DE01EC9D1EBC10CBBB1D1D1D94782D3AE8CEC9F23F5227E0D7`. The exact Minecraft 26.2 resource check passed at format 88.0: 18 door/trapdoor blockstates and all 54 affected Remodeled Doors models (52 block, 2 item) resolve through the curated layers. Offline Smoke and strict direct-download Install/Repair/Uninstall passed. Archive SHA-512: `9B756275D14380848C06183CE8E693C8519ACDBD1EC13EBB3D65E1154F4E57E9EB3D03321A535FD1C5230797C3D093F925C1C24DE5B0E95D7B0B92722E4E3061`. The previous `0.18.1` archive remains immutable; in-game resource rendering and gameplay acceptance remain pending.

## 0.18.1 — Vanilla Plus without Low On Fire

Published with MinePack Installer `1.5.0` for Minecraft `26.2` and Fabric Loader `0.19.5`: 37 mods, 7 resource packs, and 1 shader. Removes only Low On Fire; all other pinned downloads, the shader, and initial player settings are unchanged. The former `0.18.0` archive remains immutable for Repair and Uninstall. Archive SHA-512: `2E259CEE78A2022CDE78012BA95E6EE5E789C3F0DA45D3C51B1D7E4FC689865E0D981FD784C850EF74492F02F8AF8497943394ADC15DBE9135718918FAFB9906`. Deterministic smoke plus direct network Install/Repair/Uninstall in a clean temporary instance passed in the normal Windows user environment.

## 0.18.0 — Vanilla Plus without Smooth Swapping

Minecraft `26.2`, Fabric Loader `0.19.5`. Removes only Smooth Swapping from `0.15.0`; the other 46 pinned downloads remain unchanged: 37 mods, 8 resource packs, and 1 shader. The Iris override remains. Archive SHA-512: `C7469A3820A4B9BF75132BD016FB99F5B18F3E6FF66CDF0DCFBCE937E5BA8309AD4062C813457B8F95401F6D2F3326ABDBCDBA067B911795B6C574CD9D525776`. Packwiz export, deterministic smoke, real temporary Install/Repair/Uninstall with SHA-512 verification, Release publish, and extracted ZIP hash checks passed. The `0.15.0` archive is retained to recognize and repair existing installations. Gameplay remains for the user's check.

## 0.15.0 — Smooth Swapping and Subtle Effects in Vanilla Plus

Minecraft `26.2`, Fabric Loader `0.19.5`. This release preserves all 43 downloads in `0.10.0` and adds the exact files already present in Vanilla 2 Plus `0.14.0`:

| Project | Pinned version | Downloaded file |
| --- | --- | --- |
| [Smooth Swapping](https://modrinth.com/mod/smooth-swapping) | `0.9.10-26.2` | `smoothswapping-0.9.10-26.2-fabric.jar` |
| [Subtle Effects](https://modrinth.com/mod/subtle-effects) | `1.14.3` | `SubtleEffects-fabric-26.2-1.14.3.jar` |
| [Fzzy Config](https://modrinth.com/mod/fzzy-config), required by Subtle Effects | `0.7.6+26.2` | `fzzy_config-0.7.6+26.2.jar` |
| [Fabric Language Kotlin](https://modrinth.com/mod/fabric-language-kotlin), required by Fzzy Config | `1.14.1+kotlin.2.4.20` | `fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar` |

The archive has 47 pinned Modrinth downloads: 38 mod JARs, 8 resource packs, and 1 shader, plus the Iris config override. Vanilla 2 Plus `0.14.0` remains unchanged and contains all 47 of these exact downloads. SHA-512: `9698BCBFE76BC6072ADB2609F2EDF17D4E823A5DB850BA0B54BE16E2EA39A967F6536C3FF025E7363BC6DED76D28BA1280DA1ACB8BD09D9FDA209889524ED45A`.

Packwiz export, deterministic smoke checks, Release publish, and installation in a separate local instance passed. Minecraft `26.2` launched through the MinePack Launcher profile; its log lists Smooth Swapping, Subtle Effects, Fzzy Config, and Fabric Language Kotlin during resource reload. Visual effects and inventory animations remain for the user's in-game check. The prior `0.10.0` archive and the installed Vanilla 2 Plus `0.14.0` instance remain intact.

## 0.10.0 — Vanilla Plus with Mod Menu candidate

Minecraft `26.2`, Fabric Loader `0.19.5`. Preserves all 41 files from `0.9.0` and adds [Mod Menu 20.0.2](https://modrinth.com/mod/modmenu/version/WdLLrOzD) (`mOgUt4GM` / `WdLLrOzD`, `modmenu-20.0.2.jar`) and its required [Text Placeholder API 3.1.0-beta.1+26.2](https://modrinth.com/mod/placeholder-api/version/NDqH16LT) (`eXts2L7r` / `NDqH16LT`, `placeholder-api-3.1.0-beta.1+26.2.jar`). Modrinth metadata explicitly lists Minecraft `26.2` and Fabric for both versions. The existing Fabric API satisfies Mod Menu's other required dependency. Both JARs are downloaded by pinned Modrinth CDN URL and SHA-512; neither is bundled in the `.mrpack`.

The 43-file export contains 34 mods, 8 resource packs, and 1 shader. Packwiz resolved the dependencies and exported the complete archive. A clean temporary install downloaded and SHA-512-verified all files, then passed Repair and Uninstall while preserving user data. Minecraft/Fabric startup and the in-game Mod Menu screen still require game testing. Archive SHA-512: `FDEA02362FB025310E26C3DE14DE18B907574BB412A0ADEC800881ECD6D8F31095D4021691EB0A35DCECB318E042F56360A36EDA79372453F658BC46090327E1`.

## 0.9.0 — map, world selection, UI and movement candidate

Minecraft `26.2`, Fabric Loader `0.19.5`. Preserves every file and setting from `0.8.0` and adds five requested mods plus the required Iceberg library:

| Component | Modrinth project / version | Pinned file |
| --- | --- | --- |
| [Xaero's World Map](https://modrinth.com/mod/xaeros-world-map/version/q0Wvp46X) 1.46.1 | `NcUtCpym` / `q0Wvp46X` | `xaeroworldmap-fabric-26.2-1.46.1.jar` |
| [Advancement Plaques](https://modrinth.com/mod/advancement-plaques/version/EULg1tpY) 1.7.2 | `9NM0dXub` / `EULg1tpY` | `AdvancementPlaques-26.2-fabric-1.7.2.jar` |
| [Cherished Worlds](https://modrinth.com/mod/cherished-worlds/version/VhoXPFdC) 17.0.0+26.2 | `3azQ6p0W` / `VhoXPFdC` | `cherishedworlds-fabric-17.0.0+26.2.jar` |
| [Leaf Me Alone](https://modrinth.com/mod/leaf-me-alone/version/RtzEHUwL) 1.2.0 | `ppMUvsIg` / `RtzEHUwL` | `leafmealone-1.2.0.jar` |
| [InvMove](https://modrinth.com/mod/invmove/version/VFFU6Lfs) 0.9.6 beta | `REfW2AEX` / `VFFU6Lfs` | `InvMove-0.9.6+26.2-Fabric.jar` |
| [Iceberg](https://modrinth.com/mod/iceberg/version/c69GepxX) 1.4.2.2 | `5faXoLqX` / `c69GepxX` | `Iceberg-26.2-fabric-1.4.2.2.jar` |

The chosen Modrinth versions explicitly include Fabric and Minecraft `26.2`. Their downloaded JARs passed SHA-512 verification. JAR metadata confirms that Advancement Plaques requires Iceberg `>=1.4.2`, InvMove requires the existing Cloth Config, and Xaero's World Map bundles its required XaeroLib `1.7.7` as a nested JAR. Advancement Plaques declares a conflict with Canvas, which is absent; Sodium's old-Iceberg incompatibility is `<1.2.7`, while this pack uses `1.4.2.2`. No new JAR or existing JAR declares a break against another selected version. Leaf Me Alone needs installation on both client and server for multiplayer; the local singleplayer integrated server uses the installed file. InvMove warns that movement in inventory screens may trigger some multiplayer anticheat systems. Its interaction with Inventory Sorting still needs an in-game check.

[First-person Model](https://modrinth.com/mod/first-person-model/version/6sgz2HEq) was **not added**. It requires Not Enough Animations, and the existing Punchy! mod has priority. Although Punchy! previously claimed First-person Model support, [a report of missing legs/body](https://github.com/tr7zw/FirstPersonModel/issues/630) and [an open request to synchronize their animations](https://github.com/tr7zw/FirstPersonModel/issues/656) leave the exact current combination unverified. Fresh Animations in this pack does not include the separate player extension; the concern is primarily Punchy! and the additional animation dependency. Reconsider only after a successful visual test with the exact pinned versions.

Packwiz exported a 41-file `.mrpack` containing 32 mods, 8 resource packs, and 1 shader. A clean temporary installer run downloaded and verified every file, then passed Repair and Uninstall while preserving user data. Minecraft/Fabric startup logs, the five new mods' in-game behavior, Leaf Me Alone on a multiplayer server, and InvMove with Inventory Sorting still require game testing. Archive SHA-512: `FDA99C8A9545A17890643E1BECCE9914176F1D53CB4DF81A762AB60B7B315EC233952181D2D10BE9B5555E74A89561803580D5E5AFFD7993BD16C79F48C9C648`.

## 0.8.0 — Punchy! first-person animation candidate

Minecraft `26.2`, Fabric Loader `0.19.5`. Preserves all 34 files from the user-tested `0.7.0` pack and adds [Punchy! 2.8a for Fabric 26.2](https://modrinth.com/mod/punchy-fpa/version/QShDZDjS): Modrinth project `8aoMKplv`, version `QShDZDjS`, file `mods/punchy-2.8a-fabric-26.2.jar`. The JAR declares Fabric Loader `>=0.19.3`, Java `>=25`, Minecraft `26.2`, and Fabric API; the pinned pack meets these requirements. No additional external dependency or declared hard break targets an existing mod. The JAR is downloaded from Modrinth CDN with SHA-512 verification, not embedded in the installer.

Punchy's JAR includes a resource pack `punchy:punchy` with animation data. The installer selects it in a new instance's `options.txt` after the eight file resource packs; Repair and Uninstall preserve later user choices. Fresh Animations and Extensions in this pack supply mob models and do not include the separate player extension. Punchy contains optional Iris rendering hooks. The existing BBE chest and shulker optimizations remain disabled for the Fresh Animations models.

There is an [open report](https://github.com/Traben-0/Entity_Model_Features/issues/542) of displaced mob limbs with Punchy, EMF 3.2.4, and Fresh Animations on Minecraft 1.21.1. The Punchy 2.5 [release notes](https://www.curseforge.com/minecraft/mc-mods/punchy/files/7984365) claim a fix for that combination, but the report was filed later and does not establish whether the current 26.2 versions are affected. Inspect mobs in game before treating this candidate as validated. [Grim AntiCheat](https://github.com/GrimAnticheat/Grim/wiki/Known-incompatible-client-mods) lists Punchy as incompatible on servers using Grim because attack/mining packet order can trigger checks; this is a multiplayer limitation outside the local mod stack.

The installer installed and hash-verified all 35 files in a clean temporary instance, then verified Repair and Uninstall. Minecraft launch, animation visuals, shader interaction, multiplayer compatibility, and FPS require user testing. Release SHA-512: `F191D98E2E87C6CB1B75274471B28535C2D717F795A6A1B94C2BE3F5015F9069AF5BB7AFA2A55FD0AFA0866656506F82E48C36FC00A38AE7580324E859DBAA38`.

## 0.7.0 — GUI, sound, vegetation, and optimization candidate

Minecraft `26.2`, Fabric Loader `0.19.5`. Based on `0.6.0`, with Smooth Swapping `0.9.10` removed after the user reported that it did not work in game. Its cause was not investigated. All other `0.6.0` components remain. Five pinned files were added:

| Component | Modrinth project / version | Filename |
| --- | --- | --- |
| [Os' Colorful Grasses (Mix)](https://modrinth.com/resourcepack/os-colorful-grasses/version/9KieAB5z) Mix-26.3 (also tagged 26.2) | `O2zhH8n8` / `9KieAB5z` | `Os' Colorful Grasses (Mix).zip` |
| [GUI Retextures](https://modrinth.com/resourcepack/gui-retextures/version/e91P9kR5) 2.1, dark variant | `ZM5PH9W6` / `e91P9kR5` | `GUIRetextures-Dark-2.1.zip` |
| [Better Click Sounds](https://modrinth.com/resourcepack/better-click-sounds/version/IgkvU58S) 1.3 | `XWQ6jMjk` / `IgkvU58S` | `better_click_sounds_1.3.zip` |
| [ImmediatelyFast](https://modrinth.com/mod/immediatelyfast/version/pMWERcSu) 1.16.5+26.2-fabric | `5ZwdcRci` / `pMWERcSu` | `ImmediatelyFast-Fabric-1.16.5+26.2.jar` |
| [FerriteCore](https://modrinth.com/mod/ferrite-core/version/d5ddUdiB) 9.0.0-fabric | `uXXizFIs` / `d5ddUdiB` | `ferritecore-9.0.0-fabric.jar` |

Modrinth metadata and SHA-512-verified downloads support Minecraft `26.2`. ImmediatelyFast's JAR requires Fabric Loader `>=0.19.0` and Java `>=25`; FerriteCore requires Minecraft `>=26.1 <27` and breaks only old Hydrogen, which is absent. Neither new mod declares extra dependencies. ImmediatelyFast's `1.16.5` changelog fixes an Iris shader rendering issue. The three ZIPs include resource format `88`, and their files do not overwrite the five earlier packs' grass, GUI texture, or sound assets. GUI Retextures and two earlier packs define different glyphs in `assets/minecraft/font/default.json`; their combined appearance still needs an in-game check. The eight packs are selected automatically in a new instance; user changes remain unmanaged. Os' Colorful Grasses is fetched from the author's Modrinth CDN, not embedded or redistributed in the installer. Effects on FPS and RAM depend on the player's system and scene.

The installer downloaded and hash-verified all 34 files in a clean temporary instance, then verified Repair and Uninstall. Gameplay, shader visuals, sound, resource-pack glyphs, and actual performance remain for user testing. Release SHA-512: `60F0913CDCF2F9FFD61E6A6759C554AEF0D59D27C18BFAEE35067380D17B56BDA1495E0388FC45503FB393CA5DE0D885CD026EBFBBFD086A1E7DFB86A36650DF`.

## 0.6.0 — inventory, audio, vegetation, and performance candidate

Minecraft `26.2`, Fabric Loader `0.19.5`; retains all 18 files from the gameplay-tested `0.5.0` pack. The 12 new pinned files are:

| Component | Modrinth project / version | Filename |
| --- | --- | --- |
| [Inventory Particles](https://modrinth.com/mod/inventory-particles) 3.2.0 | `XYnKrsxH` / `6fZZmxDU` | `InventoryParticles-3.2.0+26.2+fabric.jar` |
| [MossyLib](https://modrinth.com/mod/mossylib) 1.6.0 | `ffLDUGbm` / `sL8KZEzW` | `MossyLib-1.6.0+26.2+fabric.jar` |
| [Dense Flowers](https://modrinth.com/mod/dense-flowers) 0.3.1 | `Ud3A1Fat` / `hAZimeFe` | `dense-flowers-0.3.1+mc26.2.jar` |
| [Inventory Sorting](https://modrinth.com/mod/inventory-sorting) 3.0.1 | `5ibSyLAz` / `D6W3Lrmj` | `inventorysorter-fabric-3.0.1+mc26.2.jar` |
| [Cloth Config](https://modrinth.com/mod/cloth-config) 26.2.155 | `9s6osm5g` / `Nv3xnWXd` | `cloth-config-26.2.155.jar` |
| [Smooth Swapping](https://modrinth.com/mod/smooth-swapping) 0.9.10 | `ydZic5r4` / `aUhMczfZ` | `smoothswapping-0.9.10-26.2-fabric.jar` |
| [Cool Rain](https://modrinth.com/mod/coolrain) 1.4.0 | `iDyqnQLT` / `zc88gNk3` | `coolrain-1.4.0-26.2.jar` |
| [Sound Physics Remastered](https://modrinth.com/mod/sound-physics-remastered) 1.5.1 | `qyVF9oeo` / `d8iioMMp` | `sound-physics-remastered-fabric-1.5.1+26.2.jar` |
| [Held Item Info](https://modrinth.com/mod/held-item-info) 1.9.2 | `tEcWzCZz` / `bNAT9H23` | `held-item-info-1.9.2.jar` |
| [Better Block Entities](https://modrinth.com/mod/better-block-entities/version/IDqHHWrF) 1.3.7 | `ONZm0H7Y` / `IDqHHWrF` | `bbe-fabric-1.3.7+mc26.2.jar` |
| [Clumps](https://modrinth.com/mod/clumps) 26.2.1 | `Wnxd13zP` / `dEMopoOJ` | `Clumps-fabric-26.2-26.2.1.jar` |
| [Entity Culling](https://modrinth.com/mod/entityculling) 1.11.2 | `NNAgCjsB` / `RWjup6Jf` | `entityculling-fabric-1.11.2-mc26.2.jar` |

Modrinth metadata and the SHA-512-verified JARs confirm Fabric `26.2` support. Inventory Particles requires MossyLib `>=1.6.0`; Inventory Sorting requires Cloth Config `>=26.2.155` and Fabric API `>=0.153.0`; the existing Fabric API `0.161.0` satisfies this. Dense Flowers and BBE require the existing Sodium. BBE's `1.3.7` release notes explicitly target Sodium `0.9.0–0.9.1`, so the tested Iris/Voxy/Sodium `0.9.1` stack remains pinned. No mod-declared hard break targets another selected mod. Mod JARs are downloaded from Modrinth CDN by URL and SHA-512; none is embedded in the installer.

Fresh Animations: Extensions includes custom chest and shulker-box models. BBE can interfere with EMF/ETF models for those blocks, so a fresh instance starts with `optimize.chest=false` and `optimize.shulker=false` in BBE's verified `config/BBEConfig.json` format. Other BBE optimizations remain enabled. This initial config is unmanaged: Repair and Uninstall preserve later user changes. EMF's author recommends Entity Culling for animation-heavy packs; BBE lists C2ME support. Cool Rain generates material-specific rain sounds, while Sound Physics Remastered processes sound attenuation and reverb; no declared conflict was found. Clumps works on the integrated server in singleplayer; multiplayer XP grouping needs the server to have Clumps. Inventory Sorting's server-side functionality similarly depends on the server in multiplayer, though its client interface is available locally.

Smooth Swapping and Inventory Sorting both touch inventory screens. Upstream reports crashes with **other** sorting mods, but no confirmed issue with this exact pair was found during preflight. The user later confirmed that all other `0.6.0` additions worked but Smooth Swapping did not. Its cause was left uninvestigated at the user's request, and `0.7.0` removes it. The installer downloaded and hash-verified all 30 files in a clean temporary instance, then verified Repair and Uninstall. Release SHA-512: `0BFC3915A9F1856770536202449C3936A29BD2AD1FE1E090BD525FCF0E10307F9C1C2DCAEA69DD66C24F036663307C9034745D42559BFF23F2DBA9EE00B684E5`.

## 0.5.0 — gameplay-tested visual mods and resource packs

Minecraft `26.2`, Fabric Loader `0.19.5`; retains every pinned file from `0.4.0`, including C2ME. The user reported that Chunky radius 1024 completed in about 1.5 minutes with C2ME; the earlier baseline was an estimate, so the exact speedup is not established. This release adds the following exact Modrinth versions:

| Component | Project / version | Filename |
| --- | --- | --- |
| [Pick Up Notifier](https://modrinth.com/mod/pick-up-notifier/version/DWaZDkc8) 26.2.0 | `ZX66K16c` / `DWaZDkc8` | `PickUpNotifier-v26.2.0-mc26.2.x-Fabric.jar` |
| [Puzzles Lib](https://modrinth.com/mod/puzzles-lib) 26.2.4 | `QAGBst4M` / `aNOJuoCM` | `PuzzlesLib-v26.2.4-mc26.2.x-Fabric.jar` |
| [Forge Config API Port](https://modrinth.com/mod/forge-config-api-port) 26.2.1 | `ohNO6lps` / `rSd3GiG8` | `ForgeConfigAPIPort-v26.2.1-mc26.2.x-Fabric.jar` |
| [Explosive Enhancement](https://modrinth.com/mod/explosive-enhancement/version/q9vZmPqg) 1.4.2-26.2 | `OSQ8mw2r` / `q9vZmPqg` | `explosive-enhancement-1.4.2-26.2.jar` |
| [Entity Model Features](https://modrinth.com/mod/entity-model-features) 3.3.8 | `4I1XuqiY` / `uqpavrXj` | `entity_model_features-3.3.8-26.2-fabric.jar` |
| [Entity Texture Features](https://modrinth.com/mod/entitytexturefeatures) 7.2.4 | `BVzZfTc1` / `sTno2gjm` | `entity_texture_features-7.2.4-26.2-fabric.jar` |
| [Fresh Animations](https://modrinth.com/resourcepack/fresh-animations/version/RGIzA5em) 1.10.5 | `50dA9Sha` / `RGIzA5em` | `FreshAnimations_v1.10.5.zip` |
| [Fresh Animations: Extensions](https://modrinth.com/resourcepack/fresh-animations-extensions/version/R5ZGSF8A) 1.9.2 | `YAVTU8mK` / `R5ZGSF8A` | `FA+All_Extensions-v1.9.2.zip` |
| [Low On Fire](https://modrinth.com/resourcepack/low-on-fire) 26.3 (file tagged 26.2) | `RRxvWKNC` / `yQdcUfnr` | `LowOnFire v26.2§8.zip` |
| [Fancy Crops](https://modrinth.com/resourcepack/fancy-crops) 1.3 | `UGEVQ6t9` / `ZJEBZjg6` | `Fancy Crops v1.3.zip` |
| [Better Flame Particles](https://modrinth.com/resourcepack/better-flame-particles) 3.1 | `ivUZsvzp` / `5zlKz15s` | `better_flame_particles-v3.1-mc1.21.9+-resourcepack.zip` |

Modrinth metadata and JAR declarations require Fabric API plus Puzzles Lib and Forge Config API Port for Pick Up Notifier, and ETF for EMF. EMF/ETF provide Fresh Animations support with Sodium/Iris; OptiFine is not included. Explosive Enhancement's version metadata/JAR only require Fabric API; YACL and Mod Menu are optional configuration interfaces and are not in this release. Resource-pack ZIPs advertise a format range including Minecraft `26.2`'s resource format `88`. No known hard incompatibility was found with Voxy, C2ME, Iris or Sodium. Fresh Animations Extensions overlaps the base pack intentionally, so it is selected above Fresh Animations. Low On Fire and Better Flame Particles edit different fire assets. The only other duplicated asset path among the five packs is `assets/minecraft/font/default.json` in Extensions and Low On Fire; each defines a different glyph, so its combined behavior should be checked in game. The five packs are selected in a fresh `options.txt` during staging; this file is left unmanaged so user choices survive Repair and Uninstall.

The installer downloaded and hash-verified all 18 files in a temporary isolated instance, then verified Repair and Uninstall. The user later confirmed that all mods worked in Minecraft with no observed issues; exact FPS and complete logs were not reported. Release SHA-512: `0B94017C6C392AB05B17102C72A9A3207AE5F52BA7175F17C7FF5F764FEC06EA62397183DC3B0F32AF6309600DE87F82E66DE1C77ED51384ED528E04FC8E23F6`.

## 0.4.0 — C2ME comparison test

Pinned Minecraft `26.2` and Fabric Loader `0.19.5`. This is a separate versioned instance with the exact `0.3.0` file set plus one mod: [C2ME `0.4.2-alpha.0.52+26.2`](https://modrinth.com/mod/c2me-fabric/version/LmKTn6Yc), project `VSNURh3q`, version `LmKTn6Yc`, file `c2me-fabric-mc26.2-0.4.2-alpha.0.52.jar`. Modrinth reports no additional external dependencies; the JAR bundles its internal modules and declares Fabric Loader `>=0.18.3`, Java `>=25`, and Minecraft `>1.21.11`. The selected loader `0.19.5` and Minecraft `26.2` satisfy those constraints. Its declared conflicts (`tic_tacs`, `optifabric`) are absent from this pack. The user confirmed a successful Minecraft launch and Chunky run on the target PC.

Create a new world with the same recorded seed; do not reuse a world that Chunky has already generated. Run `/chunky radius 1024` and `/chunky start`. The user reported about 1.5 minutes with C2ME, while the `0.3.0` baseline of 2.5–3 minutes is uncertain; the exact speedup is not established. World spawn, background load, and generation order can differ. The `0.3.0` archive and installed instance remain available separately. Release SHA-512: `04483AB2F7996049C7962C5ADB835499DD1B05C36BBCA9EFBE36F3071B7685136EE56C51ECF55FEA0FCC94C3CB4F33AE48ED1DD4351CAF40D01A39B157A02D90`.

## 0.3.0 — Voxy and Chunky test

Pinned Minecraft `26.2` and Fabric Loader `0.19.5`. This is a separate versioned release; the `0.2.0` artifact remains available. The `.mrpack` downloads six exact files from Modrinth CDN and verifies SHA-512.

| Component | Modrinth project / version | Filename |
| --- | --- | --- |
| Fabric API 0.161.0+26.2 | `P7dR8mSH` / `ewUK83HI` | `fabric-api-0.161.0+26.2.jar` |
| Iris 1.11.2 | `YL57xq9U` / `oaD6KQls` | `iris-fabric-1.11.2+mc26.2.jar` |
| Sodium 0.9.1 | `AANobbMI` / `2Yom1N68` | `sodium-fabric-0.9.1+mc26.2.jar` |
| [Voxy 0.2.19-beta](https://modrinth.com/mod/voxy) | `fxxUqruK` / `LzyXnE51` | `voxy-0.2.19-beta.jar` |
| Chunky 1.5.3 | `fALzjamp` / `4Eotm6ov` | `Chunky-Fabric-1.5.3.jar` |
| Complementary Reimagined r5.9.3 | `HVnmMxH1` / `Bqen1mJX` | `ComplementaryReimagined_r5.9.3.zip` |

Iris and Voxy both require this exact Sodium version in Modrinth metadata. Fabric API is required by Voxy and Chunky. Chunky is included for optional pre-generation of a new world; C2ME is not required and was deferred until the basic stack was tested. The user confirmed an in-game run of Voxy, Chunky, and the shader with good FPS; Chunky completed a 1024-block radius in roughly 2.5–3 minutes. Voxy requires OpenGL 4.6. Release SHA-512: `9B50156730A5A17E264B4EA0B5AD0BE594CF426CB46BB54D4AF16E4960F1B55222B7F22C873135BBDF79E9BE8C05B069C0D44396DDD8DD8A77A5D649DBE57E84`.

## 0.2.0 — Complementary Reimagined test

Pinned Minecraft `26.3`, Fabric Loader `0.19.5`, and the following Modrinth files. The `.mrpack` downloads them from Modrinth CDN and verifies SHA-512; it does not bundle their binaries.

| Component | Modrinth project / version | Filename |
| --- | --- | --- |
| Fabric API | `P7dR8mSH` / `hHj6EvFZ` | `fabric-api-0.160.7+26.3.jar` |
| Iris 1.11.6 | `YL57xq9U` / `bAdKrpw8` | `iris-fabric-1.11.6+mc26.3.jar` |
| Sodium 0.9.2 | `AANobbMI` / `bAZQdGpg` | `sodium-fabric-0.9.2+mc26.3.jar` |
| Complementary Reimagined r5.9.3 | `HVnmMxH1` / `Bqen1mJX` | `ComplementaryReimagined_r5.9.3.zip` |

Iris requires exactly Sodium version `bAZQdGpg` in Modrinth metadata. Reimagined supports `26.3` and Iris. `config/iris.properties` selects and enables the shader on first launch. Release SHA-512: `76320C3EBB6B32D53EB0E58B8E4DD3FF721CEB2F718E2D18CB976A020DC2C4104F2E586277314DCC24227DC0DEA18CF6661835F01E04F61AEB51D22F88A8FF83`. Minecraft gameplay and shader rendering still require a user acceptance run.

## 0.1.0 — initial Fabric API test

This is a small, pinned validation pack. It is not a gameplay-tested public release.

| Item | Pinned value |
| --- | --- |
| Minecraft Java Edition | `26.3` (Mojang version manifest: release; Java runtime major version 25) |
| Fabric Loader | `0.19.5` (stable for Minecraft `26.3`) |
| Fabric API | project `P7dR8mSH`, version `hHj6EvFZ` (`0.160.7+26.3`, release) |
| Fabric API file | `fabric-api-0.160.7+26.3.jar` |
| Fabric API SHA-512 | `e80d3980c235a2245cfe61eee02d60d041245f9571117d86bd7edd6c62e158f681384a104375f588bf58966cb8c6eba79e66363afb9ecde93ea3411414276825` |
| Fabric API license | Apache-2.0; the `.mrpack` contains its pinned HTTPS download URL and hash, not a redistributed JAR |
| `.mrpack` SHA-512 | `453fc54446e6d7b379c7c07ca6f995998d6cba01791b0749d6929fce98bce59561aaa598b255344b342396251877a0e7aceccc3100ea179484d0a967ceba0ff5` |

The exported `modrinth.index.json` contains one required client-and-server file and dependencies `minecraft=26.3` and `fabric-loader=0.19.5`. Modrinth reports no additional dependencies for this Fabric API version. A second Packwiz export produced the same pinned versions, download URL, and SHA-512.

The `0.1.0` artifact remains bundled so the current installer can repair or uninstall an existing `0.1.0` instance. The installer now creates its own isolated official Launcher profile automatically. A live `0.1.0` profile and game launch were confirmed by the user; this does not validate `0.2.0` shader rendering.
