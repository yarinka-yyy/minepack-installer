# Установщик MinePack

[English](README.md) · Русский · [简体中文](README.zh-CN.md)

MinePack помогает установить готовый набор модов для **официальной версии Minecraft: Java Edition** без ручной настройки каждого мода. Он скачивает выбранные файлы, проверяет их и размещает игру в отдельной папке MinePack.

MinePack работает в Windows и устанавливает сборки для Minecraft: Java Edition версии 26.2. Для игры нужен официальный Minecraft Launcher и аккаунт с доступом к Java Edition.

## Выберите сборку

**Vanilla Plus** включает 37 модов, 8 ресурспаков и 1 шейдер. Существа и жители двигаются выразительнее, а шейдер и визуальные дополнения меняют освещение, воду, растения и частицы. Voxy показывает уже исследованный ландшафт далеко за обычной дальностью прорисовки; моды производительности помогают игре работать плавнее. Карта мира, сортировка инвентаря, подсказки о предметах и более объёмное звучание делают повседневную игру удобнее.

**Frontier** включает всё содержимое Vanilla Plus: всего 61 мод, 10 ресурспаков и 1 шейдер. Пять строительных модов добавляют окна, заборы и стены, мосты, двери и новые варианты лестниц. В ещё не исследованных областях встречаются обновлённые деревни, руины, башни, постройки Незера, лагеря разбойников, а также переработанные модами YUNG's подземелья, храмы, шахты, крепости и крепости Незера; один только MVS заявляет [более 130 видов построек](https://modrinth.com/mod/moogs-voyager-structures). В деревнях появляются жители-охранники с мечами и арбалетами. Пауки могут лазать по стенам и потолку, а в списке миров видно время игры в каждом мире.

## Установка

Доступный по ссылке релиз 1.0.0 вышел до этих дополнений Frontier. ZIP версии 1.0.3 подготовлен локально, но ещё не опубликован как GitHub Release.

