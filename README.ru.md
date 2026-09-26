# Установщик MinePack

[English](README.md) · Русский

MinePack устанавливает закреплённую сборку для **официальной Minecraft: Java Edition** через официальный Minecraft Launcher. Аккаунт остаётся в Launcher.

Windows · Fabric · Minecraft 26.2 · официальный Minecraft Launcher

Выберите одну сборку: в **Vanilla Plus 0.15.0** — 38 модов, 8 ресурспаков и 1 шейдер, включая Smooth Swapping и Subtle Effects. **Vanilla 2 Plus 0.14.0** включает этот состав и настройки, пять строительных модов Macaw's, четыре проекта для генерации мира и структур, Guard Villagers и обязательные библиотеки (всего 50 модов). Ещё два ресурспака добавляют анимации охранников (итого 10 ресурспаков). Установщик скачивает закреплённые файлы, проверяет их хеши и использует один профиль MinePack с отдельной папкой игры для активной сборки.

## Установка

1. Публичную загрузку установщика пока не удалось подтвердить; ссылка пока не опубликована.
2. Когда появится полный пакет установщика, скачайте и распакуйте его. Все файлы пакета должны остаться рядом.
3. Запустите `MinePack.Installer.exe`, выберите **Vanilla Plus** или **Vanilla 2 Plus** и нажмите **«Установить сборку»**. MinePack сам закроет открытый официальный Launcher, установит выбранную сборку и обновит профиль MinePack, затем отправит запрос на запуск Launcher.
4. Выберите профиль **MinePack** и нажмите **«Играть»**. При первом запуске Launcher загрузит базовые файлы Minecraft.

## Файлы игры и миры

По умолчанию MinePack хранит данные в `%LOCALAPPDATA%\MinePack`; в установщике можно выбрать другую папку. Для каждой сборки создаётся отдельный каталог внутри `instances` со своими папками `mods`, `resourcepacks`, `shaderpacks`, `saves` и настройками игры. Профиль MinePack указывает на активный каталог. При переключении прежняя папка и её миры сохраняются; MinePack не копирует миры между сборками.

Обычная `.minecraft`, её миры и другие профили Launcher не затрагиваются. Старая версия MinePack хранится отдельно, пока новая не установится успешно. Проверка и исправление, а также удаление сборки сохраняют миры и другие пользовательские файлы. Импорт миров копирует их, исходные файлы остаются на месте. Перед открытием мира в другой версии Minecraft сделайте резервную копию.

## Состав сборки

<details>
<summary>Производительность и дальность (8)</summary>

