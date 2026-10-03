using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinePack.Core;
using MinePack.Installer;

internal static class Program
{
    private static Application? _application;
    private static bool _reportedDpi;

    [STAThread]
    private static int Main()
    {
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var repositoryRoot = FindRepositoryRoot();
            var output = Path.Combine(repositoryRoot, "artifacts", "build-1.6.1", "ui-smoke");
            Directory.CreateDirectory(output);

            VerifyOverlayDismissalAndStaleResults(output);
            VerifyOperationProgressLifecycle(output);
            SaveChooserPreviews(output);
            SaveOperationProgressPreviews(output);
            Console.WriteLine("All WPF UI smoke scenarios passed.");
            Console.WriteLine("Native Windows display scaling was not changed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            _application?.Shutdown();
        }
    }

    private static void VerifyOperationProgressLifecycle(string output)
    {
        var fixtureRoot = NewFixtureRoot(output);
        Directory.CreateDirectory(fixtureRoot);
        var window = CreateWindow("ru-RU", fixtureRoot);
        try
        {
            Require(window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "idle window has no progress overlay");
            Require(window.MainContentScrollViewer.IsEnabled && window.InstallButton.IsDefault,
                "idle main content and default Install action remain available");

            var canceledBeforeAdmission = window.BeginOperationForUiSmoke();
            var cancelledChoice = window.ShowLauncherChoiceForUiSmokeAsync(InventoryWithBothLaunchers());
            PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
                "chooser opens before operation progress");
            Require(window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "progress remains hidden while choosing a launcher");
            var backdrop = RaiseMouse(window.LauncherChoiceOverlay, window.LauncherChoiceOverlay);
            Require(backdrop.Handled, "launcher chooser cancellation is handled");
            WaitFor(cancelledChoice, "pre-admission launcher choice cancellation");
            Require(cancelledChoice.Result is null && window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "cancelled launcher choice does not show success or progress");
            window.StatusBox.Text = LocalizedText.Get("OperationStoppedStatus");
            window.FinishOperationForUiSmoke("cancelled");
            Require(window.OperationProgressOverlay.Visibility == Visibility.Collapsed &&
                    window.InlineNoticeText.Visibility == Visibility.Visible &&
                    window.InlineNoticeText.Text == LocalizedText.Get("OperationStoppedStatus"),
                "cancelled chooser reports inline without opening progress or a stale result");
            Require(!canceledBeforeAdmission.IsCancellationRequested,
                "cancelling launcher selection does not cancel an already-dismissed progress token");

            var inventory = InventoryWithBothLaunchers();
            var acceptedToken = window.BeginOperationForUiSmoke();
            var acceptedChoice = window.ShowLauncherChoiceForUiSmokeAsync(inventory);
            PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
                "accepted launcher chooser opens");
            Require(window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "progress is absent until the launcher is explicitly accepted");
            window.OfficialLauncherChoice.IsChecked = true;
            Require(window.LauncherChoiceContinueButton.IsEnabled,
                "one verified official target can be explicitly continued");
            window.LauncherChoiceContinueButton.RaiseEvent(
                new RoutedEventArgs(ButtonBase.ClickEvent, window.LauncherChoiceContinueButton));
            WaitFor(acceptedChoice, "official launcher choice acceptance");
            Require(acceptedChoice.Result is { Kind: LauncherKind.Official },
                "explicit chooser returns the accepted launcher target");
            window.PresentOperationProgressForUiSmoke();
            PumpOnce();

            Require(window.OperationProgressOverlay.Visibility == Visibility.Visible,
                "accepted target followed by root admission opens progress");
            Require(!window.MainContentScrollViewer.IsEnabled && !window.InstallButton.IsDefault,
                "visible progress disables background controls and the default Install action");
            Require(ReferenceEquals(window.OperationCancellationForUiSmoke, acceptedToken),
                "accepted operation retains its cancellation token");

