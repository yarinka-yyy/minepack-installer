# План 003: Автоматизировать цикл Minecraft Launcher и очистить старые локальные артефакты

> **Исполнителю:** Работай из `C:\Users\Yarin\Documents\MinePack Installer\app`. Сначала прочитай `..\AGENTS.md`, этот план, `plans/README.md` и разделы 5, 7, 8 и 10 `..\doc\minecraft_modpack_installer_spec.md`. Делай блоки по порядку, проверяй каждый шаг. Не делай commit/push, не публикуй ZIP или GitHub Release. Пользователь разрешил запускать установленный у него Minecraft Launcher и тестировать установку, но не разрешал менять vanilla-миры, учётную запись, чужие профили или удалять реальные игровые данные. Основной агент проверит diff и результаты.
>
> **Drift check:** `git rev-parse HEAD; git status --short; git diff -- src/MinePack.Core/FabricLauncherService.cs src/MinePack.Installer/MainWindow.xaml.cs src/MinePack.Core/Resources/Strings.resx src/MinePack.Core/Resources/Strings.ru.resx README.md README.ru.md`. На дату плана HEAD `93d1a21e4c884e51dbc0ed728426358f373e773b`; незакоммиченный кандидат пакета `0.9.0` уже меняет `MainWindow.xaml.cs`, ресурсы и README. Сохраняй эти изменения. Если текущая логика или состав пакета перестали совпадать с описанием ниже, остановись и сообщи о дрейфе; не откатывай чужой worktree.

## Статус

- **Статус:** DONE — код, smoke, publish и согласованная локальная очистка выполнены; живой Store/Xbox Launcher закрыт и повторно открыт при установке в отдельный каталог. Классический Win32 остаётся проверенным только smoke-тестами, поскольку на этой машине его нет.
- **Приоритет:** P1. **Объём:** средний. **Риск:** высокий для живого Launcher и разрушительной очистки локальных сборок.
- **Зависит от:** 002 (DONE), локального кандидата пакета `0.9.0` как базового содержимого. **Категории:** launcher integration, UX, tests, local cleanup.
- **Основание:** commit `93d1a21e4c884e51dbc0ed728426358f373e773b`, 2026-09-25; `app/` — Git-корень; `artifacts/` исключён из Git. Следующий publish — **новая сборка приложения с тем же закреплённым пакетом 0.9.0**, без произвольного повышения версии модпака.

## Цель и границы

После нажатия **«Установить сборку»** MinePack без дополнительного подтверждения определяет официальный Minecraft Launcher, если он работает — штатно закрывает, ждёт фактического завершения, лишь затем начинает загрузку/установку. Перед записью профиля повторно проверяет, что Launcher не запущен. После успешной установки файлов **и** профиля автоматически открывает найденный официальный Launcher. Игру не запускает и профиль MinePack не выбирает вместо пользователя. Ошибка открытия Launcher не отменяет успешную установку: UI даёт понятную ручную инструкцию. При отказе Launcher закрыться установка не начинается; не применять принудительный `Kill`.

После проверки новой сборки оставить в `app/artifacts` ровно четыре **основные publish-копии**: новую `publish-launcher-auto`, `publish-0.9.0`, `publish-0.8.0`, `publish-0.7.0`. Удалить старые локальные publish-копии и подтверждённые временные результаты старых тестов. Не удалять исходники, закреплённые `.mrpack` в `app/releases`, Packwiz-проект, историю Git или данные `%LOCALAPPDATA%\MinePack`/`%APPDATA%\.minecraft`. Старые `.mrpack` **внутри сохраняемых publish-папок** остаются: текущий код использует их для Repair/Uninstall прежних установок. Отдельный пользовательский ZIP пока не создавать.

## Исследованные варианты официального Launcher

