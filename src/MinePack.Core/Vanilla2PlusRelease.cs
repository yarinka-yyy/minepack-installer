namespace MinePack.Core;

public static class Vanilla2PlusRelease
{
    public const string ArtifactFileName = "vanilla-2-plus-0.14.0.mrpack";
    public const string ArtifactRelativePath = "releases/vanilla-2-plus/" + ArtifactFileName;
    public const string ArtifactSha512 = "6B7D4560296AE0CB79E212B4D0DB18FDF0CA739C2AB7CBBED546430CD959D78E57735EC8336136E814DAEEA8F7E9C4848722F12000C2C231F391C9BAE1F8222B";
    public const string PackVersion = "0.14.0";
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
