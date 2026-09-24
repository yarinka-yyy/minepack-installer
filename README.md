# MinePack Installer

Windows WPF prototype for installing one pinned Fabric test pack into an isolated Minecraft game directory. This repository is a development prototype, not a player-ready public release.

## Layout

- `src/MinePack.Installer` — WPF application.
- `src/MinePack.Core` — archive validation, verified downloads, install state, repair, and managed-file removal.
- `tests/MinePack.Smoke` — dependency-free runnable checks.
- `pack/test-pack` — Packwiz source for the test pack.
- `releases/test-pack` — exported immutable `.mrpack` artifact and its metadata.

## Build and checks

Run from this directory with the .NET 10 SDK on Windows:

```powershell
dotnet build MinePack.slnx -c Release
dotnet run --project tests/MinePack.Smoke -c Release
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish
```

The pinned Packwiz source is exported with the portable tool (run from `pack/test-pack`):

```powershell
& 'C:\Users\Yarin\AppData\Local\Temp\minepack-tools\packwiz\packwiz.exe' modrinth export -o ../../releases/test-pack/test-pack-0.1.0.mrpack
```

## Pack and Fabric tools

- Packwiz: portable executable from the official Packwiz GitHub Actions run `34043101039`, source revision `ef87d964f8cbd52b3b13ea42453ef322290e2b9e`; downloaded ZIP SHA-256 `C59CD1AB7B8FB6A09CD00CC9BECBCA5467731CA2DA37C14E8166CFE360FCB31E`. It is kept outside this repository and invoked by full path.
- Fabric Installer CLI: version `1.1.2`, downloaded from the official Fabric Maven repository; JAR SHA-256 `61E035BF7BF70153E127440CE34DE47C9036F0A2D0C65D1529454BD35CEEFE4F`.

The test pack is intentionally small and pinned: Minecraft `26.3`, Fabric Loader `0.19.5`, and the single Fabric API release `0.160.7+26.3`. Its exact Modrinth project/version IDs, source URL, SHA-512, license, and `.mrpack` SHA-512 are recorded in `releases/test-pack/README.md`. No mod JAR is redistributed in the app.

The app stores data by default under `%LOCALAPPDATA%\MinePack`, with each release in a versioned `instances` directory. Install stages and verifies files before marking the instance active. Repair checks only managed files; uninstall removes only those files and preserves worlds, screenshots, and unknown user data. The app does not write Launcher profiles. After installation, use **Инструкция Launcher** to create a new dedicated MinePack profile and set its Game Directory manually; do not modify an existing profile. The live Launcher could not be safely inspected on this machine, so Launcher and gameplay checks remain not run.