1. Скачайте [MinePack-Installer-1.0.0-win-x64.zip](https://github.com/yarinka-yyy/minepack-installer/releases/download/v1.0.0/MinePack-Installer-1.0.0-win-x64.zip) со страницы первого релиза и полностью распакуйте его в папку. Не перемещайте файлы отдельно друг от друга.
2. Запустите `MinePack.Installer.exe`, выберите **Vanilla Plus** или **Frontier**, затем нажмите **«Установить сборку»**. MinePack подготовит сборку и обновит свой профиль Launcher. Во время установки он может закрыть запущенный официальный Launcher, а по завершении попробует открыть его снова.
3. В официальном Minecraft Launcher выберите профиль **MinePack** и нажмите **«Играть»**. При первом запуске Launcher может скачать базовые файлы Minecraft. Если Launcher не открылся сам, запустите его вручную и выберите MinePack.

EXE пока не подписан цифровой подписью: Windows может показать предупреждение SmartScreen. Скачивайте ZIP только с указанной страницы релиза.

## Папки игры и миры

По умолчанию MinePack хранит файлы в `%LOCALAPPDATA%\MinePack`; в установщике можно выбрать другую папку. Для каждой сборки есть отдельная папка игры. При переключении прежняя папка и миры в ней сохраняются. MinePack не копирует миры между сборками автоматически.

Проверка и исправление, а также удаление сборки сохраняют миры и другие личные файлы. Удаление убирает файлы, которыми управляет MinePack, и его профиль Launcher. Команда **«Импортировать миры»** копирует миры в активную сборку, не удаляя оригиналы. Перед открытием мира в другой версии Minecraft сделайте резервную копию. Обычная папка `.minecraft` и другие профили Launcher не затрагиваются.

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
<summary>Инструменты и удобство (9)</summary>

- [Chunky](https://modrinth.com/mod/fALzjamp)
- [Inventory Sorting](https://modrinth.com/mod/5ibSyLAz)
- [Held Item Info](https://modrinth.com/mod/tEcWzCZz)
- [Pick Up Notifier](https://modrinth.com/mod/ZX66K16c)
- [Xaero's World Map](https://modrinth.com/mod/NcUtCpym)
- [Cherished Worlds](https://modrinth.com/mod/3azQ6p0W)
- [Leaf Me Alone](https://modrinth.com/mod/ppMUvsIg)
- [InvMove](https://modrinth.com/mod/REfW2AEX)
- [Mod Menu](https://modrinth.com/mod/mOgUt4GM)

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
<summary>Дополнения Frontier — строительные моды (5)</summary>

Они добавляются ко всему составу Vanilla Plus выше.

- [Macaw's Windows](https://modrinth.com/mod/C7I0BCni)
- [Macaw's Fences and Walls](https://modrinth.com/mod/GmwLse2I)
- [Macaw's Bridges](https://modrinth.com/mod/GURcjz8O)
- [Macaw's Doors](https://modrinth.com/mod/kNxa8z3e)
- [Macaw's Stairs](https://modrinth.com/mod/iP3wH1ha)

Для игры на внешнем многопользовательском сервере эти моды блоков нужно установить и на сервере. В одиночной игре используется встроенный сервер.

</details>

<details>
<summary>Дополнения Frontier — мир и структуры (11)</summary>

Они добавляются ко всему составу Vanilla Plus и пяти модам Macaw's выше.

- [Better Villages](https://modrinth.com/mod/dGVX5JbJ)
- [MNS — Moog's Nether Structures](https://modrinth.com/mod/nGUXvjTa)
- [MVS — Moog's Voyager Structures](https://modrinth.com/mod/OQAgZMH1)
- [Structory](https://modrinth.com/datapack/aKCwCJlY) (вариант мода для Fabric)
- [It Takes a Pillage Continuation](https://modrinth.com/mod/QOJOg1gE)
- [YUNG's Better Desert Temples](https://github.com/YUNG-GANG/YUNGs-Better-Desert-Temples)
- [YUNG's Better Dungeons](https://github.com/YUNG-GANG/YUNGs-Better-Dungeons)
- [YUNG's Better Jungle Temples](https://github.com/YUNG-GANG/YUNGs-Better-Jungle-Temples)
- [YUNG's Better Mineshafts](https://github.com/YUNG-GANG/YUNGs-Better-Mineshafts)
- [YUNG's Better Nether Fortresses](https://github.com/YUNG-GANG/YUNGs-Better-Fortresses)
- [YUNG's Better Strongholds](https://github.com/YUNG-GANG/YUNGs-Better-Strongholds)

Сборка также добавляет [Voxy WorldGen](https://modrinth.com/mod/xT0lnNE9) и обязательные библиотеки [Library Ferret](https://modrinth.com/mod/DOB2l4oJ), [Moog's Structure Lib](https://modrinth.com/mod/1oUDhxuy), [Resourceful Lib](https://modrinth.com/mod/G1hIVOrD) и [YUNG's API](https://github.com/YUNG-GANG/YUNGs-API). Семь файлов YUNG's включены в архив как форки для Minecraft 26.2. Новые структуры появляются в ещё не исследованных чанках. Для игры на внешнем сервере эти моды генерации и библиотеки нужны также на сервере; одиночная игра использует встроенный сервер.

</details>

<details>
<summary>Дополнение Frontier — жители (1)</summary>

- [Guard Villagers (Fabric/Quilt)](https://modrinth.com/mod/59rkB3YY) — охранники деревень

Subtle Effects входит в обе сборки вместе с Fzzy Config и Fabric Language Kotlin. Для охранников на внешнем сервере требуется серверная установка Guard Villagers.

Frontier также включает [F.M.R.P](https://modrinth.com/resourcepack/freshly-modded) с моделями охранников и [Semos Animations Lib](https://modrinth.com/resourcepack/semos-animations-lib) с подробными движениями. Оба ресурспака работают на стороне игрока; их внешний вид ещё нужно проверить в игре.

</details>

<details>
<summary>Дополнения Frontier — пауки и время игры (2)</summary>

- [Nyf's Spiders](https://modrinth.com/mod/nyfs-spiders) от Nyfaria — форк для Minecraft 26.2, позволяющий паукам лазать по стенам и потолку.
- [World Play Time](https://modrinth.com/mod/world-play-time) — форк для Minecraft 26.2, показывающий время игры в каждом мире в списке миров.

Оба JAR входят в Frontier. World Play Time работает на клиенте; для поведения пауков на внешнем сервере нужен Nyf's Spiders и на сервере.

</details>

## Лицензия

Исходный код и оригинальная документация MinePack доступны по лицензии [0BSD](LICENSE): их можно использовать, изменять и распространять без обязательного указания авторства. Моды, ресурспаки, шейдер, шрифт и Minecraft принадлежат своим правообладателям; 0BSD не меняет их условия.
