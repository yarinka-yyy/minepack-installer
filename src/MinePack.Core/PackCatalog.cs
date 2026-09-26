namespace MinePack.Core;

public sealed record CatalogItem(string Name, string GroupKey, string Kind, string ProjectId, string FilePath)
{
    public string Group => LocalizedText.Get(GroupKey);
    public Uri ModrinthUrl => new($"https://modrinth.com/{Kind}/{ProjectId}");
}

public sealed record CatalogGroup(string Key, IReadOnlyList<CatalogItem> Items)
{
    public string Name => LocalizedText.Get(Key);
    public string Heading => $"{Name} — {Items.Count}";
}

public static class PackCatalog
{
    private static CatalogGroup Group(string key, string kind, params (string Name, string Id, string Path)[] items) =>
        new(key, items.Select(item => new CatalogItem(item.Name, key, kind, item.Id, item.Path)).ToArray());

    public static IReadOnlyList<CatalogGroup> VanillaPlusGroups { get; } =
    [
        Group("CatalogPerformance", "mod",
            ("Sodium", "AANobbMI", "mods/sodium-fabric-0.9.1+mc26.2.jar"),
            ("Voxy", "fxxUqruK", "mods/voxy-0.2.19-beta.jar"),
            ("C2ME", "VSNURh3q", "mods/c2me-fabric-mc26.2-0.4.2-alpha.0.52.jar"),
            ("ImmediatelyFast", "5ZwdcRci", "mods/ImmediatelyFast-Fabric-1.16.5+26.2.jar"),
            ("FerriteCore", "uXXizFIs", "mods/ferritecore-9.0.0-fabric.jar"),
            ("Entity Culling", "NNAgCjsB", "mods/entityculling-fabric-1.11.2-mc26.2.jar"),
            ("Better Block Entities", "ONZm0H7Y", "mods/bbe-fabric-1.3.7+mc26.2.jar"),
            ("Clumps", "Wnxd13zP", "mods/Clumps-fabric-26.2-26.2.1.jar")),
        Group("CatalogGraphics", "mod",
            ("Iris", "YL57xq9U", "mods/iris-fabric-1.11.2+mc26.2.jar"),
            ("EMF", "4I1XuqiY", "mods/entity_model_features-3.3.8-26.2-fabric.jar"),
            ("ETF", "BVzZfTc1", "mods/entity_texture_features-7.2.4-26.2-fabric.jar"),
            ("Punchy!", "8aoMKplv", "mods/punchy-2.8a-fabric-26.2.jar"),
            ("Explosive Enhancement", "OSQ8mw2r", "mods/explosive-enhancement-1.4.2-26.2.jar"),
            ("Dense Flowers", "Ud3A1Fat", "mods/dense-flowers-0.3.1+mc26.2.jar"),
            ("Inventory Particles", "XYnKrsxH", "mods/InventoryParticles-3.2.0+26.2+fabric.jar"),
            ("Advancement Plaques", "9NM0dXub", "mods/AdvancementPlaques-26.2-fabric-1.7.2.jar")),
        Group("CatalogTools", "mod",
            ("Chunky", "fALzjamp", "mods/Chunky-Fabric-1.5.3.jar"),
            ("Inventory Sorting", "5ibSyLAz", "mods/inventorysorter-fabric-3.0.1+mc26.2.jar"),
            ("Held Item Info", "tEcWzCZz", "mods/held-item-info-1.9.2.jar"),
            ("Pick Up Notifier", "ZX66K16c", "mods/PickUpNotifier-v26.2.0-mc26.2.x-Fabric.jar"),
            ("Xaero's World Map", "NcUtCpym", "mods/xaeroworldmap-fabric-26.2-1.46.1.jar"),
            ("Cherished Worlds", "3azQ6p0W", "mods/cherishedworlds-fabric-17.0.0+26.2.jar"),
            ("Leaf Me Alone", "ppMUvsIg", "mods/leafmealone-1.2.0.jar"),
            ("InvMove", "REfW2AEX", "mods/InvMove-0.9.6+26.2-Fabric.jar"),
            ("Mod Menu", "mOgUt4GM", "mods/modmenu-20.0.2.jar")),
        Group("CatalogSound", "mod",
            ("Cool Rain", "iDyqnQLT", "mods/coolrain-1.4.0-26.2.jar"),
            ("Sound Physics Remastered", "qyVF9oeo", "mods/sound-physics-remastered-fabric-1.5.1+26.2.jar")),
        Group("CatalogTechnical", "mod",
            ("Fabric API", "P7dR8mSH", "mods/fabric-api-0.161.0+26.2.jar"),
            ("Cloth Config API", "9s6osm5g", "mods/cloth-config-26.2.155.jar"),
            ("Forge Config API Port", "ohNO6lps", "mods/ForgeConfigAPIPort-v26.2.1-mc26.2.x-Fabric.jar"),
            ("MossyLib", "ffLDUGbm", "mods/MossyLib-1.6.0+26.2+fabric.jar"),
            ("Puzzles Lib", "QAGBst4M", "mods/PuzzlesLib-v26.2.4-mc26.2.x-Fabric.jar"),
            ("Iceberg", "5faXoLqX", "mods/Iceberg-26.2-fabric-1.4.2.2.jar"),
            ("Text Placeholder API", "eXts2L7r", "mods/placeholder-api-3.1.0-beta.1+26.2.jar")),
        Group("CatalogResourcePacks", "resourcepack",
            ("Fresh Animations", "50dA9Sha", "resourcepacks/FreshAnimations_v1.10.5.zip"),
            ("Fresh Animations: Extensions", "YAVTU8mK", "resourcepacks/FA+All_Extensions-v1.9.2.zip"),
            ("Low On Fire", "RRxvWKNC", "resourcepacks/LowOnFire v26.2§8.zip"),
            ("Fancy Crops", "UGEVQ6t9", "resourcepacks/Fancy Crops v1.3.zip"),
            ("Better Flame Particles", "ivUZsvzp", "resourcepacks/better_flame_particles-v3.1-mc1.21.9+-resourcepack.zip"),
            ("Os' Colorful Grasses", "O2zhH8n8", "resourcepacks/Os' Colorful Grasses (Mix).zip"),
            ("GUI Retextures — Dark", "ZM5PH9W6", "resourcepacks/GUIRetextures-Dark-2.1.zip"),
            ("Better Click Sounds", "XWQ6jMjk", "resourcepacks/better_click_sounds_1.3.zip")),
        Group("CatalogShader", "shader",
            ("Complementary Reimagined", "HVnmMxH1", "shaderpacks/ComplementaryReimagined_r5.9.3.zip"))
    ];

