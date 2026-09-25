# MinePack Installer

Windows WPF prototype for installing one pinned Fabric test pack into an isolated Minecraft game directory and creating its own official Launcher profile. This repository is a development prototype, not a player-ready public release.

## Запуск

Откройте `artifacts/publish-0.8.0/MinePack.Installer.exe`, оставив рядом всю папку `artifacts/publish-0.8.0`. Закройте Minecraft Launcher, нажмите **Установить сборку** и дождитесь сообщения **Сборка готова**. Установщик скачает закреплённые моды, шейдер и ресурспаки, проверит их и переключит профиль **MinePack** на новый отдельный Game Directory. Предыдущая папка сборки `0.7.0` и её мир останутся на диске. Затем откройте официальный Minecraft Launcher, выберите этот профиль и нажмите **Играть**. Для первого запуска Launcher сам загрузит базовые файлы Minecraft и библиотеки Fabric; вход в аккаунт остаётся в официальном Launcher.

Новая установка заранее меняет бег на левый Shift, приседание на левый Ctrl, поле зрения на 80, режим экрана на Exclusive и масштаб интерфейса на 4×. Разрешение монитора не фиксируется. Проверка и исправление сборки не перезаписывает последующие настройки игрока.
Для Minecraft `26.2` начальный `options.txt` содержит `version:4903`: без версии игра пытается мигрировать новые названия клавиш как старые числовые коды и сбрасывает весь файл, включая выбор ресурспаков.

Тестовый кандидат `0.8.0` остаётся на Minecraft `26.2` и сохраняет все компоненты проверенной вами сборки `0.7.0`. Добавлен Punchy! `2.8a` с анимациями от первого лица; его встроенный ресурспак включается в новом профиле вместе с прежними восемью. При Repair выбор ресурспаков игрока не перезаписывается. Чистая установка, запуск игры, начальные настройки и выбор ресурспаков проверены пользователем. Миры `26.3` не открывайте в `26.2` без резервной копии.

Если сборка скачалась, а профиль создать не удалось, исправьте причину в сообщении и нажмите **Восстановить профиль Launcher**. Повторная загрузка мода не требуется. По умолчанию данные сборки находятся в `%LOCALAPPDATA%\MinePack`.

Если после первого запуска появилась прежняя ошибка `FABRIC_VERSION_CONFLICT`, откройте обновлённый EXE из этой папки и нажмите **Восстановить профиль Launcher**. Официальный Launcher должен быть закрыт. Исправленный установщик распознаёт клиентский JAR, которым Launcher заполняет Fabric-профиль после запуска, и не удаляет его.

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
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish-0.8.0
```

The pinned Packwiz source is exported with the portable tool (run from `pack/test-pack`):

```powershell
& 'C:\Users\Yarin\AppData\Local\Temp\minepack-tools\packwiz\packwiz.exe' --cache ../../artifacts/packwiz-cache modrinth export -o ../../releases/test-pack/test-pack-0.8.0.mrpack
```

## Pack and Fabric tools

- Packwiz: portable executable from the official Packwiz GitHub Actions run `34043101039`, source revision `ef87d964f8cbd52b3b13ea42453ef322290e2b9e`; downloaded ZIP SHA-256 `C59CD1AB7B8FB6A09CD00CC9BECBCA5467731CA2DA37C14E8166CFE360FCB31E`. It is kept outside this repository and invoked by full path.
- Fabric Installer CLI `1.1.2` was inspected during research, but the application uses the official Fabric Meta API profile ZIP directly. The pinned ZIP SHA-512 is in `FabricLauncherService.cs`; the installer verifies it before touching Launcher files.

The test pack is pinned to Minecraft `26.2` and Fabric Loader `0.19.5`. Its exact Modrinth project/version IDs are recorded in `releases/test-pack/README.md`; source URLs and file hashes are pinned in `pack/test-pack`, and the `.mrpack` SHA-512 is recorded in the release README. Mod JARs and pack ZIPs are downloaded from Modrinth CDN after SHA-512 verification; they are not bundled in the app.

Install stages and verifies files before marking the instance active. Repair checks managed files and reconfigures the MinePack profile; uninstall removes managed files and only that profile, preserving worlds, screenshots, unknown user data, and shared Fabric versions. If the profile cannot be removed after the pack files, **Удалить сборку** can be run again to remove only the remaining MinePack profile. Launcher profile changes require the Launcher to be closed. The installer handles one detected official profile file (`launcher_profiles.json` or `launcher_profiles_microsoft_store.json`); if both are present, it stops instead of guessing. The live Launcher and gameplay still need a manual acceptance run. Automated checks use isolated profile fixtures and never write to the user's Launcher.
