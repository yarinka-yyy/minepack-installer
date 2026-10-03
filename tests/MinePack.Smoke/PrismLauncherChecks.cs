using MinePack.Core;

internal static class PrismLauncherChecks
{
    public static async Task RunAsync(string smokeRoot)
    {
        VerifyDecisions();
        VerifyPathAndConfigResolution(smokeRoot);
        VerifyTargetDeduplication();
        VerifyComponentPins();
        await VerifyCurrentPinnedPacksAsync(smokeRoot);
        VerifySelectedLauncherLifecycle(smokeRoot);
        Console.WriteLine("PASS: Prism discovery, pinned pack inventory/defaults, launcher metadata, and selected-target lifecycle");
    }

    private static void VerifyDecisions()
    {
        Equal(LauncherDecisionKind.Automatic,
            LauncherDiscovery.Decide(LauncherDiscoveryState.Found, 1, LauncherDiscoveryState.Absent, 0).Kind,
            "official-only installation is automatic");
        Equal(LauncherKind.Official,
            LauncherDiscovery.Decide(LauncherDiscoveryState.Found, 1, LauncherDiscoveryState.Absent, 0).LauncherKind,
            "official-only target selected");
        Equal(LauncherKind.Prism,
            LauncherDiscovery.Decide(LauncherDiscoveryState.Absent, 0, LauncherDiscoveryState.Found, 1).LauncherKind,
            "Prism-only target selected");
        Equal(LauncherDecisionKind.ChooseLauncher,
            LauncherDiscovery.Decide(LauncherDiscoveryState.Found, 1, LauncherDiscoveryState.Found, 1).Kind,
            "two launcher types require explicit choice");
        Equal(LauncherDecisionKind.ChooseInstallation,
            LauncherDiscovery.Decide(LauncherDiscoveryState.Absent, 0, LauncherDiscoveryState.Found, 2).Kind,
            "multiple independent Prism targets require explicit choice");
        Equal(LauncherDecisionKind.LocateOrRetry,
            LauncherDiscovery.Decide(LauncherDiscoveryState.Unknown, 0, LauncherDiscoveryState.Absent, 0).Kind,
            "unknown discovery never becomes absence");
        Equal(LauncherDecisionKind.LocateOrRetry,
            LauncherDiscovery.Decide(LauncherDiscoveryState.Absent, 0, LauncherDiscoveryState.Absent, 0).Kind,
            "no launchers require locate or retry");
    }

