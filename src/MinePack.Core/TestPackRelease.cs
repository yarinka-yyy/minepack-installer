using System.Text.Json;

namespace MinePack.Core;

public static class TestPackRelease
{
    public const string ArtifactFileName = "test-pack-0.18.1.mrpack";
    public const string ArtifactRelativePath = "releases/test-pack/" + ArtifactFileName;
    public const string ArtifactSha512 = "2E259CEE78A2022CDE78012BA95E6EE5E789C3F0DA45D3C51B1D7E4FC689865E0D981FD784C850EF74492F02F8AF8497943394ADC15DBE9135718918FAFB9906";
    public const string SmoothArtifactFileName = "test-pack-0.15.0.mrpack";
    public const string SmoothArtifactSha512 = "9698BCBFE76BC6072ADB2609F2EDF17D4E823A5DB850BA0B54BE16E2EA39A967F6536C3FF025E7363BC6DED76D28BA1280DA1ACB8BD09D9FDA209889524ED45A";
    public const string PriorArtifactFileName = "test-pack-0.10.0.mrpack";
    public const string PriorArtifactSha512 = "FDEA02362FB025310E26C3DE14DE18B907574BB412A0ADEC800881ECD6D8F31095D4021691EB0A35DCECB318E042F56360A36EDA79372453F658BC46090327E1";
    public const string MapArtifactFileName = "test-pack-0.9.0.mrpack";
    public const string MapArtifactSha512 = "FDA99C8A9545A17890643E1BECCE9914176F1D53CB4DF81A762AB60B7B315EC233952181D2D10BE9B5555E74A89561803580D5E5AFFD7993BD16C79F48C9C648";
    public const string AnimationArtifactFileName = "test-pack-0.8.0.mrpack";
    public const string AnimationArtifactSha512 = "F191D98E2E87C6CB1B75274471B28535C2D717F795A6A1B94C2BE3F5015F9069AF5BB7AFA2A55FD0AFA0866656506F82E48C36FC00A38AE7580324E859DBAA38";
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
    public const string PackVersion = "0.18.1";
    public const string LowFireArtifactFileName = "test-pack-0.18.0.mrpack";
    public const string LowFireArtifactSha512 = "C7469A3820A4B9BF75132BD016FB99F5B18F3E6FF66CDF0DCFBCE937E5BA8309AD4062C813457B8F95401F6D2F3326ABDBCDBA067B911795B6C574CD9D525776";
    public const string MinecraftVersion = "26.2";
    public const string FabricLoaderVersion = "0.19.5";
    public const string FabricApiVersion = "0.161.0+26.2";
    public static string[] PreviousResourcePacks =>
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

    public static string[] InitialResourcePacks => PreviousResourcePacks
        .Where(name => name != "LowOnFire v26.2§8.zip").ToArray();

    public static string InitialOptions => BuildInitialOptions(InitialResourcePacks);

    public static string BuildInitialOptions(IEnumerable<string> resourcePacks) =>
        "version:4903" + Environment.NewLine +
        "enableVsync:false" + Environment.NewLine +
        "resourcePacks:" + JsonSerializer.Serialize(new[] { "vanilla" }
            .Concat(resourcePacks.Select(name => "file/" + name)).Append("punchy:punchy")) + Environment.NewLine +
        "incompatibleResourcePacks:[]" + Environment.NewLine +
        "key_key.sprint:key.keyboard.left.shift" + Environment.NewLine +
        "key_key.sneak:key.keyboard.left.control" + Environment.NewLine +
        "fov:0.25" + Environment.NewLine +
        "fullscreen:true" + Environment.NewLine +
        "exclusiveFullscreen:true" + Environment.NewLine +
        "guiScale:4" + Environment.NewLine;
}