- [Sodium](https://modrinth.com/mod/AANobbMI)
- [Voxy](https://modrinth.com/mod/fxxUqruK)
- [C2ME](https://modrinth.com/mod/VSNURh3q)
- [ImmediatelyFast](https://modrinth.com/mod/5ZwdcRci)
- [FerriteCore](https://modrinth.com/mod/uXXizFIs)
- [Entity Culling](https://modrinth.com/mod/NNAgCjsB)
- [Better Block Entities](https://modrinth.com/mod/ONZm0H7Y)
- [Clumps](https://modrinth.com/mod/Wnxd13zP)

</details>

<details>
<summary>Графика и анимации (9)</summary>

- [Iris](https://modrinth.com/mod/YL57xq9U)
- [EMF](https://modrinth.com/mod/4I1XuqiY)
- [ETF](https://modrinth.com/mod/BVzZfTc1)
- [Punchy!](https://modrinth.com/mod/8aoMKplv)
- [Explosive Enhancement](https://modrinth.com/mod/OSQ8mw2r)
- [Dense Flowers](https://modrinth.com/mod/Ud3A1Fat)
- [Inventory Particles](https://modrinth.com/mod/XYnKrsxH)
- [Advancement Plaques](https://modrinth.com/mod/9NM0dXub)
- [Subtle Effects](https://modrinth.com/mod/4q8UOK1d) — настраиваемые частицы и визуальные эффекты

</details>

<details>
<summary>Инструменты и удобство (10)</summary>

- [Chunky](https://modrinth.com/mod/fALzjamp)
- [Inventory Sorting](https://modrinth.com/mod/5ibSyLAz)
- [Held Item Info](https://modrinth.com/mod/tEcWzCZz)
- [Pick Up Notifier](https://modrinth.com/mod/ZX66K16c)
- [Xaero's World Map](https://modrinth.com/mod/NcUtCpym)
- [Cherished Worlds](https://modrinth.com/mod/3azQ6p0W)
- [Leaf Me Alone](https://modrinth.com/mod/ppMUvsIg)
- [InvMove](https://modrinth.com/mod/REfW2AEX)
- [Mod Menu](https://modrinth.com/mod/mOgUt4GM)
- [Smooth Swapping](https://modrinth.com/mod/ydZic5r4) — анимация перемещения предметов в инвентаре

</details>

<details>
<summary>Звук (2)</summary>

- [Cool Rain](https://modrinth.com/mod/iDyqnQLT)
- [Sound Physics Remastered](https://modrinth.com/mod/qyVF9oeo)

</details>

<details>
<summary>Техническая основа (9)</summary>

- [Fabric API](https://modrinth.com/mod/P7dR8mSH)
- [Cloth Config API](https://modrinth.com/mod/9s6osm5g)
- [Forge Config API Port](https://modrinth.com/mod/ohNO6lps)
- [MossyLib](https://modrinth.com/mod/ffLDUGbm)
- [Puzzles Lib](https://modrinth.com/mod/QAGBst4M)
- [Iceberg](https://modrinth.com/mod/5faXoLqX)
- [Text Placeholder API](https://modrinth.com/mod/eXts2L7r)
- [Fzzy Config](https://modrinth.com/mod/hYykXjDp)
- [Fabric Language Kotlin](https://modrinth.com/mod/Ha28R6CL)

</details>

<details>
<summary>Ресурспаки (8)</summary>

- [Fresh Animations](https://modrinth.com/resourcepack/50dA9Sha)
- [Fresh Animations: Extensions](https://modrinth.com/resourcepack/YAVTU8mK)
- [Low On Fire](https://modrinth.com/resourcepack/RRxvWKNC)
- [Fancy Crops](https://modrinth.com/resourcepack/UGEVQ6t9)
- [Better Flame Particles](https://modrinth.com/resourcepack/ivUZsvzp)
- [Os' Colorful Grasses](https://modrinth.com/resourcepack/O2zhH8n8)
- [GUI Retextures — Dark](https://modrinth.com/resourcepack/ZM5PH9W6)
- [Better Click Sounds](https://modrinth.com/resourcepack/XWQ6jMjk)

</details>

<details>
<summary>Шейдер (1)</summary>

- [Complementary Reimagined](https://modrinth.com/shader/HVnmMxH1)

</details>

<details>
<summary>Дополнения Vanilla 2 Plus — строительные моды (5)</summary>

Они добавляются ко всему составу Vanilla Plus выше.

- [Macaw's Windows](https://modrinth.com/mod/C7I0BCni)
- [Macaw's Fences and Walls](https://modrinth.com/mod/GmwLse2I)
- [Macaw's Bridges](https://modrinth.com/mod/GURcjz8O)
- [Macaw's Doors](https://modrinth.com/mod/kNxa8z3e)
- [Macaw's Stairs](https://modrinth.com/mod/iP3wH1ha)

Для игры на внешнем многопользовательском сервере эти моды блоков нужно установить и на сервере. В одиночной игре используется встроенный сервер.

</details>

<details>
<summary>Дополнения Vanilla 2 Plus — мир и структуры (4)</summary>

Они добавляются ко всему составу Vanilla Plus и пяти модам Macaw's выше.

- [Better Villages](https://modrinth.com/mod/dGVX5JbJ)
- [MNS — Moog's Nether Structures](https://modrinth.com/mod/nGUXvjTa)
- [MVS — Moog's Voyager Structures](https://modrinth.com/mod/OQAgZMH1)
- [Structory](https://modrinth.com/datapack/aKCwCJlY) (вариант мода для Fabric)

В сборку включены обязательные библиотеки [Library Ferret](https://modrinth.com/mod/DOB2l4oJ) и [Moog's Structure Lib](https://modrinth.com/mod/1oUDhxuy). Новые структуры появляются в ещё не исследованных чанках. Для игры на внешнем сервере эти моды генерации и библиотеки нужны также на сервере; одиночная игра использует встроенный сервер.

</details>

<details>
<summary>Дополнение Vanilla 2 Plus — жители (1)</summary>

- [Guard Villagers (Fabric/Quilt)](https://modrinth.com/mod/59rkB3YY) — охранники деревень

Smooth Swapping и Subtle Effects входят в обе сборки вместе с Fzzy Config и Fabric Language Kotlin для Subtle Effects. Smooth Swapping повторно проверяется после того, как не заработал в ранней сборке; Mod Menu открывает его настройки. Для охранников на внешнем сервере требуется серверная установка Guard Villagers.

Vanilla 2 Plus также включает [F.M.R.P](https://modrinth.com/resourcepack/freshly-modded) с моделями охранников и [Semos Animations Lib](https://modrinth.com/resourcepack/semos-animations-lib) с подробными движениями. Оба ресурспака работают на стороне игрока; их внешний вид ещё нужно проверить в игре.

</details>

## Сборка из исходного кода

Для сборки в Windows нужен .NET 10 SDK. Команда публикации:

```powershell
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish-0.15.0
```
