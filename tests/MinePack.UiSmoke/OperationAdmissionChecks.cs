using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using MinePack.Core;
using MinePack.Installer;

internal static partial class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void VerifyOperationAdmissionAndPendingRecovery(string output)
    {
        var fixtureRoot = NewFixtureRoot(output);
        var instance = Path.Combine(fixtureRoot, "instances", "pending-fixture");
        Directory.CreateDirectory(instance);
        var pack = PackArchive.Open(Path.Combine(AppContext.BaseDirectory, TestPackRelease.ArtifactRelativePath),
            TestPackRelease.ArtifactSha512);
        var transactionType = typeof(InstallService).Assembly.GetType("MinePack.Core.ManagedFileTransaction")!;
        transactionType.GetMethod("BeginRepair", [typeof(string), typeof(string), typeof(PackArchive), typeof(OperationLog)])!
            .Invoke(null, [fixtureRoot, instance, pack, null]);
        var journalPath = Path.Combine(instance + ".minepack-transaction", "journal.json");
        var originalJournal = File.ReadAllBytes(journalPath);
        var window = CreateOperationFixture(fixtureRoot, new AdmissionFixturePlatform());
        try
        {
            foreach (var operation in new[] { "ConfigureLauncher", "ImportWorlds", "Repair" })
            {
                var task = RunFixtureOperation(window, operation);
                AcceptOfficialFixtureChoice(window, task);
                WaitFor(task, $"{operation} pending transaction is handled");
                Require(window.OperationCancellationForUiSmoke is null,
                    $"{operation} releases admission after a pending-transaction error");
                Require(window.StatusBox.Text.Contains(LocalizedText.Get("TransactionRecoveryRequired", ""), StringComparison.Ordinal),
                    $"{operation} presents the pending transaction error instead of faulting its UI task");
                Require(originalJournal.SequenceEqual(File.ReadAllBytes(journalPath)),
                    $"{operation} preserves an incomplete fixture without a trusted manifest");
                if (operation == "Repair")
                    Require(window.OperationProgressOverlay.Visibility == Visibility.Visible,
                        "Repair admits the pending target to the recovery path before safely rejecting its missing manifest");
            }
        }
        finally { window.Close(); }

        using var scanBarrier = new ManualResetEventSlim(false);
        var platform = new AdmissionFixturePlatform(scanBarrier);
        window = CreateOperationFixture(NewFixtureRoot(output), platform);
        try
        {
            var first = RunFixtureOperation(window, "Repair");
            Require(window.OperationCancellationForUiSmoke is not null,
                "operation is busy synchronously before asynchronous launcher discovery");
            var second = RunFixtureOperation(window, "ConfigureLauncher");
            WaitFor(second, "repeated operation is ignored while discovery is pending");
            PumpUntil(() => Volatile.Read(ref platform.Scans) >= 1, "first operation enters fixture discovery");
            Require(Volatile.Read(ref platform.Scans) == 1,
                "repeated operation cannot start a second preflight scan or overwrite the chooser");
            scanBarrier.Set();
            PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
                "first operation owns the only chooser");
            window.LauncherChoiceCancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WaitFor(first, "first operation completes after chooser cancellation");
            Require(window.OperationCancellationForUiSmoke is null && window.MainContentScrollViewer.IsEnabled,
                "chooser cancellation releases admission and restores controls");
            Require(window.InlineNoticeText.Text == LocalizedText.Get("OperationStoppedStatus"),
                "cancelled preflight reports cancellation instead of continuing to display a busy message");

            var original = window.ShowLauncherChoiceForUiSmokeAsync(InventoryWithBothLaunchers());
            var replacement = window.ShowLauncherChoiceForUiSmokeAsync(InventoryWithBothLaunchers());
            WaitFor(original, "replaced chooser completes its original task");
            Require(original.Result is null && !replacement.IsCompleted &&
                    window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
                "replaced chooser cleanup cannot close the newer chooser session");
            window.Close();
            WaitFor(replacement, "closing the window completes the current chooser");
            Require(replacement.Result is null, "window close cancels the remaining chooser without orphaning it");
        }
        finally { scanBarrier.Set(); window.Close(); }
        Console.WriteLine("Operation admission, pending recovery errors, repeated actions, and chooser ownership passed.");
    }

    private static MainWindow CreateOperationFixture(string root, IMinecraftLauncherPlatform platform)
    {
        var window = CreateWindow("ru-RU", root);
        typeof(MainWindow).GetField("_launcherController", PrivateInstance)!
            .SetValue(window, new MinecraftLauncherController(platform));
        return window;
    }

    private static Task RunFixtureOperation(MainWindow window, string operation)
    {
        var operationType = typeof(MainWindow).GetNestedType("Operation", BindingFlags.NonPublic)!;
        return (Task)typeof(MainWindow).GetMethod("RunOperationAsync", PrivateInstance)!
            .Invoke(window, [Enum.Parse(operationType, operation), null, null, null])!;
    }

    private static void AcceptOfficialFixtureChoice(MainWindow window, Task operation)
    {
        PumpUntil(() => operation.IsCompleted || window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
            "operation resolves its fixture launcher");
        if (operation.IsCompleted) return;
        window.OfficialLauncherChoice.IsChecked = true;
        window.LauncherTargetComboBox.SelectedIndex = 0;
        Require(window.LauncherChoiceContinueButton.IsEnabled, "fixture official target is selectable");
        window.LauncherChoiceContinueButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private sealed class AdmissionFixturePlatform(ManualResetEventSlim? scanBarrier = null) : IMinecraftLauncherPlatform
    {
        public int Scans;
        public IReadOnlyList<MinecraftLauncherTarget> FindTargets()
        {
            Interlocked.Increment(ref Scans);
            if (scanBarrier is not null && !scanBarrier.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Fixture discovery was not released.");
            return [new(MinecraftLauncherKind.Win32, @"C:\MinePackUiSmoke\FixtureA.exe"),
                new(MinecraftLauncherKind.Win32, @"C:\MinePackUiSmoke\FixtureB.exe")];
        }
        public IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(MinecraftLauncherTarget target) => [];
        public bool HasUnidentifiedLauncherProcess() => false;
        public void Start(MinecraftLauncherTarget target) => throw new InvalidOperationException("The UI fixture must never start a launcher.");
    }
}
