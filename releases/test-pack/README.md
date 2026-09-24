# Test pack release 0.1.0

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

The Fabric Installer CLI is available as version `1.1.2` from the official Fabric Maven repository, SHA-256 `61E035BF7BF70153E127440CE34DE47C9036F0A2D0C65D1529454BD35CEEFE4F`. The app does not edit Launcher files: the installed CLI's documented help exposes no Game Directory option, and the live Launcher could not be safely inspected on this machine. After the app installs the files, follow **Инструкция Launcher** to create a new dedicated MinePack profile for the pinned Fabric version and set the dedicated Game Directory manually; do not modify an existing profile. The live Launcher profile and Minecraft gameplay have not been tested on this machine.
