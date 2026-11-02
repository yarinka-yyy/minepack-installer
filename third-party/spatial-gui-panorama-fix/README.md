# Spatial GUI panorama fix

MinePack `1.9.1+minepack.1` retains the exact Fabric 26.2 Spatial GUI 1.9.1
release and backports only the author's panorama completion fix: the
`spatialGUI$onPanoramaCaptureEnd` injection changes `TAIL` to `RETURN`.
This also executes on early returns introduced by panorama mods and prevents
Spatial GUI from staying disabled after panorama capture.

- Upstream issue: https://github.com/tastytrash/Spatial-GUI/issues/173
- Upstream fix: https://github.com/tastytrash/Spatial-GUI/commit/a1ba5b8aaf9499ff4f503ad9e8ec2dac4e71583d
- Original version: https://modrinth.com/mod/spatial-gui/version/IwoDj3xl
- Input SHA-512 is enforced by `Build.java`.
- License: MIT, retained in `LICENSE.txt` and inside the resulting JAR.

The upstream commit's unrelated hand-animation features are not included.
No Minecraft classes, caches, accounts or launcher state are used by this build.
Use JDK 25 and the existing ASM 9.10.1 dependency from Fabric's launcher libraries:

```powershell
java -cp '<asm-9.10.1.jar>' Build.java '<spatial-gui-1.9.1+26.2-fabric.jar>' '<new-spatial-gui-1.9.1+minepack.1+26.2-fabric.jar>' LICENSE.txt
```

The output path must not exist. This source-mode build adds no runtime dependency;
all unchanged JAR entries retain their original bytes. Pack smoke checks compare
the new release against its previous immutable archive and require this fixed JAR.
Game rendering and repeated world-exit/re-entry remain manual acceptance checks.
