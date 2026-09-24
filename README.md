# MinePack Installer

Windows WPF prototype for installing one pinned Fabric test pack into an isolated Minecraft game directory and creating its own official Launcher profile. This repository is a development prototype, not a player-ready public release.

## Запуск

Откройте `artifacts/publish/MinePack.Installer.exe`, оставив рядом всю опубликованную папку `artifacts/publish`. Закройте Minecraft Launcher, нажмите **Установить сборку** и дождитесь сообщения **Сборка готова**. Установщик скачает закреплённый мод, проверит его, добавит закреплённую Fabric version и создаст отдельный профиль **MinePack Test Pack** с собственным Game Directory. Затем откройте официальный Minecraft Launcher, выберите этот профиль и нажмите **Играть**. Для первого запуска Launcher сам загрузит базовые файлы Minecraft и библиотеки Fabric; вход в аккаунт остаётся в официальном Launcher.

Если сборка скачалась, а профиль создать не удалось, исправьте причину в сообщении и нажмите **Настроить Launcher**. Повторная загрузка мода не требуется. По умолчанию данные сборки находятся в `%LOCALAPPDATA%\MinePack`.

## Layout

- `src/MinePack.Installer` — WPF application.
- `src/MinePack.Core` — archive validation, verified downloads, Fabric/Launcher integration, install state, repair, and managed-file removal.
- `tests/MinePack.Smoke` — dependency-free runnable checks.
- `pack/test-pack` — Packwiz source for the test pack.
- `releases/test-pack` — exported immutable `.mrpack` artifact and its metadata.

## Build and checks

Run from this directory with the .NET 10 SDK on Windows:

```powershell
dotnet build MinePack.slnx -c Release
dotnet run --project tests/MinePack.Smoke -c Release
dotnet run --project tests/MinePack.Smoke -c Release -- --live-fabric
dotnet run --project tests/MinePack.Smoke -c Release -- --live-profile-copy
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish
```

The pinned Packwiz source is exported with the portable tool (run from `pack/test-pack`):

```powershell
& 'C:\Users\Yarin\AppData\Local\Temp\minepack-tools\packwiz\packwiz.exe' modrinth export -o ../../releases/test-pack/test-pack-0.1.0.mrpack
```

## Pack and Fabric tools

- Packwiz: portable executable from the official Packwiz GitHub Actions run `34043101039`, source revision `ef87d964f8cbd52b3b13ea42453ef322290e2b9e`; downloaded ZIP SHA-256 `C59CD1AB7B8FB6A09CD00CC9BECBCA5467731CA2DA37C14E8166CFE360FCB31E`. It is kept outside this repository and invoked by full path.
- Fabric Installer CLI `1.1.2` was inspected during research, but the application uses the official Fabric Meta API profile ZIP directly. The pinned ZIP SHA-512 is in `FabricLauncherService.cs`; the installer verifies it before touching Launcher files.

The test pack is intentionally small and pinned: Minecraft `26.3`, Fabric Loader `0.19.5`, and the single Fabric API release `0.160.7+26.3`. Its exact Modrinth project/version IDs, source URL, SHA-512, license, and `.mrpack` SHA-512 are recorded in `releases/test-pack/README.md`. No mod JAR is redistributed in the app.

Install stages and verifies files before marking the instance active. Repair checks managed files and reconfigures the MinePack profile; uninstall removes managed files and only that profile, preserving worlds, screenshots, unknown user data, and shared Fabric versions. If the profile cannot be removed after the pack files, **Удалить сборку** can be run again to remove only the remaining MinePack profile. Launcher profile changes require the Launcher to be closed. The installer handles one detected official profile file (`launcher_profiles.json` or `launcher_profiles_microsoft_store.json`); if both are present, it stops instead of guessing. The live Launcher and gameplay still need a manual acceptance run. Automated checks use isolated profile fixtures and never write to the user's Launcher.