            var enter = RaiseKey(window.OperationProgressOverlay, Key.Enter);
            Require(enter.Handled && !acceptedToken.IsCancellationRequested,
                "Enter on progress cannot click through to Install or cancel the operation");
            RaiseMouse(window.OperationProgressCard, window.OperationProgressCard);
            Require(window.OperationProgressOverlay.Visibility == Visibility.Visible,
                "clicking inside the progress card does not dismiss it");
            var escape = RaiseKey(window.OperationProgressOverlay, Key.Escape);
            Require(escape.Handled && window.OperationProgressOverlay.Visibility == Visibility.Collapsed &&
                    ReferenceEquals(window.OperationCancellationForUiSmoke, acceptedToken) &&
                    !acceptedToken.IsCancellationRequested,
                "Escape hides progress without cancelling the running operation");
            window.InstallButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.InstallButton));
            PumpOnce();
            Require(window.OperationProgressOverlay.Visibility == Visibility.Visible &&
                    ReferenceEquals(window.OperationCancellationForUiSmoke, acceptedToken),
                "Install reopens progress after Escape");
            backdrop = RaiseMouse(window.OperationProgressOverlay, window.OperationProgressOverlay);
            Require(backdrop.Handled && window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "outside click hides the running progress overlay");
            Require(ReferenceEquals(window.OperationCancellationForUiSmoke, acceptedToken) &&
                    !acceptedToken.IsCancellationRequested,
                "outside dismissal preserves the running operation and its token");
            Require(window.MainContentScrollViewer.IsEnabled && window.InstallButton.IsEnabled &&
                    Equals(window.InstallButton.Content, LocalizedText.Get("UiShowProgress")),
                "hidden progress leaves Install enabled as a show-progress action");

            window.InstallButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.InstallButton));
            PumpOnce();
            Require(window.OperationProgressOverlay.Visibility == Visibility.Visible &&
                    ReferenceEquals(window.OperationCancellationForUiSmoke, acceptedToken),
                "Install reopens the same operation without starting another one");

            backdrop = RaiseMouse(window.OperationProgressOverlay, window.OperationProgressOverlay);
            Require(backdrop.Handled && window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "progress can be hidden before its final refresh");
            window.PrepareOperationFinishForUiSmoke();
            Require(ReferenceEquals(window.OperationCancellationForUiSmoke, acceptedToken),
                "operation remains busy while final launcher refresh is pending");
            window.InstallButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.InstallButton));
            PumpOnce();
            Require(window.OperationProgressOverlay.Visibility == Visibility.Visible &&
                    ReferenceEquals(window.OperationCancellationForUiSmoke, acceptedToken),
                "Install cannot start a second operation during final refresh");

            window.StatusBox.Text = LocalizedText.Get("UiOperationInstallSuccess", "Vanilla Plus", "fixture",
                LocalizedText.Get("UiLauncherOptionOfficial"));
            window.FinishOperationForUiSmoke("completed");
            Require(window.OperationProgressOverlay.Visibility == Visibility.Visible &&
                    window.StateHeading.Text == LocalizedText.Get("UiOperationInstalledHeading") &&
                    window.ProgressLabel.Text == LocalizedText.Get("InstallComplete"),
                "full install completion shows an installed heading and completed progress label");
            RaiseMouse(window.OperationProgressCard, window.OperationProgressCard);
            window.CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.CancelButton));
            Require(window.OperationProgressOverlay.Visibility == Visibility.Collapsed &&
                    window.MainContentScrollViewer.IsEnabled && window.InlineNoticeText.Visibility == Visibility.Visible,
                "the explicit close button dismisses the completed result and leaves a visible notice");

            window.BeginOperationForUiSmoke();
            Require(window.OperationProgressOverlay.Visibility == Visibility.Collapsed &&
                    window.InlineNoticeText.Visibility == Visibility.Collapsed &&
                    window.StatusBox.Text == LocalizedText.Get("OperationWait"),
                "a new operation clears the previous result");
            window.StateHeading.Text = LocalizedText.Get("OperationFailedHeading");
            window.StatusBox.Text = "fixture failure";
            window.PresentOperationProgressForUiSmoke();
            window.FinishOperationForUiSmoke("failed");
            Require(window.StateHeading.Text == LocalizedText.Get("OperationFailedHeading") &&
                    window.StateHeading.Text != LocalizedText.Get("UiOperationInstalledHeading"),
                "failure is not presented as an installed pack");
            backdrop = RaiseMouse(window.OperationProgressOverlay, window.OperationProgressOverlay);
            Require(backdrop.Handled && window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "outside click closes a terminal error result");

            window.BeginOperationForUiSmoke();
            var cancelledToken = window.OperationCancellationForUiSmoke!;
            window.PresentOperationProgressForUiSmoke();
            window.CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.CancelButton));
            Require(cancelledToken.IsCancellationRequested, "explicit Cancel uses the current cancellable token");
            window.StateHeading.Text = LocalizedText.Get("OperationCancelledHeading");
            window.StatusBox.Text = LocalizedText.Get("OperationCancelledStatus");
            window.FinishOperationForUiSmoke("cancelled");
            Require(window.StateHeading.Text == LocalizedText.Get("OperationCancelledHeading"),
                "cancelled operation remains distinct from success");
            escape = RaiseKey(window.OperationProgressOverlay, Key.Escape);
            Require(escape.Handled && window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "Escape closes a terminal cancellation result");

            window.BeginOperationForUiSmoke();
            window.PresentOperationProgressForUiSmoke();
            window.StatusBox.Text = LocalizedText.Get("InstalledProfilePendingStatus");
            window.FinishOperationForUiSmoke("profile_pending");
            Require(window.StateHeading.Text == LocalizedText.Get("PackInstalledProfilePending"),
                "profile-pending install remains distinct from complete success");
            window.CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.CancelButton));

            var automatic = MainWindow.MakeAutomaticSelectionForUiSmoke(LauncherKind.Official,
                InventoryWithOfficialOnly());
            Require(automatic is { Kind: LauncherKind.Official },
                "one verified official target produces an automatic selection");
            window.BeginOperationForUiSmoke();
            Require(window.OperationProgressOverlay.Visibility == Visibility.Collapsed,
                "automatic resolution alone does not expose progress before admission");
            window.PresentOperationProgressForUiSmoke();
            Require(window.OperationProgressOverlay.Visibility == Visibility.Visible,
                "automatically resolved target can enter the same progress presentation");
            window.FinishOperationForUiSmoke("failed");
            window.CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.CancelButton));
            Console.WriteLine("Operation progress lifecycle, dismissal, reopen, and terminal states passed.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyOverlayDismissalAndStaleResults(string output)
    {
        var fixtureRoot = NewFixtureRoot(output);
        var window = CreateWindow("ru-RU", fixtureRoot);
        try
        {
            var inventory = InventoryWithBothLaunchers();
            var choice = window.ShowLauncherChoiceForUiSmokeAsync(inventory);
            PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible, "launcher chooser opens");
            Require(!window.MainContentScrollViewer.IsEnabled, "launcher chooser leaves the main content disabled");
            Require(!window.LauncherChoiceContinueButton.IsEnabled, "no launcher choice is confirmed by default");

            RaiseMouse(window.LauncherChoiceCard, window.LauncherChoiceCard);
            Require(window.LauncherChoiceOverlay.Visibility == Visibility.Visible, "card background does not dismiss chooser");
            RaiseMouse(window.LauncherChoiceCard.Child, window.LauncherChoiceCard.Child);
            Require(window.LauncherChoiceOverlay.Visibility == Visibility.Visible, "card child does not dismiss chooser");

            var outside = RaiseMouse(window.LauncherChoiceOverlay, window.LauncherChoiceOverlay);
            Require(outside.Handled, "launcher backdrop dismissal consumes the click");
            WaitFor(choice, "launcher chooser cancellation");
            Require(choice.Result is null, "outside click cancels launcher choice");
            Require(window.LauncherChoiceOverlay.Visibility == Visibility.Collapsed, "launcher chooser closes after cancellation");
            Require(window.MainContentScrollViewer.IsEnabled, "main content is restored after chooser cancellation");

            VerifyLauncherTargetPopupClick(window);

            var secondChoice = window.ShowLauncherChoiceForUiSmokeAsync(inventory);
            PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible, "second launcher chooser opens");
            var scan = new TaskCompletionSource<MainWindow.LauncherInventory>(TaskCreationOptions.RunContinuationsAsynchronously);
            var rescan = window.RescanLauncherChoicesForUiSmokeAsync(scan.Task);
            PumpUntil(() => window.LauncherChoiceStatus.Text == LocalizedText.Get("UiLauncherChoiceUnknown"), "rescan starts");
            outside = RaiseMouse(window.LauncherChoiceOverlay, window.LauncherChoiceOverlay);
            Require(outside.Handled, "rescan session can be dismissed");
            WaitFor(secondChoice, "second chooser cancellation");
            var statusAfterClose = window.LauncherChoiceStatus.Text;
            scan.SetResult(EmptyInventory("late-rescan"));
            WaitFor(rescan, "late rescan completion");
            Require(!rescan.Result, "late rescan is ignored after chooser cancellation");
            Require(window.PrismLauncherChoice.IsEnabled, "late rescan does not change stale radio choices");
            Require(window.LauncherChoiceStatus.Text == statusAfterClose, "late rescan does not update closed chooser status");

            var thirdChoice = window.ShowLauncherChoiceForUiSmokeAsync(inventory);
            PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible, "third launcher chooser opens");
            var oldSession = window.CaptureLauncherChoiceSessionForUiSmoke()
                ?? throw new InvalidOperationException("No active launcher choice session was captured.");
            outside = RaiseMouse(window.LauncherChoiceOverlay, window.LauncherChoiceOverlay);
            Require(outside.Handled, "locate session can be dismissed");
            WaitFor(thirdChoice, "third chooser cancellation");
            var located = new LauncherScanResult(LauncherDiscoveryState.Absent, [], "late-locate-fixture");
            var fourthChoice = window.ShowLauncherChoiceForUiSmokeAsync(inventory);
            PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible, "fourth launcher chooser opens");
            Require(!window.ApplyLocatedPrismResultForUiSmoke(oldSession, located), "late locate result is ignored for its closed session");
            Require(window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
                "late locate cannot close a newer chooser session");
            Require(window.PrismLauncherChoice.IsEnabled, "late locate does not alter later radio state");
            outside = RaiseMouse(window.LauncherChoiceOverlay, window.LauncherChoiceOverlay);
            Require(outside.Handled, "new chooser session remains dismissible");
            WaitFor(fourthChoice, "fourth chooser cancellation");
            Require(!File.Exists(Path.Combine(fixtureRoot, "installer-preferences.json")),
                "cancelled launcher choices do not write preferences");

            VerifyNestedConfirmation(window, fixtureRoot);
            VerifyLateCleanup(window, fixtureRoot);
            Console.WriteLine("Overlay dismissal, nested confirmation, and stale async guards passed.");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifyLauncherTargetPopupClick(MainWindow window)
    {
        var choice = window.ShowLauncherChoiceForUiSmokeAsync(InventoryWithMultiplePrismTargets());
        PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible, "multi-target chooser opens");
        window.PrismLauncherChoice.IsChecked = true;
        PumpUntil(() => window.LauncherTargetComboBox.Visibility == Visibility.Visible,
            "multiple Prism targets expose target selector");
        Require(window.LauncherTargetComboBox.Items.Count == 2, "target selector contains both Prism installations");
        window.LauncherTargetComboBox.IsDropDownOpen = true;
        PumpUntil(() => window.LauncherTargetComboBox.ItemContainerGenerator.ContainerFromIndex(0) is ComboBoxItem,
            "WPF target popup creates its item container");
        var item = (ComboBoxItem)window.LauncherTargetComboBox.ItemContainerGenerator.ContainerFromIndex(0);
        RaiseMouse(item, item);
        Require(window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
            "clicking a real popup item does not dismiss the chooser");

        window.LauncherTargetComboBox.SelectedIndex = 1;
        PumpOnce();
        Require(window.LauncherChoiceOverlay.Visibility == Visibility.Visible,
            "popup selection leaves the chooser open for explicit confirmation");
        Require(window.LauncherTargetComboBox.SelectedIndex == 1 && window.LauncherChoiceContinueButton.IsEnabled,
            "a selected Prism target remains available to Continue");

        window.LauncherChoiceCancelButton.RaiseEvent(
            new RoutedEventArgs(ButtonBase.ClickEvent, window.LauncherChoiceCancelButton));
        WaitFor(choice, "multi-target chooser cancellation");
        Require(choice.Result is null, "multi-target popup test cancels without installing");
    }

    private static void VerifyNestedConfirmation(MainWindow window, string fixtureRoot)
    {
        var release = InstalledInstanceCatalog.KnownReleases[0];
        var trusted = new InstalledInstanceEntry(Path.Combine(fixtureRoot, "trusted-fixture"),
            "trusted-fixture", InstalledInstanceState.Trusted, release, null);
        window.ShowDeleteOverlayForUiSmoke(trusted);
        window.DeleteManagedFilesButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, window.DeleteManagedFilesButton));
        PumpUntil(() => window.UninstallConfirmOverlay.Visibility == Visibility.Visible, "uninstall confirmation opens");
        Require(window.DeleteInstanceOverlay.Visibility == Visibility.Visible, "delete list remains underneath confirmation");
        Require(!window.DeleteInstanceOverlay.IsEnabled && !window.MainContentScrollViewer.IsEnabled,
            "confirmation keeps lower and main content unavailable");

        RaiseMouse(window.UninstallConfirmCard, window.UninstallConfirmCard);
        Require(window.UninstallConfirmOverlay.Visibility == Visibility.Visible, "confirmation card background does not dismiss");
        var outside = RaiseMouse(window.UninstallConfirmOverlay, window.UninstallConfirmOverlay);
        Require(outside.Handled, "confirmation backdrop consumes its click");
        PumpUntil(() => window.UninstallConfirmOverlay.Visibility == Visibility.Collapsed, "confirmation backdrop closes top overlay");
        Require(window.DeleteInstanceOverlay.Visibility == Visibility.Visible, "confirmation dismissal leaves delete list open");
        Require(window.DeleteInstanceOverlay.IsEnabled && !window.MainContentScrollViewer.IsEnabled,
            "only the lower dialog is restored after nested cancellation");
        Require(!window.HasPendingDeleteActionForUiSmoke, "nested cancellation clears the pending uninstall action");
        PumpOnce();
        Require(ReferenceEquals(Keyboard.FocusedElement, window.DeleteManagedFilesButton),
            "uninstall cancellation restores focus to its originating button");

        outside = RaiseMouse(window.DeleteInstanceOverlay, window.DeleteInstanceOverlay);
        Require(outside.Handled && window.DeleteInstanceOverlay.Visibility == Visibility.Collapsed,
            "delete list backdrop closes the lower dialog");

        var residue = new InstalledInstanceEntry(Path.Combine(fixtureRoot, "residue-fixture"),
            "residue-fixture", InstalledInstanceState.Residue, null, "FIXTURE_RESIDUE");
        window.ShowDeleteOverlayForUiSmoke(residue);
        window.ShowResidueConfirmationForUiSmoke(residue);
        Require(window.UninstallConfirmOverlay.Visibility == Visibility.Visible, "residue confirmation opens");
        outside = RaiseMouse(window.UninstallConfirmOverlay, window.UninstallConfirmOverlay);
        Require(outside.Handled, "residue confirmation backdrop consumes its click");
        PumpUntil(() => window.UninstallConfirmOverlay.Visibility == Visibility.Collapsed, "residue confirmation closes");
        Require(window.DeleteInstanceOverlay.Visibility == Visibility.Visible && !window.MainContentScrollViewer.IsEnabled,
            "residue confirmation leaves the delete list modal");
        PumpOnce();
        Require(ReferenceEquals(Keyboard.FocusedElement, window.DeleteInstanceListBox),
            "residue cancellation restores focus to the list");
        RaiseMouse(window.DeleteInstanceOverlay, window.DeleteInstanceOverlay);
    }

    private static void VerifyLateCleanup(MainWindow window, string fixtureRoot)
    {
        var residue = new InstalledInstanceEntry(Path.Combine(fixtureRoot, "late-residue"),
            "late-residue", InstalledInstanceState.Residue, null, "FIXTURE_RESIDUE");
        window.ShowDeleteOverlayForUiSmoke(residue);
        var generation = window.BeginCleanupPreparationForUiSmoke(residue);
        var status = window.StatusBox.Text;
        var outside = RaiseMouse(window.DeleteInstanceOverlay, window.DeleteInstanceOverlay);
        Require(outside.Handled && window.DeleteInstanceOverlay.Visibility == Visibility.Collapsed,
            "delete overlay closes while cleanup preparation is pending");
        Require(!window.ApplyCleanupFailureForUiSmoke(residue, generation, "late-cleanup-fixture"),
            "late cleanup result is discarded after close");
        Require(window.StatusBox.Text == status, "late cleanup does not mutate status after close");
        Require(!window.HasPendingDeleteActionForUiSmoke, "closing delete overlay clears pending action");
    }

    private static void SaveChooserPreviews(string output)
    {
        foreach (var cultureName in new[] { "ru-RU", "en-US", "zh-CN" })
        {
            foreach (var (width, height) in new[] { (710d, 610d), (850d, 730d) })
            {
                var fixtureRoot = NewFixtureRoot(output);
                var window = CreateWindow(cultureName, fixtureRoot, width, height);
                try
                {
                    var choice = window.ShowLauncherChoiceForUiSmokeAsync(InventoryWithBothLaunchers());
                    PumpUntil(() => window.LauncherChoiceOverlay.Visibility == Visibility.Visible, $"{cultureName} chooser opens");
                    PumpOnce();
                    window.UpdateLayout();
                    SaveWindowPng(window, Path.Combine(output, $"launcher-choice-{cultureName}-{width:0}x{height:0}.png"));
                    var outside = RaiseMouse(window.LauncherChoiceOverlay, window.LauncherChoiceOverlay);
                    Require(outside.Handled, $"{cultureName} chooser preview can be dismissed");
                    WaitFor(choice, $"{cultureName} chooser closes");
                }
                finally
                {
                    window.Close();
                }
            }
        }
    }

    private static void SaveOperationProgressPreviews(string output)
    {
        foreach (var cultureName in new[] { "ru-RU", "en-US", "zh-CN" })
        {
            foreach (var (width, height) in new[] { (710d, 610d), (850d, 730d) })
            {
                SaveProgressState(output, cultureName, width, height, "idle");
                SaveProgressState(output, cultureName, width, height, "running");
                SaveProgressState(output, cultureName, width, height, "success");
            }
        }
    }

    private static void SaveProgressState(string output, string cultureName, double width, double height, string state)
    {
        var fixtureRoot = NewFixtureRoot(output);
        var window = CreateWindow(cultureName, fixtureRoot, width, height);
        try
        {
            if (state != "idle")
            {
                window.BeginOperationForUiSmoke();
                window.PresentOperationProgressForUiSmoke();
                if (state == "success")
                {
                    window.StatusBox.Text = LocalizedText.Get("UiOperationInstallSuccess", "Vanilla Plus", "fixture",
                        LocalizedText.Get("UiLauncherOptionOfficial"));
                    window.FinishOperationForUiSmoke("completed");
                }
            }
            PumpOnce();
            window.UpdateLayout();
            SaveWindowPng(window, Path.Combine(output,
                $"operation-{state}-{cultureName}-{width:0}x{height:0}.png"));
            if (window.OperationCancellationForUiSmoke is not null)
                window.FinishOperationForUiSmoke("failed");
        }
        finally
        {
            window.Close();
        }
    }

    private static void SaveWindowPng(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        if (!_reportedDpi)
        {
            Console.WriteLine($"WPF preview DPI scale: {dpi.DpiScaleX:0.##}x{dpi.DpiScaleY:0.##} (current Windows display setting).");
            _reportedDpi = true;
        }
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static MainWindow CreateWindow(string cultureName, string fixtureRoot, double width = 850, double height = 730)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        var window = new MainWindow(fixtureRoot)
        {
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 100,
            Top = 100
        };
        if (cultureName == "zh-CN") window.FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, SimSun");
        window.Show();
        PumpOnce();
        return window;
    }

    private static MainWindow.LauncherInventory InventoryWithBothLaunchers()
    {
        var prism = new PrismLauncherTarget(
            @"C:\MinePackUiSmoke\PrismLauncher\prismlauncher.exe",
            @"C:\MinePackUiSmoke\PrismLauncher",
            @"C:\MinePackUiSmoke\PrismLauncher\instances",
            new string('A', 64), "fixture");
        return new MainWindow.LauncherInventory(
            [new MinecraftLauncherTarget(MinecraftLauncherKind.Win32, @"C:\MinePackUiSmoke\MinecraftLauncher.exe")],
            new LauncherScanResult(LauncherDiscoveryState.Found, [prism]), false, [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static MainWindow.LauncherInventory InventoryWithMultiplePrismTargets()
    {
        var targets = new[]
        {
            new PrismLauncherTarget(@"C:\MinePackUiSmoke\PrismA\prismlauncher.exe",
                @"C:\MinePackUiSmoke\PrismA", @"C:\MinePackUiSmoke\PrismA\instances", new string('A', 64), "fixture"),
            new PrismLauncherTarget(@"D:\Other Prism\prismlauncher.exe",
                @"D:\Other Prism", @"D:\Other Prism\instances", new string('B', 64), "fixture")
        };
        return new MainWindow.LauncherInventory([], new LauncherScanResult(LauncherDiscoveryState.Found, targets), false, [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static MainWindow.LauncherInventory InventoryWithOfficialOnly() =>
        new([new MinecraftLauncherTarget(MinecraftLauncherKind.Win32, @"C:\MinePackUiSmoke\MinecraftLauncher.exe")],
            new LauncherScanResult(LauncherDiscoveryState.Absent, [], "fixture"), false, [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static MainWindow.LauncherInventory EmptyInventory(string diagnostic) =>
        new([], new LauncherScanResult(LauncherDiscoveryState.Absent, [], diagnostic), false, [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private static MouseButtonEventArgs RaiseMouse(UIElement routedSource, object originalSource)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseDownEvent,
            Source = originalSource
        };
        routedSource.RaiseEvent(args);
        return args;
    }

    private static KeyEventArgs RaiseKey(UIElement routedSource, Key key)
    {
        var source = PresentationSource.FromVisual(routedSource)
            ?? throw new InvalidOperationException("The WPF fixture has no presentation source.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = routedSource
        };
        routedSource.RaiseEvent(args);
        return args;
    }

    private static void WaitFor(Task task, string label)
    {
        PumpUntil(() => task.IsCompleted, label);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"Timed out while waiting for {label}.");
            PumpOnce();
            Thread.Sleep(1);
        }
        PumpOnce();
    }

    private static void PumpOnce()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static string NewFixtureRoot(string output)
    {
        var path = Path.Combine(output, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "MinePack.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate MinePack.slnx from the UI smoke output directory.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