    private static void VerifyPathAndConfigResolution(string smokeRoot)
    {
        var root = Path.Combine(smokeRoot, "Prism данные", "Portable root");
        var executableDirectory = Path.Combine(root, "PrismLauncher");
        Directory.CreateDirectory(executableDirectory);
        var executable = Path.Combine(executableDirectory, "prismlauncher.exe");
        File.WriteAllBytes(executable, [0x4D, 0x5A]);
        var userData = Path.Combine(executableDirectory, "UserData");
        Directory.CreateDirectory(userData);
        File.WriteAllText(Path.Combine(executableDirectory, "portable.txt"), string.Empty);
        Equal(Path.GetFullPath(userData), LauncherDiscovery.ResolveDataRoot(executable),
            "portable UserData takes precedence over marker");
        Directory.Delete(userData, recursive: true);
        Equal(Path.GetFullPath(executableDirectory), LauncherDiscovery.ResolveDataRoot(executable),
            "portable marker selects executable directory");

        var argumentRoot = Path.Combine(root, "CLI root");
        Equal(Path.GetFullPath(argumentRoot), LauncherDiscovery.ResolveDataRoot(executable,
                ["--dir", argumentRoot]), "absolute --dir is retained");
        Equal(Path.GetFullPath(Path.Combine(executableDirectory, "relative root")),
            LauncherDiscovery.ResolveDataRoot(executable, ["--dir", "relative root"], executableDirectory),
            "relative --dir requires and uses verified working directory");
        Throws(() => LauncherDiscovery.ResolveDataRoot(executable, ["--dir", "relative root"]),
            "relative --dir without working directory is rejected");
        Throws(() => LauncherDiscovery.ResolveDataRoot(executable, ["--dir", "C:drive-relative"]),
            "drive-relative --dir is rejected");
        Throws(() => LauncherDiscovery.ResolveDataRoot(executable, ["--dir", "\\root-relative"]),
            "root-relative --dir is rejected");
        Throws(() => LauncherDiscovery.ResolveDataRoot(executable, environmentDataRoot: "relative-env-root"),
            "relative environment root is rejected");

        var dataRoot = Path.Combine(root, "data root");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "prismlauncher.cfg"),
            "[General]\r\nInstanceDir = \"Custom instances\"\r\n");
        var setting = LauncherDiscovery.ReadInstanceDirectorySetting(dataRoot);
        Equal("Custom instances", setting, "quoted InstanceDir is read from real config fixture");
        Equal(Path.Combine(dataRoot, "Custom instances"),
            LauncherDiscovery.ResolveInstancesRootFromSetting(dataRoot, setting),
            "parsed InstanceDir is not parsed as INI again");
        Throws(() => LauncherDiscovery.ResolveInstancesRoot(dataRoot, "[General]\nInstanceDir=C:drive-relative"),
            "drive-relative configured InstanceDir is rejected");
        Throws(() => LauncherDiscovery.ResolveInstancesRoot(dataRoot, "[General]\nInstanceDir=\\root-relative"),
            "root-relative configured InstanceDir is rejected");

        var lower = InstallationLayout.ComputePrismFingerprint(dataRoot.ToLowerInvariant(),
            Path.Combine(dataRoot, "Instances").ToLowerInvariant());
        var upper = InstallationLayout.ComputePrismFingerprint(dataRoot.ToUpperInvariant(),
            Path.Combine(dataRoot, "Instances").ToUpperInvariant());
        Equal(lower, upper, "Prism root fingerprint ignores Windows path casing");

        var recheckDataRoot = Path.Combine(root, "recheck-data");
        var recheckInstancesRoot = Path.Combine(recheckDataRoot, "instances");
        var recheckExeDirectory = Path.Combine(root, "recheck-app", "PrismLauncher");
        Directory.CreateDirectory(recheckInstancesRoot);
        Directory.CreateDirectory(recheckExeDirectory);
        var recheckExe = Path.Combine(recheckExeDirectory, "prismlauncher.exe");
        File.WriteAllBytes(recheckExe, [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(recheckDataRoot, "prismlauncher.cfg"),
            "[General]\r\nInstanceDir = instances\r\n");
        var recheckTarget = new PrismLauncherTarget(recheckExe, recheckDataRoot, recheckInstancesRoot,
            InstallationLayout.ComputePrismFingerprint(recheckDataRoot, recheckInstancesRoot), "fixture");
        LauncherDiscovery.RevalidatePrismTarget(recheckTarget);
        File.WriteAllText(Path.Combine(recheckDataRoot, "prismlauncher.cfg"),
            "[General]\r\nInstanceDir = changed-instances\r\n");
        try
        {
            LauncherDiscovery.RevalidatePrismTarget(recheckTarget);
            throw new InvalidOperationException("Expected a changed Prism InstanceDir to invalidate the target snapshot.");
        }
        catch (InstallerException ex) when (ex.Code == "PRISM_IDENTITY_CHANGED") { }
    }

    private static void VerifyTargetDeduplication()
    {
        var first = new PrismLauncherTarget(@"C:\Apps\PrismLauncher\prismlauncher.exe",
            @"C:\Data\Prism", @"C:\Data\Prism\instances", "fingerprint", "fixture");
        var duplicate = first with { ExecutablePath = @"c:\apps\prismlauncher\prismlauncher.exe" };
        var secondExecutable = first with { ExecutablePath = @"D:\Apps\PrismLauncher\prismlauncher.exe" };
        Equal(2, LauncherDiscovery.DeduplicateTargets([first, duplicate, secondExecutable]).Count,
            "same executable and root deduplicate while different executables remain separate");
        var mixedEvidence = LauncherDiscovery.CreatePrismScanResult(false, true, [first], "PRISM_IDENTITY_UNKNOWN");
        Equal(LauncherDiscoveryState.Unknown, mixedEvidence.State,
            "unverified candidate blocks automatic selection even when another target is verified");
        Equal("PRISM_IDENTITY_UNKNOWN", mixedEvidence.DiagnosticCode,
            "discovery preserves the specific uncertainty code");
        Equal(LauncherDiscoveryState.Found,
            LauncherDiscovery.CreatePrismScanResult(false, false, [first], null).State,
            "verified common-path candidate remains a positive discovery result");
    }

    private static void VerifyComponentPins()
    {
        var pack = PackArchive.Open(
            InstalledInstanceCatalog.PinnedArchive(TestPackRelease.PackVersion, AppContext.BaseDirectory).Path,
            TestPackRelease.ArtifactSha512);
        using var document = System.Text.Json.JsonDocument.Parse(PrismLauncherService.BuildComponentManifest(pack));
        var components = document.RootElement.GetProperty("components").EnumerateArray().ToArray();
        Equal("1", document.RootElement.GetProperty("formatVersion").GetRawText(), "Prism component format version");
        Equal(2, components.Length, "Minecraft and Fabric are the only pinned components");
        Equal(pack.MinecraftVersion, components[0].GetProperty("version").GetString(), "Minecraft component pin");
        Equal("net.minecraft", components[0].GetProperty("uid").GetString(), "Minecraft component id");
        Equal(pack.FabricLoaderVersion, components[1].GetProperty("version").GetString(), "Fabric component pin");
        Equal("net.fabricmc.fabric-loader", components[1].GetProperty("uid").GetString(), "Fabric component id");
        Equal("MinePack for " + pack.MinecraftVersion, LauncherProfile.ProfileName(pack.MinecraftVersion),
            "shared official and Prism profile name");
    }

    private static async Task VerifyCurrentPinnedPacksAsync(string smokeRoot)
    {
        var releases = new[]
        {
            (TestPackRelease.PackVersion, TestPackRelease.ArtifactSha512, TestPackRelease.InitialResourcePacks),
            (Vanilla2PlusRelease.PackVersion, Vanilla2PlusRelease.ArtifactSha512, Vanilla2PlusRelease.InitialResourcePacks)
        };
        foreach (var (packVersion, archiveHash, resourcePacks) in releases)
        {
            var (archivePath, pinnedHash) = InstalledInstanceCatalog.PinnedArchive(packVersion, AppContext.BaseDirectory);
            Equal(archiveHash, pinnedHash, $"{packVersion} Prism fixture uses its pinned archive hash");
            var pack = PackArchive.Open(archivePath, pinnedHash);
            Equal(packVersion, pack.VersionId, $"{packVersion} Prism fixture opens the pinned pack release");
            Equal(TestPackRelease.MinecraftVersion, pack.MinecraftVersion, $"{packVersion} Prism fixture pins Minecraft");
            Equal(TestPackRelease.FabricLoaderVersion, pack.FabricLoaderVersion, $"{packVersion} Prism fixture pins Fabric");

            var release = InstalledInstanceCatalog.KnownReleases.Single(item => item.PackVersion == packVersion);
            var fixtureRoot = Path.Combine(smokeRoot, "Prism pinned " + packVersion);
            var appDirectory = Path.Combine(fixtureRoot, "PrismLauncher");
            var dataRoot = Path.Combine(fixtureRoot, "data root");
            var instancesRoot = Path.Combine(dataRoot, "instances");
            Directory.CreateDirectory(appDirectory);
            Directory.CreateDirectory(instancesRoot);
            var executable = Path.Combine(appDirectory, "prismlauncher.exe");
            File.WriteAllBytes(executable, [0x4D, 0x5A]);
            File.WriteAllText(Path.Combine(dataRoot, "prismlauncher.cfg"), "[General]\r\nInstanceDir = instances\r\n");
            var target = new PrismLauncherTarget(executable, dataRoot, instancesRoot,
                InstallationLayout.ComputePrismFingerprint(dataRoot, instancesRoot), "fixture");
            var layout = InstallationLayout.PrismForTesting(target, release.InstanceDirectoryPrefix,
                Path.Combine(fixtureRoot, "state"), release);

            var inventoryPaths = pack.Files.Select(file => SafePath.Resolve(layout.GameDirectory, file.Path)).ToArray();
            True(inventoryPaths.Length > 0 && inventoryPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == pack.Files.Count,
                $"{packVersion} Prism inventory has unique pinned game paths");
            True(inventoryPaths.All(path => path.StartsWith(layout.GameDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
                $"{packVersion} Prism inventory remains under the nested minecraft directory");
            True(pack.Files.Any(file => file.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)) &&
                 inventoryPaths.Any(path => path.Contains(Path.DirectorySeparatorChar + "mods" + Path.DirectorySeparatorChar,
                     StringComparison.OrdinalIgnoreCase)),
                $"{packVersion} Prism inventory routes mods into minecraft/mods");

            var stage = Path.Combine(fixtureRoot, "staged overrides");
            Directory.CreateDirectory(stage);
            await pack.ExtractOverridesAsync(stage, CancellationToken.None);
            var initial = InitialConfiguration.Create(pack, stage);
            var options = System.Text.Encoding.UTF8.GetString(initial.Single(file => file.Path == "options.txt").Contents);
            Equal(TestPackRelease.BuildInitialOptions(resourcePacks), options,
                $"{packVersion} Prism initial options match the pinned resource pack order");
            Equal(1, options.Split("enableVsync:false", StringSplitOptions.None).Length - 1,
                $"{packVersion} Prism initial options disable VSync exactly once");
            True(InitialConfiguration.GetInitialPaths(pack).Contains("options.txt"),
                $"{packVersion} Prism declares options.txt as a restorable initial path");

            var metadata = PrismLauncherService.BuildComponentManifest(pack);
            using var componentsDocument = System.Text.Json.JsonDocument.Parse(metadata);
            var components = componentsDocument.RootElement.GetProperty("components").EnumerateArray().ToArray();
            Equal(pack.MinecraftVersion, components[0].GetProperty("version").GetString(),
                $"{packVersion} Prism metadata pins Minecraft");
            Equal(pack.FabricLoaderVersion, components[1].GetProperty("version").GetString(),
                $"{packVersion} Prism metadata pins Fabric");
        }
    }

    private static void VerifySelectedLauncherLifecycle(string smokeRoot)
    {
        var root = Path.Combine(smokeRoot, "Prism lifecycle controller");
        var firstDirectory = Path.Combine(root, "PrismLauncher");
        var dataRoot = Path.Combine(root, "Prism data");
        var secondData = Path.Combine(root, "Other data");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(secondData);
        var firstExe = Path.Combine(firstDirectory, "prismlauncher.exe");
        File.WriteAllBytes(firstExe, [0x4D, 0x5A]);
        var first = new PrismLauncherTarget(firstExe, dataRoot, Path.Combine(dataRoot, "instances"),
            InstallationLayout.ComputePrismFingerprint(dataRoot, Path.Combine(dataRoot, "instances")), "fixture");
        var second = new PrismLauncherTarget(firstExe, secondData, Path.Combine(secondData, "instances"),
            InstallationLayout.ComputePrismFingerprint(secondData, Path.Combine(secondData, "instances")), "fixture");
        Equal(true, LauncherDiscovery.TryMatchPrismProcess(first, firstExe, [firstExe, "--dir", dataRoot], out var selectedRoot) && selectedRoot,
            "same Prism executable with matching --dir belongs to selected target");
        Equal(true, LauncherDiscovery.TryMatchPrismProcess(first, firstExe, [firstExe, "--dir", secondData], out var otherRoot) && !otherRoot,
            "same Prism executable with another --dir is left running");
        Equal(false, LauncherDiscovery.TryMatchPrismProcess(first, firstExe, [firstExe], out _),
            "same Prism executable without verified --dir remains unidentified");
        Equal(true, LauncherDiscovery.TryReadVerifiedPrismProcessDataRoot(firstExe, [firstExe, "--dir", dataRoot], out var resolvedRoot) &&
                    Path.GetFullPath(dataRoot).Equals(resolvedRoot, StringComparison.OrdinalIgnoreCase),
            "verified process data root is read from allowlisted --dir only");
        var secondExeDirectory = Path.Combine(root, "Other Prism Launcher");
        Directory.CreateDirectory(secondExeDirectory);
        var secondExe = Path.Combine(secondExeDirectory, "prismlauncher.exe");
        File.WriteAllBytes(secondExe, [0x4D, 0x5A]);
        Equal(true, LauncherDiscovery.TryReadVerifiedPrismProcessDataRoot(secondExe, [secondExe, "--dir", dataRoot], out var sharedRoot) &&
                    Path.GetFullPath(dataRoot).Equals(sharedRoot, StringComparison.OrdinalIgnoreCase),
            "a second Prism executable with the same data root is recognized for write blocking");
        var platform = new FakePrismPlatform(first, second);
        var controller = new PrismLauncherController(platform);
        controller.CloseBeforeInstallAsync(first).GetAwaiter().GetResult();
        Equal(true, platform.Processes[0].Exited, "selected Prism process receives a bounded close request");
        Equal(false, platform.Processes[1].Exited, "other Prism data root is left running");
        controller.Start(first);
        Equal(first, platform.Started, "Prism start keeps the exact selected data root");

        var leftoverProcesses = new[] { new FakePrismProcess(), new FakePrismProcess() };
        var cleanupPlatform = new FakePrismPlatform(first, second) { PostCloseProcesses = leftoverProcesses };
        try
        {
            new PrismLauncherController(cleanupPlatform).CloseBeforeInstallAsync(first).GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected a Prism process to remain after the close request.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_CLOSE_TIMEOUT") { }
        Equal(1, leftoverProcesses[0].DisposeCount, "first post-close process handle is disposed");
        Equal(1, leftoverProcesses[1].DisposeCount, "all post-close process handles are disposed after a live process is found");

        var sharedRootPlatform = new FakePrismPlatform(first, second) { Unidentified = true };
        try
        {
            new PrismLauncherController(sharedRootPlatform).CloseBeforeInstallAsync(first).GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected a second executable on the selected data root to block writes.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_IDENTITY_UNKNOWN") { }
        Equal(false, sharedRootPlatform.Processes[0].Exited,
            "a second executable on the selected data root blocks writes without closing the selected process");
    }

    private static void Throws(Action action, string name)
    {
        try { action(); }
        catch (InstallerException) { return; }
        throw new InvalidOperationException("Expected rejection: " + name);
    }

    private static void True(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'.");
    }

    private sealed class FakePrismPlatform(PrismLauncherTarget selected, PrismLauncherTarget other) : IPrismLauncherPlatform
    {
        private int _findCount;
        public FakePrismProcess[] Processes { get; } = [new(), new()];
        public bool Unidentified { get; init; }
        public IReadOnlyList<FakePrismProcess>? PostCloseProcesses { get; init; }
        public PrismLauncherTarget? Started { get; private set; }

        public IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(PrismLauncherTarget target, out bool unidentified)
        {
            unidentified = Unidentified;
            if (_findCount++ == 1 && PostCloseProcesses is not null) return PostCloseProcesses;
            if (target == selected && !Processes[0].Exited) return [Processes[0]];
            if (target == other && !Processes[1].Exited) return [Processes[1]];
            return [];
        }

        public void Start(PrismLauncherTarget target) => Started = target;
    }

    private sealed class FakePrismProcess : IMinecraftLauncherProcess
    {
        public bool Exited { get; private set; }
        public int DisposeCount { get; private set; }
        public bool HasExited => Exited;
        public bool RequestClose() { Exited = true; return true; }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => DisposeCount++;
    }
}