- [Minecraft Help](https://help.minecraft.net/hc/en-us/articles/23430683774221) перечисляет установку через Microsoft Store, Xbox App и Minecraft.net; загрузка через Minecraft.net на современных Windows всё равно использует учётную запись Microsoft Store. [Официальные альтернативные загрузки](https://www.minecraft.net/en-us/download/alternative.In) также содержат Windows 7/8 MSI/EXE. На поддерживаемой системе учитывать современный зарегистрированный Store-вариант и установленный классический Win32-вариант; сайт, Store и Xbox не считать тремя независимыми расположениями EXE.
- Для пакетного приложения Windows идентификатор запуска — AUMID, а не путь в `WindowsApps`. Microsoft описывает [поиск AUMID](https://learn.microsoft.com/en-us/windows/configuration/store/find-aumid) и [запуск через `shell:AppsFolder\<AUMID>`](https://learn.microsoft.com/en-us/windows/apps/distribute-through-store/how-to-transition-users-from-your-web-unpackaged-app-to-store-packaged-app). Определять **зарегистрированный** AUMID на машине, не фиксировать путь с версией пакета и не запускать произвольный `Minecraft.exe`.
- Для классического варианта использовать зарегистрированный официальный Start Menu shortcut/установочный путь или сохранённый путь **проверенного** работающего процесса. Не искать случайные `Minecraft*.exe` по всем дискам. Если официальный переносимый EXE лежит в произвольной папке и не запущен/не зарегистрирован, универсально обнаружить его невозможно: показывать точную инструкцию для ручного запуска; не обещать поддержку неразличимого файла.
- .NET [`CloseMainWindow`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.closemainwindow?view=net-10.0) посылает обычный запрос закрытия, но не гарантирует выход; [`WaitForExit`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=net-10.0) с конечным таймаутом подтверждает завершение. Не путать закрытие окна с завершением процесса.

## Текущее состояние и опорные файлы

- `src/MinePack.Installer/MainWindow.xaml.cs:131-139`: `Operation.Install` вызывает `_launcher.CheckReady()` перед загрузкой и затем `InstallService.InstallAsync` либо `RepairAsync` для уже активной той же версии. `:176-183` вызывает `ConfigureLauncherAsync` после успеха. `:268-283` показывает «Откройте официальный Minecraft Launcher», но не запускает его. Автостарт относится только к нажатию **«Установить сборку»**, включая ветку Repair от этой кнопки; кнопки Repair, Restore profile, Import и Uninstall не получают нового автостарта.
- `src/MinePack.Core/FabricLauncherService.cs:41-46,149-153`: `CheckReady()` ищет один JSON-профиль и проверяет процессы по подстроке `MinecraftLauncher`; при открытом Launcher выбрасывает `LAUNCHER_RUNNING`. `ConfigureAsync` снова вызывает `CheckReady()` перед сетевым запросом, но после скачивания до `UpdateProfile` Launcher может открыться. `RemoveOwnProfile` тоже требует закрытого Launcher. Не заменять безопасность записи профиля на один ранний check.
- `FabricLauncherService.cs:137-146` допускает ровно один `launcher_profiles.json` или `launcher_profiles_microsoft_store.json`; два файла считаются неоднозначностью. `LauncherProfile.cs` меняет только собственный профиль, а `UpdateProfile` сохраняет резервную копию и отвергает конкурентную правку. Сохранить эти гарантии. Если оба официальных Launcher установлены одновременно, соотнести обнаруженный активный/выбранный вариант с файлом профиля; при неоднозначности остановиться, не угадывать.
- `src/MinePack.Core/Resources/Strings.resx` и `Strings.ru.resx` содержат UI/ошибки на EN/RU, включая `UiRequirements`, `LauncherRunning`, `LaunchInstruction`; `MainWindow.xaml` использует встроенный Monocraft. Новые строки добавлять парно в существующие ресурсы, стиль/шрифт не менять.
- `README.md:13-16` и `README.ru.md:13-16` требуют закрыть Launcher вручную и открыть вручную. Исправить только эти шаги после реализации. В `../doc/minecraft_modpack_installer_spec.md:150-154` зафиксирована текущая ручная проверка; обновить раздел 5, сохранив ограничения для профиля и данных.
- `tests/MinePack.Smoke/Program.cs` уже содержит изолированные Launcher fixtures и общую команду `dotnet run --project tests/MinePack.Smoke -c Release`; тесты не должны закрывать настоящий Launcher. `src/MinePack.Installer/MinePack.Installer.csproj` публикует все закреплённые `.mrpack`, включая старые; их нельзя выборочно убрать из сохраняемых build-копий без изменения поддержки старых установок.
- Снимок `artifacts/` на 2026-09-25: `publish-0.2.0`…`publish-0.6.0` — по ~139.5 MB; `publish`, `publish-final`, `publish-v2`, `review-publish` содержат только `test-pack-0.1.0.mrpack` и являются старыми publish-копиями; `publish-0.7.0`, `publish-0.8.0`, `publish-0.9.0` — нужные копии. `publish-ui-preview-0.8.0` и `publish-ui-preview-compact` — дубли предварительного UI, не основные publish-копии. `compat-review-0.6` — старый тестовый набор; `candidate-0.9.0`, `compat-review-0.7`, `compat-review-0.8`, `packwiz-cache`, `packwiz-tool`, скачанные JAR/ZIP, скриншоты и `UiCapture` требуют классификации перед удалением. Не удалять их по одному лишь имени.
- В доступном текущем сеансе найден `launcher_profiles.json`, но `Get-StartApps` и стандартные пути EXE не показали зарегистрированный Launcher, Store-пакет не обнаружен. Это **не** доказательство отсутствия Launcher у пользователя; не зашивать на основании этого снимка путь запуска. Живую проверку выполнить в пользовательском desktop-сеансе.

## Разрешённые изменения

- Код: `app/src/MinePack.Core/FabricLauncherService.cs`, новый минимальный помощник обнаружения/управления Launcher в `app/src/MinePack.Core/` **или** `app/src/MinePack.Installer/` (выбрать одно место), `app/src/MinePack.Installer/MainWindow.xaml.cs`, при необходимости `MainWindow.xaml`, существующие `Strings.resx`/`Strings.ru.resx`.
- Проверки: `app/tests/MinePack.Smoke/Program.cs`; только если проверка требует отдельной fixture, добавить её в `app/tests/MinePack.Smoke/`.
- Документация: `app/README.md`, `app/README.ru.md`, `../doc/minecraft_modpack_installer_spec.md` (только раздел Launcher), этот план и `plans/README.md` для статуса.
- Локальные результаты: новый `app/artifacts/publish-launcher-auto/`; подтверждённые кандидаты на удаление в `app/artifacts/`, перечисленные в блоке B. Другие каталоги вне `artifacts/` не чистить.
- За пределами: состав пакета/версии/хэши, `pack/test-pack/`, `app/releases/`, `TestPackRelease.cs`, алгоритм загрузки модов, учётные данные Launcher, vanilla-данные и пользовательские миры, системные настройки, публикация ZIP/GitHub Release.

## Блок A. Автоматический цикл Launcher

### Шаг 1. Обнаружить официальный вариант и зафиксировать цель повторного запуска

На Windows различать зарегистрированный Store/Xbox/Minecraft.net вариант (AUMID) и классический Win32 (зарегистрированный ярлык/путь); использовать доступные платформенные данные, не добавлять NuGet-зависимость и не вызывать PowerShell из приложения. Не считать одно имя процесса достаточным доказательством официального Launcher: сопоставить процесс с обнаруженной регистрацией/путём, не затрагивать игру `javaw.exe` и сторонние лаунчеры. Если Launcher уже открыт, сохранить его подтверждённую цель запуска; иначе обнаружить зарегистрированный Launcher для открытия после установки. Проверить единственность файла профилей, но не определять тип Launcher по его имени: живой Store/Xbox Launcher пользователя использует `launcher_profiles.json`. Если найдено несколько целей без единственной активной, остановиться до загрузки.

**Проверка:** `dotnet build src/MinePack.Installer -c Release` → код 0. В smoke-fixtures (шаг 4) видны обе модели цели — AUMID и Win32 — без обращения к реальным процессам.

### Шаг 2. Закрыть работающий Launcher до загрузки и повторно проверить перед записью профиля

Только путь `Operation.Install`: определить все принадлежащие выбранному официальному Launcher процессы, вызвать штатное закрытие, асинхронно ждать их выхода с ограниченным временем и поддержкой отмены. После ожидания повторно перечислить процессы, чтобы не пропустить перезапуск/дочерний процесс. Если закрытие не удалось, завершить операцию **до** `_installer.InstallAsync`/`RepairAsync` с локализованной ошибкой, без `Kill`, без подтверждающего диалога и без изменения профиля. Если Launcher открылся во время загрузки, повторно закрыть перед `UpdateProfile` либо безопасно остановить только настройку профиля с существующей возможностью Restore; не записывать профиль поверх работающего Launcher. Оставить атомарную запись/backup и проверку изменившегося JSON.

**Проверка:** `dotnet run --project tests/MinePack.Smoke -c Release` → fixture показывает порядок `close → confirmed exit → first install action`, отказ/таймаут не вызывает install, повторный запуск до записи профиля не проходит незамеченным.

### Шаг 3. Открыть Launcher только после успешного завершения установки и профиля

После успешного `ConfigureLauncherAsync` на пути `Operation.Install` открыть сохранённую цель: Store через зарегистрированный AUMID/Windows shell, классический Launcher через проверенный ярлык/EXE. Проверить, что запрос запуска не дал немедленной ошибки; не считать мгновенный возврат `explorer.exe` доказательством, что окно реально появилось. При ошибке открытия оставить результат установки успешным, показать локализованное «сборка готова, откройте Launcher вручную» и диагностический код. Не выбирать профиль и не нажимать «Играть» автоматически. Переписать соответствующие шаги `README.md`/`README.ru.md` и раздел 5 спецификации под новый UX. Не трогать подсказки для Repair/Uninstall, если они всё ещё требуют ручного закрытия.

**Проверка:** `dotnet run --project tests/MinePack.Smoke -c Release` → открытие вызывается ровно после успеха файлов и профиля; не вызывается после ошибки, отмены, Repair/Uninstall и при Restore profile; EN/RU сообщения существуют.

### Шаг 4. Проверить обе установки и живой пользовательский сценарий

В `tests/MinePack.Smoke/Program.cs` оставить **одну минимальную запускаемую группу** с подменой только процесса/launch-цели, покрывающую: Launcher отсутствует/уже закрыт; работает Store; работает Win32; задержка/отказ закрытия; повторное открытие до записи профиля; неудачный автозапуск при сохранённом успехе pack; неоднозначные профили. Не запускать реальный Launcher из автоматического smoke. Собрать новую копию в **новый пустой** `artifacts/publish-launcher-auto` командой `dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish-launcher-auto`.

Пользователь разрешил живой тест: в его desktop-сеансе запустить официальный Launcher, затем проверить установку/Repair через кнопку **«Установить сборку»** в отдельном MinePack install root или на существующей тестовой установке после снимка текущего состояния. Зафиксировать: найденную разновидность установки, отсутствие новых загрузок до закрытия процесса, завершение процесса, успешную установку/профиль, автопоявление Launcher, профиль MinePack и сохранность иных профилей. Отдельно проверить Store-вариант и классический Win32, когда они доступны; отсутствие одного варианта честно отметить как **непроверенный**, не объявлять полную совместимость. Не открывать миры и не удалять игровую установку ради теста.

**Проверка:** `dotnet run --project tests/MinePack.Smoke -c Release` и указанная `dotnet publish` → оба кода 0; `Test-Path -LiteralPath 'artifacts/publish-launcher-auto/MinePack.Installer.exe'` → `True`; живой тест имеет краткий протокол с наблюдаемыми процессом/профилем (PASS или точный блокер).

## Блок B. Очистка только локальных артефактов

### Шаг 5. Составить сухой перечень, доказать границы и сохранить четыре копии

В PowerShell из `app/` вывести имена/размеры первого уровня `artifacts/`; классифицировать все кандидаты. Обязательный keep-list: `publish-0.7.0`, `publish-0.8.0`, `publish-0.9.0`, новый `publish-launcher-auto`. Перед любой рекурсивной операцией получить абсолютные `Resolve-Path -LiteralPath` и убедиться, что каждый target — прямой потомок точного `Resolve-Path -LiteralPath 'artifacts'`, не reparse point, не пересекается с keep-list и не содержит работающий EXE. Проверить `rg`-ссылки из кода/документации на каждый дополнительный кандидат. Не удалять исходные `releases/*.mrpack` и копии старых `.mrpack` внутри четырёх сохранённых публикаций.

Кандидаты после повторной проверки состава: `publish`, `publish-final`, `publish-v2`, `review-publish`, `publish-0.2.0`…`publish-0.6.0`, `publish-ui-preview-0.8.0`, `publish-ui-preview-compact`, `compat-review-0.6`. `candidate-0.9.0` удалить только после успешной проверки новой publish-копии и подтверждения, что его содержимое не является единственным диагностическим материалом для 0.9.0. `compat-review`, `compat-review-0.7`, `compat-review-0.8`, `packwiz-cache`, `packwiz-tool`, скачанные JAR/ZIP, скриншоты, `preview-en-hook`, `UiCapture` оставить, пока не подтверждены происхождение/воспроизводимость и отсутствие текущих ссылок. Не применять wildcard-удаление.

**Проверка:** из `app/` выполнить сухую проверку ниже. Она только печатает цели; каждую напечатанную строку сверить с таблицей классификации, составленной выше. Все четыре keep-EXE дают `True`; до конца шага 5 ничего не удалено.

```powershell
$artifactRoot = (Resolve-Path -LiteralPath 'artifacts').Path
$keep = @('publish-launcher-auto','publish-0.9.0','publish-0.8.0','publish-0.7.0')
$old = @('publish','publish-final','publish-v2','review-publish',
  'publish-0.2.0','publish-0.3.0','publish-0.4.0','publish-0.5.0','publish-0.6.0',
  'publish-ui-preview-0.8.0','publish-ui-preview-compact','compat-review-0.6')
if (Get-Process -Name 'MinePack.Installer' -ErrorAction SilentlyContinue) { throw 'Close the running MinePack installer before cleanup.' }
$targets = foreach ($name in $old) {
  if ($keep -contains $name) { throw "Protected build: $name" }
  $item = Get-Item -LiteralPath (Join-Path $artifactRoot $name) -Force -ErrorAction Stop
  $resolved = (Resolve-Path -LiteralPath $item.FullName).Path
  if (-not $item.PSIsContainer -or $item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or
      -not [string]::Equals((Split-Path -Parent $resolved), $artifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe cleanup target: $resolved"
  }
  $resolved
}
$targets
$keep | ForEach-Object { Test-Path -LiteralPath (Join-Path $artifactRoot "$_\MinePack.Installer.exe") }
```

### Шаг 6. Удалить подтверждённые старые результаты и сверить остаток

После успешного блока A удалить **только** утверждённые в шаге 5 literal paths через PowerShell `Remove-Item -LiteralPath ... -Recurse` в том же shell и после той же проверки. Непосредственно после сухого вывода и ручной сверки в той же PowerShell-сессии выполнить `$targets | ForEach-Object { Remove-Item -LiteralPath $_ -Recurse -Force -ErrorAction Stop }`. Не передавать пути другому shell и не строить маску для удаления. Дополнительные временные кандидаты можно включить лишь после доказанной классификации и повторной проверки пути; спорные оставить и перечислить в отчёте. Пересчитать первый уровень и размер `artifacts`, проверить keep-list и `git status --short`; Git-изменения от удаления игнорируемых артефактов отсутствуют. Если новая сборка не прошла проверки, старые локальные копии пока не удалять.

**Проверка:** в той же PowerShell-сессии `$targets | ForEach-Object { Test-Path -LiteralPath $_ }` возвращает только `False`; `$keep | ForEach-Object { Test-Path -LiteralPath (Join-Path $artifactRoot "$_\MinePack.Installer.exe") }` — четыре `True`; `git status --short` показывает только ожидаемые исходные/плановые изменения. `%LOCALAPPDATA%\MinePack` и `%APPDATA%\.minecraft` не были объектами удаления. `../doc/` вне Git-корня — обновление спецификации проверить чтением файла отдельно.

## Критерии готовности

- [x] Обычное нажатие Install без диалога закрывает обнаруженный официальный Launcher **до** установки и запускает его **после** успешной настройки профиля.
- [x] Store/AUMID и классический Win32 имеют собственные проверяемые ветви обнаружения/запуска; нет предположения о стабильном пути `WindowsApps` или имени файла по всему диску.
- [x] Невозможность закрытия останавливает установку до загрузки; принудительного завершения и изменения чужих профилей нет.
- [x] Неудача открытия Launcher после успеха сохраняет статус готовой сборки и даёт понятную EN/RU инструкцию.
- [x] Smoke и publish прошли; живой вариант Launcher на машине пользователя проверен либо назван точный блокер. Неиспытанный официальный вариант указан отдельно.
- [x] README EN/RU и раздел 5 спецификации описывают фактический новый путь.
- [x] Четыре основные publish-копии сохранены; удалены только проверенные старые локальные результаты, а исходные `.mrpack`, Packwiz, игровая установка и миры сохранены.
- [x] Основной агент просмотрел diff, тестовые результаты, dry-run и конечный список удалённых путей; статус плана обновлён в `plans/README.md`.

## Стоп-условия

- Текущий пак/Launcher код после работы над 0.9.0 отличается от описанного настолько, что выбранная точка интеграции невалидна.
- Официальность процесса или целевого приложения не подтверждается, либо установлено несколько вариантов и выбор профиля/запуска неоднозначен.
- Launcher не закрылся за ограниченное время, возник отказ в доступе, повторный запуск до записи профиля или другой риск испортить чужой профиль: сообщить ошибку, не использовать `Kill`.
- Для заявленной поддержки варианта Launcher нет достаточной регистрации или возможности живой проверки: не объявлять его проверенным; сообщить границу.
- Новый publish/тесты не прошли: блок B не запускать.
- Любой кандидат удаления выходит за `app/artifacts`, является reparse point, содержит уникальные пользовательские данные или не имеет подтверждённого происхождения: не удалять его.
- Требуется убрать старые `.mrpack` из сохраняемого publish или код поддержки старых версий: это выходит за согласованный объём и ломает Repair/Uninstall; остановиться и сообщить развилку.
