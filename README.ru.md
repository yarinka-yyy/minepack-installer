# Установщик MinePack

[English](README.md) · Русский

MinePack устанавливает закреплённую сборку Minecraft 26.2 версии 0.8.0 за несколько кликов. **Для работы нужны официальные Minecraft: Java Edition и Minecraft Launcher; аккаунт остаётся в Launcher.**

Windows · Minecraft 26.2 · Fabric · релиз 0.8.0

В сборке 26 модов, 8 ресурспаков и 1 шейдер. Установщик скачивает закреплённые файлы, проверяет их и создаёт отдельный профиль MinePack и каталог игры для официального Launcher.

## Установка

1. Публичную загрузку установщика пока не удалось подтвердить; ссылка пока не опубликована.
2. Когда появится полный пакет установщика, скачайте и распакуйте его. Все файлы пакета должны остаться рядом.
3. Закройте Minecraft Launcher, запустите `MinePack.Installer.exe`, нажмите **«Установить сборку»** и дождитесь сообщения **«Сборка готова»**.
4. Откройте официальный Minecraft Launcher, выберите профиль **MinePack** и нажмите **«Играть»**. При первом запуске Launcher загрузит базовые файлы Minecraft.

## Файлы игры и миры

По умолчанию MinePack хранит данные в `%LOCALAPPDATA%\MinePack`; в установщике можно выбрать другую папку. Для релиза 0.8.0 создаётся отдельный каталог внутри `instances` со своими папками `mods`, `resourcepacks`, `shaderpacks`, `saves` и настройками игры.

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
<summary>Графика и анимации (7)</summary>

- [Iris](https://modrinth.com/mod/YL57xq9U)
- [EMF](https://modrinth.com/mod/4I1XuqiY)
- [ETF](https://modrinth.com/mod/BVzZfTc1)
- [Punchy!](https://modrinth.com/mod/8aoMKplv)
- [Explosive Enhancement](https://modrinth.com/mod/OSQ8mw2r)
- [Dense Flowers](https://modrinth.com/mod/Ud3A1Fat)
- [Inventory Particles](https://modrinth.com/mod/XYnKrsxH)

</details>

<details>
<summary>Инструменты и удобство (4)</summary>

- [Chunky](https://modrinth.com/mod/fALzjamp)
- [Inventory Sorting](https://modrinth.com/mod/5ibSyLAz)
- [Held Item Info](https://modrinth.com/mod/tEcWzCZz)
- [Pick Up Notifier](https://modrinth.com/mod/ZX66K16c)

</details>

<details>
<summary>Звук (2)</summary>

- [Cool Rain](https://modrinth.com/mod/iDyqnQLT)
- [Sound Physics Remastered](https://modrinth.com/mod/qyVF9oeo)

</details>

<details>
<summary>Техническая основа (5)</summary>

- [Fabric API](https://modrinth.com/mod/P7dR8mSH)
- [Cloth Config API](https://modrinth.com/mod/9s6osm5g)
- [Forge Config API Port](https://modrinth.com/mod/ohNO6lps)
- [MossyLib](https://modrinth.com/mod/ffLDUGbm)
- [Puzzles Lib](https://modrinth.com/mod/QAGBst4M)

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

## Сборка из исходного кода

Для сборки в Windows нужен .NET 10 SDK. Команда публикации:

```powershell
dotnet publish src/MinePack.Installer -c Release -r win-x64 --self-contained true -o artifacts/publish-0.8.0
```
