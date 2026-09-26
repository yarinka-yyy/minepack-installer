# Vanilla 2 Plus 0.12.0

Minecraft `26.2` · Fabric Loader `0.19.5` · Fabric API `0.161.0+26.2`.

This candidate keeps all 48 downloads and the Iris configuration override from the tested [0.11.0 release](vanilla-2-plus-0.11.0.mrpack). It adds four world and structure projects and their two required libraries:

| Project | Pinned version | Downloaded file |
| --- | --- | --- |
| [Better Villages](https://modrinth.com/mod/better-village/version/ALHOnFvJ) | `4.0.0` | `bettervillage-fabric-26.2-4.0.0.jar` |
| [MNS — Moog's Nether Structures](https://modrinth.com/mod/mns-moogs-nether-structures/version/OLTqXnsN) | `3.1.1` | `MoogsNetherStructures-universal-1.21-3.1.1.jar` |
| [MVS — Moog's Voyager Structures](https://modrinth.com/mod/moogs-voyager-structures/version/PiFoSPXI) | `5.1.3` | `MoogsVoyagerStructures-universal-1.21-5.1.3.jar` |
| [Structory](https://modrinth.com/datapack/structory/version/TUbwu7eG), Fabric JAR | `1.3.17` | `Structory_26.2_v1.3.7.jar` |
| [Library Ferret](https://modrinth.com/mod/library-ferret/version/AIIk4we2), required by Better Villages | `5.0.0` | `libraryferret-fabric-26.2-5.0.0.jar` |
| [Moog's Structure Lib](https://modrinth.com/mod/moogs-structure-lib/version/Yvc02xg9), required by MNS and MVS | `3.3.0` | `MoogsStructureLib-fabric-26.2-3.3.0.jar` |

The Structory download name says `v1.3.7`; its Modrinth version and embedded `fabric.mod.json` both say `1.3.17`. The two Moog's universal filenames say `1.21`, but their pinned Modrinth versions include `26.2`; the JARs declare Moog's Structure Lib requirements satisfied by `3.3.0`. Better Villages requires Library Ferret `5.0.0`, Fabric API, Java 25, and Fabric Loader `>=0.19.3`; the pinned pack satisfies them.

Modrinth metadata for all 39 previous mods and these six additions declares no incompatibility between them. Better Villages changes vanilla villages; MVS, MNS, and Structory add structures in their own namespaces. Their content may overlap in a world, but no hard conflict was found in metadata or JAR contents. New structures appear only in newly generated chunks. On external servers, the same world generation files and dependencies must be installed server-side; singleplayer uses the integrated server.

The archive has 54 pinned Modrinth downloads: 45 Fabric mod JARs (including the Structory datapack packaged as a mod), 8 resource packs, and 1 shader. One Iris override brings the managed path count to 55. The installer downloads original files from exact Modrinth CDN URLs and verifies SHA-512; no third-party JAR is embedded in the `.mrpack` or EXE. Structory is credited and linked above in accordance with the [Stardust Labs license](https://github.com/Stardust-Labs-MC/license/blob/main/license.txt).

Archive SHA-512: `2AC6213EF9DF36C8FFE7DFC36B90DBD4CE2CEB5EB740BCDFD95EDAD46AF0420ED6CF0959C134C99AE54D0BB972D39AD2E89C9AF4A7857C1A88D18E79F76C5A86`.

## Validation

Packwiz export, Release build, deterministic smoke checks, and a clean temporary install of all 54 downloads passed. The live install verified every SHA-512, Repair, Uninstall, and preservation of user settings and worlds. The installer UI shows Vanilla 2 Plus `0.12.0`, 45 mods, and the new catalog category in Russian. The user confirmed that the new mods work in Minecraft. In the active `0.12.0` instance, `latest.log` also reports failed loads of three vanilla village templates overridden by Better Villages (`desert_meeting_point_3`, `savanna_small_house_6`, `plains_small_house_4`) with invalid NBT tag IDs. The game kept running without a crash report, but these village pieces remain an unresolved world-generation issue; this archive remains a candidate.

Previous `0.11.0` archive SHA-512: `486B7B92266FA3C0ED7F88CBD99ED572BA71FB9902058889593A5FACDC08F29DBCD9DFF185A7BCCDB5001F3E2DC8165399958F721613188046591E1DCCF329F2`. That archive remains unchanged and was previously tested in game with all five Macaw's mods.
