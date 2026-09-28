namespace MinePack.Core;

public static class Vanilla2PlusRelease
{
    public const string ArtifactFileName = "vanilla-2-plus-0.19.3.mrpack";
    public const string ArtifactRelativePath = "releases/vanilla-2-plus/" + ArtifactFileName;
    public const string ArtifactSha512 = "5D62203DD9A729207BB509F08BCF67F81A27F87C68E86913FB2ABBA043CB4FC7EAAB65A31AB4E72E134E0EDDB393B1BC9B58B136ADD4A0F46C8D3FA5C1BBFAF7";
    public const string PackVersion = "0.19.3";
    public const string YungsArtifactFileName = "vanilla-2-plus-0.19.2.mrpack";
    public const string YungsArtifactSha512 = "BEE5575B637E697C96DE783E8D252644BD24E92330346B2BF30BC55FDBCC2E166B27798139A3202C3E7E39B20EFD662A9D56EF3FCDF091BBB1075D4518D32A87";
    public const string TunedArtifactFileName = "vanilla-2-plus-0.19.1.mrpack";
    public const string TunedArtifactSha512 = "3C4CA97D8B469D750F852D16EBD402E8C6AD328ACB16AB267B87AB6CDDAF03505103EC933AFA03172A8E8704498898B8E05C6140D27F831A0F051A36CA309823";
    public const string UntunedArtifactFileName = "vanilla-2-plus-0.19.0.mrpack";
    public const string UntunedArtifactSha512 = "EDA5F98DA58A14BE7C5A608946559483E0DB38C60F85E98B64B45C9C7F7FB4063B5270A578E00C47139026BAA41AD23B6521804B4FD2C939CDE6340D4B755C67";
    public const string WorldgenArtifactFileName = "vanilla-2-plus-0.17.0.mrpack";
    public const string WorldgenArtifactSha512 = "4631317E04F547AD72E93B9C40E8C6BEB2FDDDD081D9A63F36C62D24BB9EAD6A0FFC52257260FE584223C6E134120DF7E8B4B05C1E3D5BF24631EDB37F488014";
    public const string GuardArtifactFileName = "vanilla-2-plus-0.16.0.mrpack";
    public const string GuardArtifactSha512 = "19E98E3D10001E163FEA20870B0D63B462F62B717A0422D4DD87F7AF11B07160325BDBF851D3B3E581F13F389741A239B88CA9630C17927A96DCC7D83951B0AD";
    public const string PriorArtifactFileName = "vanilla-2-plus-0.14.0.mrpack";
    public const string PriorArtifactSha512 = "6B7D4560296AE0CB79E212B4D0DB18FDF0CA739C2AB7CBBED546430CD959D78E57735EC8336136E814DAEEA8F7E9C4848722F12000C2C231F391C9BAE1F8222B";
    public const string PreviousArtifactFileName = "vanilla-2-plus-0.13.0.mrpack";
    public const string PreviousArtifactSha512 = "86629C7E463D51DF8AD49F32D6A9AEC1D6534769144AA6BF12AA58B324E6A3F22388AB7FD5148578C0A9544CA14EF23654B54B5554D06E0C071B034C09906EED";
    public const string LegacyArtifactFileName = "vanilla-2-plus-0.12.0.mrpack";
    public const string LegacyArtifactSha512 = "2AC6213EF9DF36C8FFE7DFC36B90DBD4CE2CEB5EB740BCDFD95EDAD46AF0420ED6CF0959C134C99AE54D0BB972D39AD2E89C9AF4A7857C1A88D18E79F76C5A86";
    public const string OriginalArtifactFileName = "vanilla-2-plus-0.11.0.mrpack";
    public const string OriginalArtifactSha512 = "486B7B92266FA3C0ED7F88CBD99ED572BA71FB9902058889593A5FACDC08F29DBCD9DFF185A7BCCDB5001F3E2DC8165399958F721613188046591E1DCCF329F2";
    public static string[] InitialResourcePacks =>
    [
        ..TestPackRelease.InitialResourcePacks,
        "Semos Animations Lib 2.0.4.zip",
        "Freshly Modded 3.0.5.zip"
    ];
    public static string InitialOptions => TestPackRelease.BuildInitialOptions(InitialResourcePacks);
}