    public static IReadOnlyList<CatalogGroup> Vanilla2PlusGroups { get; } = VanillaPlusGroups
        .Select(group => group.Key == "CatalogTechnical"
            ? group with { Items = group.Items.Concat(new[]
                {
                    new CatalogItem("Library Ferret", "CatalogTechnical", "mod", "DOB2l4oJ", "mods/libraryferret-fabric-26.2-5.0.0.jar"),
                    new CatalogItem("Moog's Structure Lib", "CatalogTechnical", "mod", "1oUDhxuy", "mods/MoogsStructureLib-fabric-26.2-3.3.0.jar")
                }).ToArray() }
            : group)
        .Append(Group("CatalogBuilding", "mod",
            ("Macaw's Windows", "C7I0BCni", "mods/mcw-windows-2.4.2-mc26.2fabric.jar"),
            ("Macaw's Fences and Walls", "GmwLse2I", "mods/mcw-fences-1.2.1-mc26.2fabric.jar"),
            ("Macaw's Bridges", "GURcjz8O", "mods/mcw-bridges-3.1.2-mc26.2fabric.jar"),
            ("Macaw's Doors", "kNxa8z3e", "mods/mcw-doors-1.1.5-mc26.2fabric.jar"),
            ("Macaw's Stairs", "iP3wH1ha", "mods/mcw-stairs-1.0.2-mc26.2fabric.jar")))
        .Append(new CatalogGroup("CatalogWorldgen",
        [
            new CatalogItem("Better Villages", "CatalogWorldgen", "mod", "dGVX5JbJ", "mods/bettervillage-fabric-26.2-4.0.0.jar"),
            new CatalogItem("MNS - Moog's Nether Structures", "CatalogWorldgen", "mod", "nGUXvjTa", "mods/MoogsNetherStructures-universal-1.21-3.1.1.jar"),
            new CatalogItem("MVS - Moog's Voyager Structures", "CatalogWorldgen", "mod", "OQAgZMH1", "mods/MoogsVoyagerStructures-universal-1.21-5.1.3.jar"),
            new CatalogItem("Structory", "CatalogWorldgen", "datapack", "aKCwCJlY", "mods/Structory_26.2_v1.3.7.jar")
        ])).ToArray();

    public static IReadOnlyList<CatalogGroup> Groups => VanillaPlusGroups;

    public static IReadOnlyList<CatalogItem> Items { get; } = Vanilla2PlusGroups.SelectMany(group => group.Items).ToArray();
}
