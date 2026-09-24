# MinePack Installer

Windows WPF prototype for installing one pinned Fabric test pack into an isolated Minecraft game directory and creating its own official Launcher profile. This repository is a development prototype, not a player-ready public release.

## Запуск

Откройте `artifacts/publish-0.3.0/MinePack.Installer.exe`, оставив рядом всю опубликованную папку `artifacts/publish-0.3.0`. Закройте Minecraft Launcher, нажмите **Установить сборку** и дождитесь сообщения **Сборка готова**. Установщик скачает закреплённые Fabric API, Iris, Sodium, Voxy, Chunky и Complementary Reimagined, проверит их, настроит шейдер и создаст отдельный профиль **MinePack Test Pack** с собственным Game Directory. Затем откройте официальный Minecraft Launcher, выберите этот профиль и нажмите **Играть**. Для первого запуска Launcher сам загрузит базовые файлы Minecraft и библиотеки Fabric; вход в аккаунт остаётся в официальном Launcher.

Релиз `0.3.0` использует Minecraft `26.2`, потому что официального Voxy для `26.3` пока нет. Сначала создайте **новый мир 26.2**. Миры, уже сохранённые в `26.3`, не открывайте в `26.2`: переход на старую версию может повредить данные. Voxy показывает дальние области, которые уже были исследованы или импортированы. Для первого теста начните с обычной дальности 8–12 чанков и Voxy 64 чанка. В новом мире можно открыть чат и выполнить `/chunky radius 1024`, затем `/chunky start`, дождаться конца генерации и выполнить `/voxy import current`. Радиус Chunky задаётся в блоках: 1024 блока — это 64 чанка. C2ME в первом тесте нет: он ускоряет генерацию, но не требуется для работы Voxy и Chunky. Voxy требует OpenGL 4.6; частота кадров зависит от ПК, мира и настроек шейдера.

Если сборка скачалась, а профиль создать не удалось, исправьте причину в сообщении и нажмите **Настроить Launcher**. Повторная загрузка мода не требуется. По умолчанию данные сборки находятся в `%LOCALAPPDATA%\MinePack`.

Если после первого запуска появилась прежняя ошибка `FABRIC_VERSION_CONFLICT`, откройте обновлённый EXE из этой папки и нажмите **Настроить Launcher**. Официальный Launcher должен быть закрыт. Исправленный установщик распознаёт клиентский JAR, которым Launcher заполняет Fabric-профиль после запуска, и не удаляет его.

Чтобы перенести миры, сначала установите новую сборку, закройте Minecraft, нажмите **Импортировать миры** и выберите папку `saves` прежнего профиля (например `%APPDATA%\.minecraft\saves`). Установщик копирует каждый мир целиком; исходные миры не удаляются, совпадающие имена пропускаются. Перед открытием старого мира в Minecraft другой версии сохраните его резервную копию: сама игра может изменить формат мира.

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
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish-0.3.0
```

The pinned Packwiz source is exported with the portable tool (run from `pack/test-pack`):

```powershell
& 'C:\Users\Yarin\AppData\Local\Temp\minepack-tools\packwiz\packwiz.exe' --cache ../../artifacts/packwiz-cache modrinth export -o ../../releases/test-pack/test-pack-0.3.0.mrpack
```

## Pack and Fabric tools

- Packwiz: portable executable from the official Packwiz GitHub Actions run `34043101039`, source revision `ef87d964f8cbd52b3b13ea42453ef322290e2b9e`; downloaded ZIP SHA-256 `C59CD1AB7B8FB6A09CD00CC9BECBCA5467731CA2DA37C14E8166CFE360FCB31E`. It is kept outside this repository and invoked by full path.
- Fabric Installer CLI `1.1.2` was inspected during research, but the application uses the official Fabric Meta API profile ZIP directly. The pinned ZIP SHA-512 is in `FabricLauncherService.cs`; the installer verifies it before touching Launcher files.

The test pack is intentionally small and pinned: Minecraft `26.2`, Fabric Loader `0.19.5`, Fabric API, Iris, Sodium, Voxy, Chunky, and Complementary Reimagined. Its exact Modrinth project/version IDs, source URLs, hashes, and `.mrpack` SHA-512 are recorded in `releases/test-pack/README.md`. No mod JAR or shader ZIP is redistributed in the app.

Install stages and verifies files before marking the instance active. Repair checks managed files and reconfigures the MinePack profile; uninstall removes managed files and only that profile, preserving worlds, screenshots, unknown user data, and shared Fabric versions. If the profile cannot be removed after the pack files, **Удалить сборку** can be run again to remove only the remaining MinePack profile. Launcher profile changes require the Launcher to be closed. The installer handles one detected official profile file (`launcher_profiles.json` or `launcher_profiles_microsoft_store.json`); if both are present, it stops instead of guessing. The live Launcher and gameplay still need a manual acceptance run. Automated checks use isolated profile fixtures and never write to the user's Launcher.
