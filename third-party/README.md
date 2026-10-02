# Local third-party source kit

Prepared 2026-10-02 for the Frontier 0.19.7 candidate. This directory provides corresponding source/build snapshots for seven MinePack YUNG's forks and eight versioned source snapshots: the seven historical `.minepack.1` inputs plus the Desert Temples `.minepack.2` candidate. The source kit is included in this repository and the local audit-candidate bundle; this is not a statement of completed legal review.

## Source to artifact mapping

`yungs-sources/source-snapshot-manifest.txt` records each included relative path, byte length, and SHA-256; its SHA-256 is `FABD411EA235BAD90AE1ADA128A8F2623E8E774202A0F2A86C958DF6171005CE`. The manifest is bundled with the source kit so a recipient can verify all included source/build/license files offline. The source projects retain their local Minecraft 26.2 port edits and pinned dependency metadata; their upstream HEAD alone is not the source snapshot.

| Source snapshot | Upstream HEAD | Pinned artifact entry |
|---|---|---|
| `source-yungs-api` | `a84778d03087a02ae8f5ae74db10827cb682bc22` | `YungsApi-26.2-Fabric-6.1.3-minepack.1.jar` |
| `source-yungs-desert-temples` | `0b0f1e1c399dba1c9731946242f7e4ae9dec365a` | `YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.1.jar` |
| `source-yungs-desert-temples-minepack2` | same source snapshot, MinePack modification below | `YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar` |
| `source-yungs-better-dungeons` | `631719bfeca523773c01e10016b3361567c44a25` | `YungsBetterDungeons-26.2-Fabric-6.1.1-minepack.1.jar` |
| `source-yungs-better-jungle-temples` | `b4bd31d18ae8073221fdfddc29e1127372b12ac6` | `YungsBetterJungleTemples-26.2-Fabric-4.1.1-minepack.1.jar` |
| `source-yungs-better-mineshafts` | `89399904d42d059e31dd3e0bdf3ab7d3560fab8a` | `YungsBetterMineshafts-26.2-Fabric-6.1.1-minepack.1.jar` |
| `source-yungs-better-fortresses` | `ab41a3ca95801e5cb5e4ac3481c318cbf398acc4` | `YungsBetterNetherFortresses-26.2-Fabric-4.1.1-minepack.1.jar` |
| `source-yungs-better-strongholds` | `7da28e7054cfc9cec33ff2b85de89c5ec938deca` | `YungsBetterStrongholds-26.2-Fabric-6.1.1-minepack.1.jar` |

The Desert Temples `.minepack.2` snapshot changes only `gradle.properties` to `5.1.1-minepack.2` and `Common/src/main/resources/data/betterdeserttemples/advancement/temple_clear.json`: `predicate.type` becomes `predicate.entity_type`; the husk and Pharaoh profile condition, advancement identity, parent, and criteria remain the same. The pinned `.minepack.1` source snapshot and archive are retained unchanged.

## Build notes

Use JDK 25, Gradle 9.4.1, the project-declared Fabric Loom 1.15.5, and the project Gradle files in the selected snapshot. Do not run publish or upload tasks. The sibling `yungsapi-local-api-marker` directory is intentionally marker-only: each dependent project's `settings.gradle` maps `:yungsapi` to that sibling so its declared local `files(...)` API JAR inputs are used. It is not a composite API source project.

The exact `.minepack.1` binary hashes and the generated Desert Temples `.minepack.2` SHA-512 `FE84BFAAC034F1BA3E234F6875D29469A7762AC56D0340CE8136BD6B8C90DDF04E2A0A1BBB7ACE2F51579F4BB6E9644269F5C7AA9DCC6DA0DC4FCFF7EDEA7D9D` are recorded with their exact source paths in `yungs-sources/artifact-mapping.txt`; `source-snapshot-manifest.txt` hashes every included source/build/license file. The six historical Desert/other mod binaries are not rebuilt for the candidate; only the Desert `.2` source snapshot is compiled.

For a source-kit rebuild, copy the seven `source-yungs-*` directories and `yungsapi-local-api-marker` to a writable scratch directory, retaining their sibling names. The accepted API build is `:Fabric:jar :Common:jar`; copy its exact `YungsApi-26.2-Fabric-6.1.3-minepack.1.jar` and `YungsApi-26.2-Common-6.1.3-minepack.1.jar` outputs into each dependent project's `Fabric/libs/` and `Common/libs/`. Build dependent snapshots with `:Fabric:jar`. The Desert `.minepack.2` candidate uses `source-yungs-desert-temples-minepack2` and produces `YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar`.

The accepted local verification used JDK `25.0.4.1+1`, the installed Gradle `9.4.1` distribution, the project-pinned Loom `1.15.5`, and a seeded cache with `--offline --no-daemon --configure-on-demand`. Its command shape is:

```powershell
$env:JAVA_HOME = '<installed JDK 25 directory>'
$env:GRADLE_USER_HOME = '<Gradle cache directory with the declared pinned dependencies>'
$gradle = '<installed Gradle 9.4.1 bin/gradle.bat>'
$emptyPublishing = @('-PcurseforgeApiKey=', '-PmodrinthToken=', '-PossrhToken=', '-PossrhTokenPassword=')
& $gradle --offline --no-daemon --configure-on-demand -p '<scratch>/source-yungs-api' ':Fabric:jar' ':Common:jar' @emptyPublishing
```

For each dependent project substitute its matching scratch path and run `:Fabric:jar`; the API output copy step above is required first. Use `source-yungs-desert-temples-minepack2` for the new Desert binary. `--offline` requires that the pinned build dependencies are already cached; the test cache and tool distributions are intentionally not included in this source kit. Passing the four empty publishing properties avoids using any publishing credentials; do not run publish/upload tasks.

Accepted binary comparison found identical class/resource payloads for the seven historical JARs; their sole difference was `Fabric-Gradle-Version: 9.2.0` → `9.4.1` in the manifest. This is evidence for the checked local builds, not a claim of bit-for-bit reproducibility.

## Notices and access

The original per-project `LICENSE` files and full GNU LGPL-3.0 and GPL-3.0 texts are included. MinePack's 2026-10-02 modification is the Desert Temples advancement codec field and version change described above. Preserve existing copyright, attribution, and license notices when redistributing source or binaries. No credentials, global Gradle settings, caches, wrapper distributions, generated Minecraft sources, or private documents are part of this kit. The corresponding sources are provided here; a new installer GitHub Release and any final legal review remain separate work.
