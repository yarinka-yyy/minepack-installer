using System.Text.Json;

namespace MinePack.Core;

public static class TestPackRelease
{
    public const string ArtifactFileName = "test-pack-0.8.0.mrpack";
    public const string ArtifactRelativePath = "releases/test-pack/" + ArtifactFileName;
    public const string ArtifactSha512 = "F191D98E2E87C6CB1B75274471B28535C2D717F795A6A1B94C2BE3F5015F9069AF5BB7AFA2A55FD0AFA0866656506F82E48C36FC00A38AE7580324E859DBAA38";
    public const string GraphicsArtifactFileName = "test-pack-0.7.0.mrpack";
    public const string GraphicsArtifactSha512 = "60F0913CDCF2F9FFD61E6A6759C554AEF0D59D27C18BFAEE35067380D17B56BDA1495E0388FC45503FB393CA5DE0D885CD026EBFBBFD086A1E7DFB86A36650DF";
    public const string InventoryArtifactFileName = "test-pack-0.6.0.mrpack";
    public const string InventoryArtifactSha512 = "0BFC3915A9F1856770536202449C3936A29BD2AD1FE1E090BD525FCF0E10307F9C1C2DCAEA69DD66C24F036663307C9034745D42559BFF23F2DBA9EE00B684E5";
    public const string VisualArtifactFileName = "test-pack-0.5.0.mrpack";
    public const string VisualArtifactSha512 = "0B94017C6C392AB05B17102C72A9A3207AE5F52BA7175F17C7FF5F764FEC06EA62397183DC3B0F32AF6309600DE87F82E66DE1C77ED51384ED528E04FC8E23F6";
    public const string C2meArtifactFileName = "test-pack-0.4.0.mrpack";
    public const string C2meArtifactSha512 = "04483AB2F7996049C7962C5ADB835499DD1B05C36BBCA9EFBE36F3071B7685136EE56C51ECF55FEA0FCC94C3CB4F33AE48ED1DD4351CAF40D01A39B157A02D90";
    public const string VoxyArtifactFileName = "test-pack-0.3.0.mrpack";
    public const string VoxyArtifactSha512 = "9B50156730A5A17E264B4EA0B5AD0BE594CF426CB46BB54D4AF16E4960F1B55222B7F22C873135BBDF79E9BE8C05B069C0D44396DDD8DD8A77A5D649DBE57E84";
    public const string PreviousArtifactFileName = "test-pack-0.2.0.mrpack";
    public const string PreviousArtifactSha512 = "76320C3EBB6B32D53EB0E58B8E4DD3FF721CEB2F718E2D18CB976A020DC2C4104F2E586277314DCC24227DC0DEA18CF6661835F01E04F61AEB51D22F88A8FF83";
    public const string LegacyArtifactFileName = "test-pack-0.1.0.mrpack";
    public const string LegacyArtifactSha512 = "453fc54446e6d7b379c7c07ca6f995998d6cba01791b0749d6929fce98bce59561aaa598b255344b342396251877a0e7aceccc3100ea179484d0a967ceba0ff5";
    public const string PackVersion = "0.8.0";
    public const string MinecraftVersion = "26.2";
    public const string FabricLoaderVersion = "0.19.5";
    public const string FabricApiVersion = "0.161.0+26.2";
    public static string[] InitialResourcePacks =>
    [
        "better_click_sounds_1.3.zip",
        "Os' Colorful Grasses (Mix).zip",
        "better_flame_particles-v3.1-mc1.21.9+-resourcepack.zip",
        "Fancy Crops v1.3.zip",
        "LowOnFire v26.2§8.zip",
        "FreshAnimations_v1.10.5.zip",
        "FA+All_Extensions-v1.9.2.zip",
        "GUIRetextures-Dark-2.1.zip"
    ];

    public static string InitialOptions =>
        "version:4903" + Environment.NewLine +
        "resourcePacks:" + JsonSerializer.Serialize(new[] { "vanilla" }
            .Concat(InitialResourcePacks.Select(name => "file/" + name)).Append("punchy:punchy")) + Environment.NewLine +
        "incompatibleResourcePacks:[]" + Environment.NewLine +
        "key_key.sprint:key.keyboard.left.shift" + Environment.NewLine +
        "key_key.sneak:key.keyboard.left.control" + Environment.NewLine +
        "fov:0.25" + Environment.NewLine +
        "fullscreen:true" + Environment.NewLine +
        "exclusiveFullscreen:true" + Environment.NewLine +
        "guiScale:4" + Environment.NewLine;
}
