# Test pack releases

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

Iris and Voxy both require this exact Sodium version in Modrinth metadata. Fabric API is required by Voxy and Chunky. Chunky is included for optional pre-generation of a new world; C2ME is not required and is deferred until the basic stack is tested. Complementary Reimagined contains Voxy integration code, but live shader rendering and FPS must be checked in Minecraft. Voxy requires OpenGL 4.6. Release SHA-512: `9B50156730A5A17E264B4EA0B5AD0BE594CF426CB46BB54D4AF16E4960F1B55222B7F22C873135BBDF79E9BE8C05B069C0D44396DDD8DD8A77A5D649DBE57E84`.

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
