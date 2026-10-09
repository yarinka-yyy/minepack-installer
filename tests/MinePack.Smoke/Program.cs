using System.IO.Compression;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MinePack.Core;
using MinePack.Installer;

try
{
    if (args.Length > 0 && args[0] == "--operation-guard-child")
        Environment.ExitCode = await Smoke.RunOperationGuardChildAsync(args);
    else if (args.Length > 0 && args[0] == "--instance-use-child")
        Environment.ExitCode = await Smoke.RunInstanceUseChildAsync(args);
    else if (args.Length > 0 && args[0] == "--transaction-crash-child")
        Environment.ExitCode = await Smoke.RunTransactionCrashChildAsync(args);
    else if (args.Length > 0 && args[0] == "--network-gate-fixture-child")
        Environment.ExitCode = await Smoke.RunNetworkGateFixtureChildAsync(args);
    else
        await Smoke.RunAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex}");
    Environment.ExitCode = 1;
}

internal static class Smoke
{
    private static readonly Uri TestDownload = new("https://cdn.modrinth.com/data/test/version/test.jar");
    private static readonly string[] YungsJarNames =
    [
        "YungsApi-26.2-Fabric-6.1.3-minepack.1.jar",
        "YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar",
        "YungsBetterDungeons-26.2-Fabric-6.1.1-minepack.1.jar",
        "YungsBetterJungleTemples-26.2-Fabric-4.1.1-minepack.1.jar",
        "YungsBetterMineshafts-26.2-Fabric-6.1.1-minepack.1.jar",
        "YungsBetterNetherFortresses-26.2-Fabric-4.1.1-minepack.1.jar",
        "YungsBetterStrongholds-26.2-Fabric-6.1.1-minepack.1.jar"
    ];
    private static readonly string[] NewForkJarNames =
    [
        "nyfsspiders-fabric-26.2-3.0.0-minepack.1.jar",
        "worldplaytime-1.2.5-minepack.1-26.2-FABRIC.jar"
    ];

    public static async Task RunAsync(string[] args)
    {
        ValidateLivePackArguments(args);
        var requireLivePack = args.Contains("--require-live-pack", StringComparer.Ordinal);
        var livePackRequested = requireLivePack || args.Contains("--live-pack", StringComparer.Ordinal);
        var tempRoot = Path.Combine(Path.GetTempPath(), "minepack-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            VerifyTempCleanupGuard();
            await VerifyOperationGuardAsync(tempRoot);
            await VerifyInstanceUseGuardAsync(tempRoot);
            await VerifyDownloadBoundariesAsync(tempRoot);
            PackRebalanceChecks.Run(True);
            VerifyPinnedRelease();
            VerifyInstalledInstanceCatalogAndPreferences(tempRoot);
            await PrismLauncherChecks.RunAsync(tempRoot);
            VerifyInstallProgressProjection();
            using (InstanceUseGuard.UseProcessInspectionForTesting(
                       Array.Empty<(int ProcessId, string ProcessName)>(), _ => string.Empty))
            {
                await VerifyInitialConfigurationAsync(tempRoot);
                VerifyLocalization();
                VerifyArchiveRejections(tempRoot);
                await VerifyArchiveMutationRejectedAsync(tempRoot);
                VerifyManifestValidation(tempRoot);
                await VerifyUninstallPreflightAsync(tempRoot);
                await VerifyInstallRepairAndUninstallAsync(tempRoot);
                await VerifyPrismInstanceLifecycleAsync(tempRoot);
                await InstanceManagementChecks.RunAsync(tempRoot);
                await VerifyOperationLoggingBestEffortAsync(tempRoot);
                await VerifyOperationLogTargetRebindAsync(tempRoot);
                await VerifyManagedFileTransactionsAsync(tempRoot);
                await VerifyVariantSwitchingAsync(tempRoot);
                await VerifyActivationProfileRollbackAsync(tempRoot);
                VerifyLauncherFixture(tempRoot);
                await VerifyLauncherLifecycleAsync(tempRoot);
                await VerifyAutomaticFabricProfileAsync(tempRoot);
                await VerifyCurrentPerformanceLauncherDefaultsAsync(tempRoot);
                await VerifyVersionedInstallLifecycleAsync();
                await VerifyOfflineFabricProfileAsync(tempRoot);
            }
            await VerifyNetworkGateProcessFixturesAsync();
            var liveCompleted = false;
            if (livePackRequested)
            {
                var liveResult = await RunLivePackChecksAsync(requireLivePack,
                    () => VerifyActualReleaseAsync(tempRoot, requireVanillaSnapshot: requireLivePack),
                    () => VerifyActualVanilla2PlusAsync(tempRoot, requireVanillaSnapshot: requireLivePack));
                liveCompleted = liveResult.Completed;
                Environment.ExitCode = liveResult.ExitCode;
            }
            if (args.Contains("--live-fabric", StringComparer.Ordinal))
                await VerifyOfficialFabricDownloadAsync(tempRoot);
            var cachedIndex = Array.IndexOf(args, "--cached-packs");
            if (cachedIndex >= 0)
            {
                if (cachedIndex + 1 >= args.Length) throw new ArgumentException("--cached-packs requires a game directory.");
                var cachedRoot = args[cachedIndex + 1];
                True(await VerifyActualReleaseAsync(tempRoot, cachedRoot), "Vanilla Plus installs from pinned local files");
                True(await VerifyActualVanilla2PlusAsync(tempRoot, cachedRoot), "Frontier installs from pinned local files");
            }
            if (args.Contains("--live-profile-copy", StringComparer.Ordinal))
                await VerifyCurrentLauncherCopyAsync(tempRoot);
            Console.WriteLine(livePackRequested && !liveCompleted
                ? "Deterministic smoke scenarios passed."
                : "All smoke scenarios passed.");
        }
        finally
        {
            DeleteSmokeTempTree(tempRoot);
        }
    }

    public static async Task<int> RunOperationGuardChildAsync(string[] args)
    {
        if (args.Length >= 3 && args[1] == "try")
        {
            try
            {
                await OperationGuard.RunAsync(() =>
                {
                    File.WriteAllText(args[2], "acquired");
                    return Task.CompletedTask;
                });
                return 0;
            }
            catch (InstallerException ex) when (ex.Code == "OPERATION_BUSY") { return 23; }
        }
        if (args.Length == 4 && args[1] == "hold")
        {
            await OperationGuard.RunAsync(async () =>
            {
                File.WriteAllText(args[2], "held");
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!File.Exists(args[3]) && DateTime.UtcNow < deadline) await Task.Delay(25);
                if (!File.Exists(args[3])) throw new TimeoutException("Smoke parent did not release the held operation gate.");
            });
            return 0;
        }
        return 2;
    }

    public static async Task<int> RunNetworkGateFixtureChildAsync(string[] args)
    {
        if (args.Length == 2 && args[1] == "conflict")
        {
            try { ValidateLivePackArguments(["--live-pack", "--require-live-pack"]); }
            catch (ArgumentException)
            {
                Console.WriteLine("FIXTURE: conflicting live flags rejected.");
                return 2;
            }
            return 0;
        }
        if (args.Length != 4 || args[1] is not ("required" or "optional") ||
            args[2] is not ("completed" or "unavailable" or "404" or "denied" or "skipped") ||
            args[3] is not ("completed" or "unavailable" or "404" or "denied" or "skipped"))
            return 2;

        var required = args[1] == "required";
        var plusStatus = args[2];
        var frontierStatus = args[3];
        var result = await RunLivePackChecksAsync(required,
            () => FakePackResultAsync(plusStatus), () => FakePackResultAsync(frontierStatus));
        Console.WriteLine($"FIXTURE: {args[1]} gate, Vanilla Plus={plusStatus}, Frontier={frontierStatus}. " +
            (result.Completed ? "COMPLETE." : result.ExitCode == 0 ? "PARTIAL." : "FAILED."));
        return result.ExitCode;
    }

    private static Task<bool> FakePackResultAsync(string status) => status switch
    {
        "completed" => Task.FromResult(true),
        "unavailable" => Task.FromException<bool>(new HttpRequestException("fixture source unavailable")),
        "404" => Task.FromException<bool>(new InstallerException("FIXTURE_HTTP_404", "fixture HTTP 404")),
        "denied" => Task.FromException<bool>(new UnauthorizedAccessException("fixture access denied")),
        _ => Task.FromResult(false)
    };

    private static void ValidateLivePackArguments(IReadOnlyCollection<string> args)
    {
        var required = args.Contains("--require-live-pack");
        if (required && args.Contains("--live-pack"))
            throw new ArgumentException("Use either --live-pack or --require-live-pack, not both.");
        if (required && args.Contains("--cached-packs"))
            throw new ArgumentException("--cached-packs cannot satisfy --require-live-pack.");
    }

    private static int LivePackGateExitCode(bool required, bool complete) => required && !complete ? 1 : 0;

    private static async Task<(bool Completed, int ExitCode)> RunLivePackChecksAsync(bool required,
        Func<Task<bool>> verifyVanillaPlus, Func<Task<bool>> verifyFrontier)
    {
        var vanillaPlus = await TryVerifyLivePackAsync("Vanilla Plus", verifyVanillaPlus);
        var frontier = await TryVerifyLivePackAsync("Frontier", verifyFrontier);
        var complete = vanillaPlus && frontier;
        var exitCode = LivePackGateExitCode(required, complete);
        if (!complete)
            Console.WriteLine(required
                ? "REQUIRED LIVE PACK GATE FAILED: both pinned packs must complete direct install, repair, and uninstall."
                : "PARTIAL: deterministic smoke passed; one or both direct live pack checks did not complete.");
        return (complete, exitCode);
    }

    private static async Task VerifyNetworkGateProcessFixturesAsync()
    {
        var unavailable = await RunSmokeChildCaptureAsync("--network-gate-fixture-child", "required", "unavailable", "unavailable");
        Equal(1, unavailable.ExitCode, "required live gate fails when both sources are unavailable");
        True(unavailable.Output.Contains("FIXTURE:", StringComparison.Ordinal), "strict child output is labeled as fixture output");

        var errors = await RunSmokeChildCaptureAsync("--network-gate-fixture-child", "required", "404", "denied");
        Equal(1, errors.ExitCode, "required live gate fails on HTTP and access errors");

        var onePack = await RunSmokeChildCaptureAsync("--network-gate-fixture-child", "required", "completed", "skipped");
        Equal(1, onePack.ExitCode, "required live gate fails when only one pack completed");

        var bothPacks = await RunSmokeChildCaptureAsync("--network-gate-fixture-child", "required", "completed", "completed");
        Equal(0, bothPacks.ExitCode, "required live gate passes only after both packs complete");

        var optional = await RunSmokeChildCaptureAsync("--network-gate-fixture-child", "optional", "unavailable", "skipped");
        Equal(0, optional.ExitCode, "optional live diagnostics may remain partial");
        True(optional.Output.Contains("PARTIAL", StringComparison.Ordinal), "optional child reports PARTIAL");

        var conflict = await RunSmokeChildCaptureAsync("--network-gate-fixture-child", "conflict");
        Equal(2, conflict.ExitCode, "conflicting optional and required live flags are rejected");
        Pass("process-level live gate fixtures distinguish strict failures from optional partial diagnostics");
    }

    private static async Task VerifyDownloadBoundariesAsync(string tempRoot)
    {
        var retryNow = DateTimeOffset.UtcNow;
        Equal(TimeSpan.Zero, DownloadEngine.ClampRetryAfter(TimeSpan.FromSeconds(-1), null, retryNow, TimeSpan.FromSeconds(60)),
            "negative Retry-After is clamped to zero");
        Equal(TimeSpan.FromSeconds(60), DownloadEngine.ClampRetryAfter(null, retryNow.AddDays(365), retryNow, TimeSpan.FromSeconds(60)),
            "oversized Retry-After is clamped to sixty seconds");
        Equal(TimeSpan.Zero, FabricLauncherService.ClampRetryAfter(null, retryNow.AddSeconds(-2), retryNow, TimeSpan.FromSeconds(60)),
            "past Fabric Retry-After dates are clamped to zero");
        var bytes = Bytes("verified download fixture");
        var file = new PackFile("mods/verified.bin", [TestDownload], HashBytes(bytes), bytes.Length);
        var timeoutRequests = 0;
        using (var engine = new DownloadEngine(new AsyncDelegateHandler((_, _) =>
               {
                   Interlocked.Increment(ref timeoutRequests);
                   return Task.FromException<HttpResponseMessage>(new TaskCanceledException("fixture request timeout", new TimeoutException()));
               }),
                   TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(20)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "request-timeout"), null, CancellationToken.None);
                throw new InvalidOperationException("Expected the request timeout to fail after bounded retries.");
            }
            catch (InstallerException ex) when (ex.Code == "DOWNLOAD_TIMEOUT") { }
        }
        Equal(3, timeoutRequests, "request timeout keeps the bounded three-attempt retry policy");

        var preCanceledRequests = 0;
        using (var engine = new DownloadEngine(new DelegateHandler(_ =>
                   {
                       Interlocked.Increment(ref preCanceledRequests);
                       return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                   }), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10)))
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "pre-canceled"), null, cancel.Token);
                throw new InvalidOperationException("Expected pre-canceled download to be rejected.");
            }
            catch (OperationCanceledException) { }
        }
        Equal(0, preCanceledRequests, "caller cancellation before request prevents any network attempt");

        var requestCancelCalls = 0;
        using (var engine = new DownloadEngine(new AsyncDelegateHandler(async (_, token) =>
               {
                   Interlocked.Increment(ref requestCancelCalls);
                   await Task.Delay(Timeout.InfiniteTimeSpan, token);
                   return new HttpResponseMessage(HttpStatusCode.OK);
               }), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10)))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(25)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "request-canceled"), null, cancel.Token);
                throw new InvalidOperationException("Expected request cancellation to propagate.");
            }
            catch (OperationCanceledException) { }
        }
        Equal(1, requestCancelCalls, "caller cancellation during request is not retried");

        var stalledRequests = 0;
        using (var engine = new DownloadEngine(new AsyncDelegateHandler((_, _) =>
               {
                   Interlocked.Increment(ref stalledRequests);
                   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StreamContent(new PendingReadStream())
                   });
               }), TimeSpan.FromMilliseconds(35), TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "body-timeout"), null, CancellationToken.None);
                throw new InvalidOperationException("Expected a stalled response body to time out.");
            }
            catch (InstallerException ex) when (ex.Code == "DOWNLOAD_TIMEOUT") { }
        }
        Equal(3, stalledRequests, "stalled response body retries only up to the source attempt limit");
        True(!Directory.EnumerateFiles(Path.Combine(tempRoot, "body-timeout", "mods"), "*.partial").Any(),
            "body timeout removes partial files after retries");

        var openingRequests = 0;
        using (var engine = new DownloadEngine(new AsyncDelegateHandler((_, _) =>
               {
                   Interlocked.Increment(ref openingRequests);
                   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new PendingOpenContent() });
               }), TimeSpan.FromMilliseconds(35), TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "body-open-timeout"), null, CancellationToken.None);
                throw new InvalidOperationException("Expected opening the response body stream to time out.");
            }
            catch (InstallerException ex) when (ex.Code == "DOWNLOAD_TIMEOUT") { }
        }
        Equal(3, openingRequests, "body stream opening timeout retries only within the source attempt limit");
        True(!Directory.EnumerateFiles(Path.Combine(tempRoot, "body-open-timeout", "mods"), "*.partial").Any(),
            "body stream opening timeout leaves no partial files");

        var lateStreamCompletion = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateStreamDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callerOpeningRequests = 0;
        using (var engine = new DownloadEngine(new AsyncDelegateHandler((_, _) =>
               {
                   Interlocked.Increment(ref callerOpeningRequests);
                   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                   { Content = new LateOpenContent(lateStreamCompletion.Task) });
               }), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(35)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "caller-body-open-cancel"), null, cancel.Token);
                throw new InvalidOperationException("Expected caller cancellation while opening the response body stream.");
            }
            catch (OperationCanceledException) { }
            Equal(1, callerOpeningRequests, "caller cancellation while opening body is not retried");
            lateStreamCompletion.SetResult(new TrackingDisposeStream(() => lateStreamDisposed.TrySetResult()));
            await lateStreamDisposed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            True(lateStreamDisposed.Task.IsCompleted, "late body stream is disposed after caller cancellation");
        }

        var cancelRequests = 0;
        using (var engine = new DownloadEngine(new AsyncDelegateHandler((_, _) =>
               {
                   Interlocked.Increment(ref cancelRequests);
                   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StreamContent(new PendingReadStream())
                   });
               }), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(35)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "caller-cancel"), null, cancel.Token);
                throw new InvalidOperationException("Expected body cancellation to propagate.");
            }
            catch (OperationCanceledException) { }
        }
        Equal(1, cancelRequests, "caller cancellation during body does not retry");
        True(!Directory.EnumerateFiles(Path.Combine(tempRoot, "caller-cancel", "mods"), "*.partial").Any(),
            "caller cancellation removes the partial file");

        var slowBytes = Bytes("slow but progressing");
        var slowFile = new PackFile("mods/slow.bin", [TestDownload], HashBytes(slowBytes), slowBytes.Length);
        using (var engine = new DownloadEngine(new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                   { Content = new StreamContent(new ChunkedMemoryStream(slowBytes, 1, TimeSpan.FromMilliseconds(10))) }),
                   TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10)))
        {
            var slowPath = await engine.DownloadVerifiedAsync(slowFile, Path.Combine(tempRoot, "slow-progress"), null, CancellationToken.None);
            Equal(HashBytes(slowBytes), HashFile(slowPath), "slow body succeeds when bytes arrive within each idle window");
        }

        var retryRequests = 0;
        using (var engine = new DownloadEngine(new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref retryRequests);
                   var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                   response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddDays(365));
                   return response;
               }), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10)))
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "retry-after-limit"), null, CancellationToken.None);
                throw new InvalidOperationException("Expected retry-after fixture to exhaust its retry bound.");
            }
            catch (InstallerException ex) when (ex.Code == "DOWNLOAD_HTTP") { }
            True(watch.Elapsed < TimeSpan.FromSeconds(2), "days-long Retry-After is clamped to the configured short fixture bound");
        }
        Equal(3, retryRequests, "Retry-After remains bounded by three source attempts");

        var delayRequests = 0;
        using (var engine = new DownloadEngine(new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref delayRequests);
                   var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                   response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddDays(365));
                   return response;
               }), TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(30)))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(35)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "retry-after-cancel"), null, cancel.Token);
                throw new InvalidOperationException("Expected cancellation during retry delay.");
            }
            catch (OperationCanceledException) { }
        }
        Equal(1, delayRequests, "caller cancellation interrupts Retry-After delay without another request");

        var mismatchRequests = 0;
        using (var engine = new DownloadEngine(new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref mismatchRequests);
                   return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
               }), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10)))
        {
            var wrongHash = new PackFile("mods/wrong-hash.bin", [TestDownload], new string('0', 128), bytes.Length);
            try
            {
                await engine.DownloadVerifiedAsync(wrongHash, Path.Combine(tempRoot, "hash-mismatch"), null, CancellationToken.None);
                throw new InvalidOperationException("Expected a SHA-512 mismatch.");
            }
            catch (InstallerException ex) when (ex.Code == "DOWNLOAD_HASH_MISMATCH") { }
        }
        Equal(3, mismatchRequests, "hash mismatches retry only within the existing bounded attempt count");

        var sizeRequests = 0;
        using (var engine = new DownloadEngine(new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref sizeRequests);
                   return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes("too large")) };
               }), TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                await engine.DownloadVerifiedAsync(file, Path.Combine(tempRoot, "size-mismatch"), null, CancellationToken.None);
                throw new InvalidOperationException("Expected declared response size mismatch.");
            }
            catch (InstallerException ex) when (ex.Code == "DOWNLOAD_SIZE_MISMATCH") { }
        }
        Equal(3, sizeRequests, "declared size mismatch remains retry bounded and is never accepted");

        var concurrentActive = 0;
        var maximumConcurrent = 0;
        var sharedBytes = Bytes("parallel body");
        using (var engine = new DownloadEngine(new AsyncDelegateHandler(async (_, token) =>
               {
                   var active = Interlocked.Increment(ref concurrentActive);
                   while (true)
                   {
                       var previous = Volatile.Read(ref maximumConcurrent);
                       if (previous >= active || Interlocked.CompareExchange(ref maximumConcurrent, active, previous) == previous) break;
                   }
                   await Task.Delay(25, token);
                   Interlocked.Decrement(ref concurrentActive);
                   return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(sharedBytes) };
               }), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)))
        {
            var parallelRoot = Path.Combine(tempRoot, "parallel-downloads");
            var downloads = Enumerable.Range(0, 9).Select(index => engine.DownloadVerifiedAsync(
                new PackFile($"mods/parallel-{index}.bin", [TestDownload], HashBytes(sharedBytes), sharedBytes.Length),
                parallelRoot, null, CancellationToken.None));
            await Task.WhenAll(downloads);
            True(maximumConcurrent is > 1 and <= 5, "parallel downloads never exceed five active network slots");
        }
        Pass("download timeouts, cancellation, idle progress, retry bounds, hashes, and concurrency are verified");
    }

    private static async Task VerifyOperationGuardAsync(string tempRoot)
    {
        ExecutionContext? staleContext = null;
        var nestedCalls = 0;
        await OperationGuard.RunAsync(async () =>
        {
            staleContext = ExecutionContext.Capture();
            await Task.Run(() => OperationGuard.Run(() => Interlocked.Increment(ref nestedCalls)));
        });
        Equal(1, nestedCalls, "composed same-process calls reenter their active operation lease");

        var originalContext = SynchronizationContext.Current;
        using (var context = new PumpSynchronizationContext())
        {
            var callerThread = Environment.CurrentManagedThreadId;
            var callbackThread = 0;
            var continuationThread = 0;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var operation = OperationGuard.RunAsync(async () =>
                {
                    callbackThread = Environment.CurrentManagedThreadId;
                    await Task.Yield();
                    continuationThread = Environment.CurrentManagedThreadId;
                    return true;
                });
                context.RunUntilComplete(operation);
            }
            finally { SynchronizationContext.SetSynchronizationContext(originalContext); }
            True(callbackThread == callerThread && continuationThread == callerThread,
                "async operation callbacks retain their caller synchronization context");
        }

        var synchronousRun = Task.Run(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            var callerThread = Environment.CurrentManagedThreadId;
            var callbackThread = 0;
            OperationGuard.Run(() => callbackThread = Environment.CurrentManagedThreadId);
            return (callerThread, callbackThread);
        });
        True(await Task.WhenAny(synchronousRun, Task.Delay(TimeSpan.FromSeconds(5))) == synchronousRun,
            "synchronous operation entrypoint does not wait on a caller synchronization context");
        var synchronousThreads = await synchronousRun;
        Equal(synchronousThreads.callerThread, synchronousThreads.callbackThread,
            "synchronous operation callback stays on its calling thread");

        var firstHeld = Path.Combine(tempRoot, "operation-held-one.txt");
        var firstRelease = Path.Combine(tempRoot, "operation-release-one.txt");
        using (var first = StartOperationGuardChild("hold", firstHeld, firstRelease))
        {
            await WaitForFileAsync(firstHeld, first, "child process acquired operation gate");
            var secondSentinel = Path.Combine(tempRoot, "operation-second-sentinel.txt");
            using (var second = StartOperationGuardChild("try", secondSentinel))
            {
                True(second.WaitForExit(10000), "competing child process returns promptly");
                Equal(23, second.ExitCode, "competing child process receives OPERATION_BUSY");
                True(!File.Exists(secondSentinel), "busy child process writes no sentinel");
            }

            var independent = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await OperationGuard.RunAsync(() => Task.CompletedTask);
                    return false;
                }
                catch (InstallerException ex) when (ex.Code == "OPERATION_BUSY") { return true; }
            })));
            True(independent.All(busy => busy), "independent Task.Run mutation entrypoints do not inherit a foreign lease");

            var staleSentinel = Path.Combine(tempRoot, "operation-stale-sentinel.txt");
            var staleWasBusy = false;
            try
            {
                ExecutionContext.Run(staleContext!, _ => OperationGuard.Run(() => File.WriteAllText(staleSentinel, "stale")), null);
            }
            catch (InstallerException ex) when (ex.Code == "OPERATION_BUSY") { staleWasBusy = true; }
            True(staleWasBusy && !File.Exists(staleSentinel), "stale ExecutionContext cannot reuse a disposed lease");

            File.WriteAllText(firstRelease, "release");
            True(first.WaitForExit(10000), "normal owner process releases operation gate");
            Equal(0, first.ExitCode, "normal owner process exits successfully");
        }
        await OperationGuard.RunAsync(() => Task.CompletedTask);

        var crashHeld = Path.Combine(tempRoot, "operation-held-crash.txt");
        var crashRelease = Path.Combine(tempRoot, "operation-release-crash.txt");
        using (var owner = StartOperationGuardChild("hold", crashHeld, crashRelease))
        {
            await WaitForFileAsync(crashHeld, owner, "crash fixture process acquired operation gate");
            owner.Kill(entireProcessTree: true);
            True(owner.WaitForExit(10000), "crashed owner process exits");
        }
        await OperationGuard.RunAsync(() => Task.CompletedTask);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var started = false;
        try
        {
            await OperationGuard.RunAsync(() =>
            {
                started = true;
                return Task.CompletedTask;
            }, canceled.Token);
            throw new InvalidOperationException("Canceled operation unexpectedly ran.");
        }
        catch (OperationCanceledException) { }
        True(!started, "canceled operation is rejected before its callback");
        Pass("SID-isolated operation gate handles composed calls, contention, cancellation, and abandoned ownership");
    }

    public static async Task<int> RunInstanceUseChildAsync(string[] args)
    {
        if (args.Length != 4) return 2;
        File.WriteAllText(args[2], "ready");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(args[3]) && DateTime.UtcNow < deadline) await Task.Delay(25);
        return File.Exists(args[3]) ? 0 : 3;
    }

    public static async Task<int> RunTransactionCrashChildAsync(string[] args)
    {
        if (args.Length != 4) return 2;
        var instance = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
        var packPath = Path.GetFullPath(args[2]);
        var instancesRoot = Path.GetDirectoryName(instance)!;
        var installRoot = Path.GetDirectoryName(instancesRoot)!;
        var smokeRoot = Path.GetDirectoryName(installRoot)!;
        var validatedSmokeRoot = ValidateSmokeTempRoot(smokeRoot);
        var smokePrefix = Path.EndsInDirectorySeparator(validatedSmokeRoot)
            ? validatedSmokeRoot : validatedSmokeRoot + Path.DirectorySeparatorChar;
        if (!Path.GetFileName(instancesRoot).Equals("instances", StringComparison.OrdinalIgnoreCase) ||
            !instance.StartsWith(Path.EndsInDirectorySeparator(instancesRoot) ? instancesRoot : instancesRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !packPath.StartsWith(smokePrefix, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(instance) || !File.Exists(packPath) || !File.Exists(Path.Combine(instance, InstallationManifest.FileName)))
            return 3;
        _ = PackArchive.Open(packPath, args[3]);
        using var processInspection = InstanceUseGuard.UseProcessInspectionForTesting(
            Array.Empty<(int ProcessId, string ProcessName)>(), _ => string.Empty);
        using var checkpoint = ManagedFileTransaction.UseCheckpointsForTesting(name =>
        {
            if (name == "after-replacement-move-0-before-journal") Environment.Exit(86);
        });
        using var installer = new InstallService();
        _ = await installer.RepairAsync(instance, packPath, args[3]);
        return 4;
    }

    private static async Task VerifyInstanceUseGuardAsync(string tempRoot)
    {
        var selfCommandLine = InstanceUseGuard.ReadProcessCommandLine(Environment.ProcessId);
        True(!string.IsNullOrWhiteSpace(selfCommandLine), "native process command line can be read for the smoke process");

        var target = Path.Combine(tempRoot, "native Minecraft instance");
        var childReady = Path.Combine(tempRoot, "native-command-line-child.ready");
        var childRelease = Path.Combine(tempRoot, "native-command-line-child.release");
        using (var child = StartInstanceUseChild(target, childReady, childRelease))
        {
            await WaitForFileAsync(childReady, child, "native command-line child fixture started");
            var childCommandLine = InstanceUseGuard.ReadProcessCommandLine(child.Id);
            True(childCommandLine.Contains("--instance-use-child", StringComparison.Ordinal) &&
                 childCommandLine.Contains(target, StringComparison.OrdinalIgnoreCase),
                "native child command line contains fixture tokens without printing arguments");
            File.WriteAllText(childRelease, "release");
            True(child.WaitForExit(10000), "native command-line child exits after release");
            Equal(0, child.ExitCode, "native command-line child exits successfully");
        }

        var candidate = (ProcessId: 711, ProcessName: "javaw");
        var matchingMinecraft = $"javaw.exe net.fabricmc.loader.impl.launch.knot.KnotClient --gameDir \"{target}\"";
        try
        {
            InstanceUseGuard.EnsureNoGameProcess(target, [candidate], _ => matchingMinecraft);
            throw new InvalidOperationException("Expected the exact target Minecraft gameDir to be rejected.");
        }
        catch (InstallerException ex) when (ex.Code == "GAME_IN_USE") { }

        var otherMinecraft = Path.Combine(tempRoot, "another Java instance");
        InstanceUseGuard.EnsureNoGameProcess(target, [candidate], _ =>
            $"javaw.exe net.minecraft.client.main.Main --gameDir \"{otherMinecraft}\"");
        InstanceUseGuard.EnsureNoGameProcess(target, [candidate], _ =>
            $"javaw.exe --gameDir \"{target}\"");

        foreach (var gameDirectory in new[] { "relative/game-dir", @"\\?\C:\unverified-game-dir" })
        {
            try
            {
                InstanceUseGuard.EnsureNoGameProcess(target, [candidate], _ =>
                    $"javaw.exe net.minecraft.client.main.Main --gameDir \"{gameDirectory}\"");
                throw new InvalidOperationException("Expected an unsupported gameDir form to fail closed.");
            }
            catch (InstallerException ex) when (ex.Code == "GAME_USE_UNVERIFIED") { }
        }

        try
        {
            InstanceUseGuard.EnsureNoGameProcess(target, [candidate], _ => "javaw.exe org.prismlauncher.EntryPoint");
            throw new InvalidOperationException("Expected a wrapper without an inspectable gameDir to fail closed.");
        }
        catch (InstallerException ex) when (ex.Code == "GAME_USE_UNVERIFIED") { }

        var lockedInstance = Path.Combine(tempRoot, "managed-lock-instance");
        Directory.CreateDirectory(Path.Combine(lockedInstance, "mods"));
        var managedPath = Path.Combine(lockedInstance, "mods", "locked.jar");
        File.WriteAllText(managedPath, "managed bytes");
        using (var externalLock = new FileStream(managedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            RefuseManagedFileUse();
            True(externalLock.CanRead, "failed managed-file preflight does not change its open handle");
        }
        using (var externalReader = new FileStream(managedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            RefuseManagedFileUse();
            True(externalReader.CanRead, "read-only use of a managed file blocks mutation preflight");
        }
        using (var externalWriter = new FileStream(managedPath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            RefuseManagedFileUse();
            True(externalWriter.CanWrite, "write use of a managed file blocks mutation preflight");
        }

        var worldLock = Path.Combine(lockedInstance, "saves", "world", "session.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(worldLock)!);
        File.WriteAllText(worldLock, "world lock fixture");
        using (var externalLock = new FileStream(worldLock, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                using var use = InstanceUseGuard.AcquireForTesting(lockedInstance, null,
                    Array.Empty<(int ProcessId, string ProcessName)>(), _ => string.Empty);
                throw new InvalidOperationException("Expected the target world's held session.lock to block preflight.");
            }
            catch (InstallerException ex) when (ex.Code == "INSTANCE_IN_USE") { }
            True(externalLock.CanRead, "failed world-lock preflight does not mutate session.lock");
        }

        Pass("native game-directory inspection, fail-closed wrapper handling, and held-file preflight");

        void RefuseManagedFileUse()
        {
            try
            {
                using var use = InstanceUseGuard.AcquireForTesting(lockedInstance, ["mods/locked.jar"],
                    Array.Empty<(int ProcessId, string ProcessName)>(), _ => string.Empty);
                throw new InvalidOperationException("Expected the held managed file to block preflight.");
            }
            catch (InstallerException ex) when (ex.Code == "INSTANCE_IN_USE") { }
        }
    }

    private static Process StartOperationGuardChild(params string[] arguments)
        => StartSmokeChild("--operation-guard-child", arguments);

    private static Process StartInstanceUseChild(params string[] arguments)
        => StartSmokeChild("--instance-use-child", arguments);

    private static Process StartTransactionCrashChild(params string[] arguments)
        => StartSmokeChild("--transaction-crash-child", arguments);

    private static Process StartSmokeChild(string mode, params string[] arguments)
    {
        return Process.Start(CreateSmokeChildStartInfo(mode, arguments))
            ?? throw new InvalidOperationException("Could not start the smoke child process.");
    }

    private static ProcessStartInfo CreateSmokeChildStartInfo(string mode, IEnumerable<string> arguments)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Smoke executable path is unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Smoke).Assembly.Location);
        start.ArgumentList.Add(mode);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<(int ExitCode, string Output)> RunSmokeChildCaptureAsync(string mode, params string[] arguments)
    {
        var start = CreateSmokeChildStartInfo(mode, arguments);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the smoke fixture child.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static async Task<bool> TryVerifyLivePackAsync(string name, Func<Task<bool>> verify)
    {
        try { return await verify(); }
        catch (InstallerException ex)
        {
            Console.WriteLine($"NOT RUN: {name} live check stopped ({ex.Code}). {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"NOT RUN: {name} live check could not reach its pinned source ({ex.GetType().Name}).");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"NOT RUN: {name} live check could not access a required path ({ex.GetType().Name}).");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"NOT RUN: {name} live check stopped on I/O ({ex.GetType().Name}).");
        }
        catch (OperationCanceledException ex)
        {
            Console.WriteLine($"NOT RUN: {name} live check timed out ({ex.GetType().Name}).");
        }
        return false;
    }

    private static async Task WaitForFileAsync(string path, Process process, string scenario)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(path) && !process.HasExited && DateTime.UtcNow < deadline) await Task.Delay(25);
        True(File.Exists(path), scenario + (process.HasExited ? $" (child exit {process.ExitCode})" : ""));
    }

    private static void VerifyTempCleanupGuard()
    {
        var outsideTemp = Path.Combine(Path.GetTempPath(), "..", "minepack-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            _ = ValidateSmokeTempRoot(outsideTemp);
            throw new InvalidOperationException("Smoke cleanup guard accepted a path outside the system temp directory.");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Refusing recursive cleanup", StringComparison.Ordinal))
        {
            Pass("recursive temp cleanup rejects paths outside its guarded root");
        }
    }

    private static void DeleteSmokeTempTree(string path)
    {
        var resolved = ValidateSmokeTempRoot(path);
        if (!Directory.Exists(resolved)) return;
        if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refusing recursive cleanup of a reparse-point directory.");
        Directory.Delete(resolved, recursive: true);
    }

    private static string ValidateSmokeTempRoot(string path)
    {
        var resolved = Path.GetFullPath(path);
        var tempBase = Path.GetFullPath(Path.GetTempPath());
        var tempPrefix = Path.EndsInDirectorySeparator(tempBase) ? tempBase : tempBase + Path.DirectorySeparatorChar;
        var leaf = Path.GetFileName(resolved);
        const string namePrefix = "minepack-smoke-";
        if (!resolved.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase) ||
            !leaf.StartsWith(namePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(leaf[namePrefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing recursive cleanup outside the generated minepack-smoke temp directory.");
        return resolved;
    }

    private static void VerifyInstalledInstanceCatalogAndPreferences(string tempRoot)
    {
        Equal(33, InstalledInstanceCatalog.KnownReleases.Count, "shared catalog retains all historical releases plus both new current releases");
        True(InstalledInstanceCatalog.UsesCurrentPerformanceDefaults(TestPackRelease.PackVersion,
                 TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, TestPackRelease.ArtifactSha512) &&
             InstalledInstanceCatalog.UsesCurrentPerformanceDefaults("0.18.3",
                 TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, TestPackRelease.FormerOptimizedArtifactSha512) &&
             InstalledInstanceCatalog.UsesCurrentPerformanceDefaults(Vanilla2PlusRelease.PackVersion,
                 Vanilla2PlusRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, Vanilla2PlusRelease.ArtifactSha512) &&
             InstalledInstanceCatalog.UsesCurrentPerformanceDefaults("0.19.9",
                 Vanilla2PlusRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, Vanilla2PlusRelease.FormerOptimizedArtifactSha512) &&
             !InstalledInstanceCatalog.UsesCurrentPerformanceDefaults("0.18.2", TestPackRelease.MinecraftVersion,
                 TestPackRelease.FabricLoaderVersion, TestPackRelease.FormerCurrentArtifactSha512) &&
             !InstalledInstanceCatalog.UsesCurrentPerformanceDefaults(TestPackRelease.PackVersion,
                 TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, new string('A', 128)),
            "performance defaults require an exact current release version and archive hash");
        True(InstalledInstanceCatalog.TryGetRelease("0.1.0", out var legacy) && legacy.MinecraftVersion == "26.3" &&
             InstalledInstanceCatalog.TryGetRelease("0.2.0", out var prior) && prior.MinecraftVersion == "26.3",
            "catalog preserves the two historical Minecraft 26.3 resolver targets");

        var root = Path.Combine(tempRoot, "catalog-root-a");
        var instancesRoot = Path.Combine(root, "instances");
        Directory.CreateDirectory(instancesRoot);
        var plusRelease = InstalledInstanceCatalog.KnownReleases.Single(item => item.PackVersion == TestPackRelease.PackVersion);
        var frontierRelease = InstalledInstanceCatalog.KnownReleases.Single(item => item.PackVersion == Vanilla2PlusRelease.PackVersion);
        var formerOptimizedFrontierRelease = InstalledInstanceCatalog.KnownReleases.Single(item => item.PackVersion == "0.19.9");
        var previousFrontierRelease = InstalledInstanceCatalog.KnownReleases.Single(item => item.PackVersion == "0.19.6");
        Equal("MinePack-26.2-VanillaPlus-0.18.4", InstanceDirectoryNaming.CreateBaseName(plusRelease),
            "new VanillaPlus folder name includes pinned Minecraft and pack versions");
        var formerFrontierInstance = WriteCatalogManifest(instancesRoot, formerOptimizedFrontierRelease, AppContext.BaseDirectory,
            InstanceDirectoryNaming.CreateBaseName(formerOptimizedFrontierRelease));
        var formerOptionsPath = Path.Combine(formerFrontierInstance, "options.txt");
        var formerOptions = Bytes("version:4903\ngraphicsPreset:\"fancy\"\nrenderDistance:16\nentityDistanceScaling:1.0\n");
        File.WriteAllBytes(formerOptionsPath, formerOptions);
        var formerWorldPath = Path.Combine(formerFrontierInstance, "saves", "unchanged-world", "level.dat");
        var formerWorld = Bytes("fixture world data");
        Directory.CreateDirectory(Path.GetDirectoryName(formerWorldPath)!);
        File.WriteAllBytes(formerWorldPath, formerWorld);
        var newFrontierInstanceName = InstanceDirectoryNaming.Allocate(instancesRoot, frontierRelease);
        Equal("MinePack-26.2-Frontier-0.19.10", newFrontierInstanceName,
            "new Frontier folder name is preferred when available");
        True(!Path.GetFullPath(Path.Combine(instancesRoot, newFrontierInstanceName))
                .Equals(Path.GetFullPath(formerFrontierInstance), StringComparison.OrdinalIgnoreCase) &&
             File.ReadAllBytes(formerOptionsPath).SequenceEqual(formerOptions) &&
             File.ReadAllBytes(formerWorldPath).SequenceEqual(formerWorld),
            "installing the new Frontier version allocates alongside the 0.19.9 fixture and preserves its options and world bytes");
        Directory.CreateDirectory(Path.Combine(instancesRoot, InstanceDirectoryNaming.CreateBaseName(frontierRelease)));
        Equal("MinePack-26.2-Frontier-0.19.10-02", InstanceDirectoryNaming.Allocate(instancesRoot, frontierRelease),
            "new instance collision receives the first stable ordinal suffix");
        True(InstanceDirectoryNaming.IsExpected("MinePack-26.2-Frontier-0.19.10", frontierRelease) &&
             InstanceDirectoryNaming.IsExpected("MinePack-26.2-Frontier-0.19.10-02", frontierRelease) &&
             !InstanceDirectoryNaming.IsExpected("MinePack-26.2-Frontier-0.19.10-01", frontierRelease) &&
             !InstanceDirectoryNaming.IsExpected("MinePack-26.2-Frontier-0.19.9", frontierRelease) &&
             !InstanceDirectoryNaming.IsExpected("MinePack-26.2-VanillaPlus-0.19.10", frontierRelease),
            "new folder parsing accepts only the exact pinned pack name/version and valid suffixes");
        var frontierNewName = WriteCatalogManifest(instancesRoot, frontierRelease, AppContext.BaseDirectory,
            InstanceDirectoryNaming.CreateBaseName(frontierRelease));
        var plusInstance = WriteCatalogManifest(instancesRoot, plusRelease, AppContext.BaseDirectory);
        var reinstallName = frontierRelease.InstanceDirectoryPrefix + "-reinstall-" + Guid.NewGuid().ToString("N");
        var frontierInstance = WriteCatalogManifest(instancesRoot, frontierRelease, AppContext.BaseDirectory, reinstallName);
        var previousFrontierInstance = WriteCatalogManifest(instancesRoot, previousFrontierRelease, AppContext.BaseDirectory);
        True(!InstalledInstanceCatalog.IsExpectedInstanceDirectory(
                Path.Combine(instancesRoot, frontierRelease.InstanceDirectoryPrefix + "-REINSTALL-" + Guid.NewGuid().ToString("N")),
                frontierRelease), "uppercase reinstall marker does not broaden historical directory ownership");
        var residue = Path.Combine(instancesRoot, "old-user-data");
        Directory.CreateDirectory(Path.Combine(residue, "saves", "world"));
        File.WriteAllText(Path.Combine(residue, "saves", "world", "level.dat"), "preserve orphan world");
        Directory.CreateDirectory(Path.Combine(root, "outside-instance-list"));
        var unknown = Path.Combine(instancesRoot, "test");
        new InstallationManifest
        {
            PackVersion = "9.9.9",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = new string('A', 128),
            InstalledAt = DateTimeOffset.UtcNow,
            Files = []
        }.SaveAtomic(unknown);

        var entries = InstalledInstanceCatalog.Enumerate(root, AppContext.BaseDirectory);
        True(entries.Single(entry => entry.Path == plusInstance).IsTrusted,
            "current Vanilla Plus manifest and pinned archive grant trusted instance status");
        True(entries.Single(entry => entry.Path == frontierInstance).IsTrusted,
            "known reinstall GUID suffix remains a trusted instance directory");
        True(entries.Single(entry => entry.Path == formerFrontierInstance).IsTrusted,
            "former optimized Frontier 0.19.9 remains a trusted instance beside the new release");
        True(entries.Single(entry => entry.Path == frontierNewName).IsTrusted,
            "new exact short folder name still requires and passes the pinned manifest/archive checks");
        True(entries.Single(entry => entry.Path == previousFrontierInstance).IsTrusted &&
             previousFrontierRelease.ArchiveSha512 == Vanilla2PlusRelease.PreviousCandidateArtifactSha512,
            "immutable Frontier 0.19.6 remains a trusted catalog target after the current release changes");
        Equal(InstalledInstanceState.Residue, entries.Single(entry => entry.Path == residue).State,
            "manifestless worlds are visible as user-data residue only");
        Equal(InstalledInstanceState.UnknownRelease, entries.Single(entry => entry.Path == unknown).State,
            "an unknown version named test is not trusted by its directory name");
        True(!entries.Any(entry => Path.GetFileName(entry.Path) == "outside-instance-list"),
            "catalog enumerates only immediate children of root/instances");

        var emptyAppRoot = Path.Combine(tempRoot, "empty-app-root");
        Directory.CreateDirectory(emptyAppRoot);
        var missingPackage = InstalledInstanceCatalog.Enumerate(root, emptyAppRoot);
        Equal(InstalledInstanceState.PackageMissing,
            missingPackage.Single(entry => entry.Path == plusInstance).State,
            "known manifest with unavailable pinned archive is read-only package-missing state");

        var preferencesPath = Path.Combine(tempRoot, "preferences-fixture", "installer-settings.json");
        var rootA = Path.Combine(tempRoot, "preferences-root-a");
        var rootB = Path.Combine(tempRoot, "preferences-root-b");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        InstallerPreferences.SaveLastValidatedRoot(preferencesPath, rootA);
        Equal(rootA, InstallerPreferences.Load(preferencesPath).LastValidatedRoot,
            "first installer launch reads the last validated root from the explicit fixture file");
        InstallerPreferences.SaveLastValidatedRoot(preferencesPath, rootB);
        Equal(rootB, InstallerPreferences.Load(preferencesPath).LastValidatedRoot,
            "second installer launch persists a changed validated root atomically");
        File.WriteAllText(preferencesPath, "not json");
        var corrupt = InstallerPreferences.Load(preferencesPath);
        True(corrupt.IsCorrupt && corrupt.LastValidatedRoot is null,
            "corrupt preferences return a readable fallback without trusting the stored path");
        File.WriteAllText(preferencesPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            LastValidatedRoot = "C:\\" + '\0' + "bad"
        }));
        True(InstallerPreferences.Load(preferencesPath).IsCorrupt,
            "fully qualified but invalid saved paths are rejected inside the preferences read boundary");
        Pass("trusted instance inventory and fixture-only root preferences");
    }

    private static string WriteCatalogManifest(string instancesRoot, KnownPackRelease release,
        string applicationDirectory, string? directoryName = null)
    {
        var archivePath = release.ArchivePath(applicationDirectory);
        var pack = PackArchive.Open(archivePath, release.ArchiveSha512);
        var manifest = new InstallationManifest
        {
            PackVersion = pack.VersionId,
            MinecraftVersion = pack.MinecraftVersion,
            FabricLoaderVersion = pack.FabricLoaderVersion,
            PackArchiveSha512 = pack.ArchiveSha512,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = pack.Files.Select(file => new ManagedFile(file.Path, file.Sha512,
                    file.Downloads.Select(url => url.AbsoluteUri).ToArray(), false, Math.Max(0, file.Size)))
                .Concat(pack.Overrides.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path))
                    .Select(file => new ManagedFile(file.Path, file.Sha512, [], true, file.Size)))
                .ToList()
        };
        var instance = Path.Combine(instancesRoot, directoryName ?? release.InstanceDirectoryPrefix);
        manifest.SaveAtomic(instance);
        return instance;
    }

    private static void VerifyPinnedRelease()
    {
        var path = Path.Combine(AppContext.BaseDirectory, TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var pack = PackArchive.Open(path, TestPackRelease.ArtifactSha512);
        var previousCurrentPath = Path.Combine(AppContext.BaseDirectory, "releases", "test-pack",
            TestPackRelease.PreviousCurrentArtifactFileName);
        var previousCurrent = PackArchive.Open(previousCurrentPath, TestPackRelease.PreviousCurrentArtifactSha512);
        Equal(TestPackRelease.PackVersion, pack.VersionId, "pinned release version");
        Equal(TestPackRelease.MinecraftVersion, pack.MinecraftVersion, "pinned Minecraft version");
        Equal(TestPackRelease.FabricLoaderVersion, pack.FabricLoaderVersion, "pinned Fabric Loader version");
        var addedMods = new[]
        {
            "InventoryParticles-3.2.0+26.2+fabric.jar", "dense-flowers-0.3.1+mc26.2.jar",
            "inventorysorter-fabric-3.0.1+mc26.2.jar", "ImmediatelyFast-Fabric-1.16.5+26.2.jar",
            "coolrain-1.4.0-26.2.jar", "sound-physics-remastered-fabric-1.5.1+26.2.jar",
            "held-item-info-1.9.2.jar", "bbe-fabric-1.3.7+mc26.2.jar",
            "Clumps-fabric-26.2-26.2.1.jar", "entityculling-fabric-1.11.2-mc26.2.jar",
            "MossyLib-1.6.0+26.2+fabric.jar", "cloth-config-26.2.155.jar",
            "ferritecore-9.0.0-fabric.jar"
        };
        var newMods = new[]
        {
            "xaeroworldmap-fabric-26.2-1.46.1.jar", "AdvancementPlaques-26.2-fabric-1.7.2.jar",
            "cherishedworlds-fabric-17.0.0+26.2.jar", "leafmealone-1.2.0.jar",
            "InvMove-0.9.6+26.2-Fabric.jar", "Iceberg-26.2-fabric-1.4.2.2.jar",
            "modmenu-20.0.2.jar", "placeholder-api-3.1.0-beta.1+26.2.jar"
        };
        var sharedNewMods = new[]
        {
            "SubtleEffects-fabric-26.2-1.14.3.jar",
            "fzzy_config-0.7.6+26.2.jar", "fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar"
        };
        var currentPerformanceMods = new[]
        {
            "BadOptimizations-2.4.1-26.2-fabric.jar",
            "moreculling-fabric-26.2-1.8.1.jar",
            "lithium-fabric-0.25.3+mc26.2.jar"
        };
        True(pack.Files.Count == 68 && sharedNewMods.All(name => previousCurrent.Files.Any(file => file.Path == "mods/" + name)) &&
             currentPerformanceMods.All(name => pack.Files.Any(file => file.Path == "mods/" + name)) &&
             pack.Files.Any(file => file.Path == "mods/betterstats-5.5.6+fn-26.2.jar") &&
             pack.Files.Any(file => file.Path == "mods/tcdcommons-5.5.6+fn-26.2.jar") &&
             !pack.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)) &&
             !pack.Files.Any(file => file.Path.Contains("firstperson", StringComparison.OrdinalIgnoreCase) ||
                                          file.Path.Contains("notenoughanimations", StringComparison.OrdinalIgnoreCase)) &&
             pack.Files.Any(file => file.Path == "shaderpacks/ComplementaryReimagined_r5.9.3.zip") &&
             pack.Files.Any(file => file.Path == "mods/voxy-0.2.19-beta.jar") &&
             pack.Files.Any(file => file.Path == "mods/Chunky-Fabric-1.5.3.jar") &&
             pack.Files.Any(file => file.Path == "mods/c2me-fabric-mc26.2-0.4.2-alpha.0.52.jar") &&
             pack.Files.Any(file => file.Path == "mods/PickUpNotifier-v26.2.0-mc26.2.x-Fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/explosive-enhancement-1.4.2-26.2.jar") &&
             pack.Files.Any(file => file.Path == "mods/entity_model_features-3.3.8-26.2-fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/entity_texture_features-7.2.4-26.2-fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/punchy-2.8a-fabric-26.2.jar") &&
             pack.Files.Any(file => file.Path == "mods/PuzzlesLib-v26.2.4-mc26.2.x-Fabric.jar") &&
             pack.Files.Any(file => file.Path == "mods/ForgeConfigAPIPort-v26.2.1-mc26.2.x-Fabric.jar") &&
             addedMods.Concat(newMods).All(name => pack.Files.Any(file => file.Path == "mods/" + name)) &&
             TestPackRelease.InitialResourcePacks.All(name => pack.Files.Any(file => file.Path == "resourcepacks/" + name) ||
                 pack.Overrides.Any(file => file.Path == "resourcepacks/" + name)) &&
             pack.Overrides.Any(file => file.Path == "config/iris.properties"),
            "pinned release includes the base pack, requested mods and resource packs, and required dependencies");
        True(pack.Files.All(file => file.Sha512.Length == 128 && file.Sha512.All(Uri.IsHexDigit) &&
                                    file.Downloads.All(uri => uri.Scheme == Uri.UriSchemeHttps && uri.Host == "cdn.modrinth.com")),
             "pinned release hashes and URLs are valid");
        var priorVanillaPlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.PriorArtifactFileName);
        var priorVanillaPlus = PackArchive.Open(priorVanillaPlusPath, TestPackRelease.PriorArtifactSha512);
        True(previousCurrent.Files.Count == 45 && sharedNewMods.All(name =>
                previousCurrent.Files.Any(file => file.Path == "mods/" + name)),
            "immutable Vanilla Plus 0.18.1 retains its previous release inventory");
        True(priorVanillaPlus.VersionId == "0.10.0" && priorVanillaPlus.Files.Count == 43 &&
             priorVanillaPlus.Files.Where(file => file.Path != "resourcepacks/LowOnFire v26.2§8.zip").All(oldFile => previousCurrent.Files.Any(file => file.Path == oldFile.Path &&
                 file.Sha512 == oldFile.Sha512 && file.Downloads.SequenceEqual(oldFile.Downloads))) &&
             previousCurrent.Files.Where(file => !priorVanillaPlus.Files.Any(oldFile => oldFile.Path == file.Path))
                 .Select(file => file.Path).ToHashSet(StringComparer.Ordinal)
                 .SetEquals(sharedNewMods.Select(name => "mods/" + name)),
            "Vanilla Plus keeps the other 0.15.0 additions while preserving 0.10.0");
        var smoothVanillaPlus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.SmoothArtifactFileName),
            TestPackRelease.SmoothArtifactSha512);
        True(smoothVanillaPlus.VersionId == "0.15.0" && smoothVanillaPlus.Files.Count == 47 &&
             smoothVanillaPlus.Files.Where(file => !file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase) &&
                 file.Path != "resourcepacks/LowOnFire v26.2§8.zip")
                 .All(oldFile => previousCurrent.Files.Any(file => file.Path == oldFile.Path && file.Sha512 == oldFile.Sha512 &&
                     file.Downloads.SequenceEqual(oldFile.Downloads))),
            "Vanilla Plus excludes Smooth Swapping and Low On Fire");
        var lowFireVanillaPlus = PackArchive.Open(Path.Combine(AppContext.BaseDirectory, "releases", "test-pack",
            TestPackRelease.LowFireArtifactFileName), TestPackRelease.LowFireArtifactSha512);
        True(lowFireVanillaPlus.VersionId == "0.18.0" && lowFireVanillaPlus.Files.Count == previousCurrent.Files.Count + 1 &&
             lowFireVanillaPlus.Files.Where(file => file.Path != "resourcepacks/LowOnFire v26.2§8.zip")
                 .All(oldFile => previousCurrent.Files.Any(file => file.Path == oldFile.Path && file.Sha512 == oldFile.Sha512)) &&
             !previousCurrent.Files.Any(file => file.Path == "resourcepacks/LowOnFire v26.2§8.zip"),
            "Vanilla Plus removes only Low On Fire and preserves the previous immutable archive");
        var originalVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.OriginalArtifactFileName);
        var originalVanilla2Plus = PackArchive.Open(originalVanilla2PlusPath, Vanilla2PlusRelease.OriginalArtifactSha512);
        True(originalVanilla2Plus.VersionId == "0.11.0" && originalVanilla2Plus.Files.Count == 48 &&
             priorVanillaPlus.Files.All(baseFile => originalVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "original Vanilla 2 Plus archive remains pinned and preserves Vanilla Plus 0.10.0");
        var legacyVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.LegacyArtifactFileName);
        var legacyVanilla2Plus = PackArchive.Open(legacyVanilla2PlusPath, Vanilla2PlusRelease.LegacyArtifactSha512);
        True(legacyVanilla2Plus.VersionId == "0.12.0" && legacyVanilla2Plus.Files.Count == 54 &&
             originalVanilla2Plus.Files.All(baseFile => legacyVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "legacy Vanilla 2 Plus archive remains pinned and preserves the original release");
        var previousVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.PreviousArtifactFileName);
        var previousVanilla2Plus = PackArchive.Open(previousVanilla2PlusPath, Vanilla2PlusRelease.PreviousArtifactSha512);
        True(previousVanilla2Plus.VersionId == "0.13.0" && previousVanilla2Plus.Files.Count == 59 &&
             legacyVanilla2Plus.Files.All(baseFile => previousVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "previous Vanilla 2 Plus archive remains pinned and preserves the legacy release");
        var priorVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.PriorArtifactFileName);
        var priorVanilla2Plus = PackArchive.Open(priorVanilla2PlusPath, Vanilla2PlusRelease.PriorArtifactSha512);
        True(priorVanilla2Plus.VersionId == "0.14.0" && priorVanilla2Plus.Files.Count == 61 &&
             previousVanilla2Plus.Files.All(baseFile => priorVanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))),
            "Vanilla 2 Plus 0.14.0 remains pinned with its guard animation packs");
        var guardVanilla2PlusPath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.GuardArtifactFileName);
        var guardVanilla2Plus = PackArchive.Open(guardVanilla2PlusPath, Vanilla2PlusRelease.GuardArtifactSha512);
        True(guardVanilla2Plus.VersionId == "0.16.0" && guardVanilla2Plus.Files.Count == 61,
            "Vanilla 2 Plus 0.16.0 remains pinned for Repair");
        var worldgenVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.WorldgenArtifactFileName),
            Vanilla2PlusRelease.WorldgenArtifactSha512);
        True(worldgenVanilla2Plus.VersionId == "0.17.0" && worldgenVanilla2Plus.Files.Count == 64,
            "Frontier 0.17.0 remains pinned for Repair");
        var untunedVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.UntunedArtifactFileName),
            Vanilla2PlusRelease.UntunedArtifactSha512);
        True(untunedVanilla2Plus.VersionId == "0.19.0", "Frontier 0.19.0 remains pinned for Repair");
        var tunedVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.TunedArtifactFileName),
            Vanilla2PlusRelease.TunedArtifactSha512);
        True(tunedVanilla2Plus.VersionId == "0.19.1", "Frontier 0.19.1 remains pinned for Repair");
        var yungsVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.YungsArtifactFileName),
            Vanilla2PlusRelease.YungsArtifactSha512);
        True(yungsVanilla2Plus.VersionId == "0.19.2", "Frontier 0.19.2 remains pinned for Repair");
        var spidersVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.SpidersArtifactFileName),
            Vanilla2PlusRelease.SpidersArtifactSha512);
        True(spidersVanilla2Plus.VersionId == "0.19.3", "Frontier 0.19.3 remains pinned for Repair");
        var xalisVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.XalisArtifactFileName),
            Vanilla2PlusRelease.XalisArtifactSha512);
        True(xalisVanilla2Plus.VersionId == "0.19.4", "Frontier 0.19.4 remains pinned for Repair");
        var vanilla2PlusPath = Path.Combine(AppContext.BaseDirectory,
            Vanilla2PlusRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var vanilla2Plus = PackArchive.Open(vanilla2PlusPath, Vanilla2PlusRelease.ArtifactSha512);
        var previousCurrentVanilla2Plus = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", Vanilla2PlusRelease.PreviousCurrentArtifactFileName),
            Vanilla2PlusRelease.PreviousCurrentArtifactSha512);
        var previousCandidatePath = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus",
            Vanilla2PlusRelease.PreviousCandidateArtifactFileName);
        var previousCandidate = PackArchive.Open(previousCandidatePath, Vanilla2PlusRelease.PreviousCandidateArtifactSha512);
        True(previousCandidate.VersionId == "0.19.6" && previousCandidate.Files.Count == previousCurrentVanilla2Plus.Files.Count &&
             previousCandidate.Overrides.Any(file => file.Path == "resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip") &&
             previousCandidate.Overrides.Any(file => file.Path == "mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.1.jar") &&
             !previousCandidate.Overrides.Any(file => file.Path.EndsWith("minepack.2.zip", StringComparison.Ordinal) ||
                 file.Path.EndsWith("minepack.2.jar", StringComparison.Ordinal)),
            "immutable Frontier 0.19.6 keeps its original resource and Desert Temples artifacts");
        var doorsVanilla2Plus = PackArchive.Open(Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus",
            Vanilla2PlusRelease.DoorsArtifactFileName), Vanilla2PlusRelease.DoorsArtifactSha512);
        True(doorsVanilla2Plus.VersionId == "0.19.5" && doorsVanilla2Plus.Files.Count == previousCurrentVanilla2Plus.Files.Count + 1 &&
             doorsVanilla2Plus.Files.Where(file => file.Path != "resourcepacks/LowOnFire v26.2§8.zip")
                 .All(oldFile => vanilla2Plus.Files.Any(file => file.Path == oldFile.Path && file.Sha512 == oldFile.Sha512)) &&
             !vanilla2Plus.Files.Any(file => file.Path == "resourcepacks/LowOnFire v26.2§8.zip"),
            "Frontier removes only Low On Fire and preserves the previous immutable archive");
        Equal(Vanilla2PlusRelease.PackVersion, vanilla2Plus.VersionId, "Vanilla 2 Plus release version");
        Equal("MinePack Vanilla 2 Plus", vanilla2Plus.Name, "Vanilla 2 Plus archive name");
        Equal(TestPackRelease.MinecraftVersion, vanilla2Plus.MinecraftVersion, "Vanilla 2 Plus Minecraft version");
        Equal(TestPackRelease.FabricLoaderVersion, vanilla2Plus.FabricLoaderVersion, "Vanilla 2 Plus Fabric Loader version");
        True(vanilla2Plus.Files.Count == 73 &&
             previousCurrentVanilla2Plus.Files.All(oldFile => vanilla2Plus.Files.Any(file => file.Path == oldFile.Path &&
                 file.Sha512 == oldFile.Sha512 && file.Downloads.SequenceEqual(oldFile.Downloads))) &&
             worldgenVanilla2Plus.Files.Where(file => !file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase) &&
                 file.Path != "resourcepacks/LowOnFire v26.2§8.zip")
                 .All(baseFile => vanilla2Plus.Files.Any(file => file.Path == baseFile.Path &&
                 file.Sha512 == baseFile.Sha512 && file.Downloads.SequenceEqual(baseFile.Downloads))) &&
             !vanilla2Plus.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)) &&
             vanilla2Plus.Overrides.Select(file => file.Path).ToHashSet(StringComparer.Ordinal)
                 .SetEquals(new[] { "config/iris.properties", "config/guardvillagers.json", "config/voxyworldgenv2.json" }
                     .Concat(YungsJarNames.Concat(NewForkJarNames).Select(name => "mods/" + name))
                     .Append("resourcepacks/xalis-enhanced-vanilla-26.2-minepack.2.zip")
                     .Append("resourcepacks/Remodeled-Doors-26.2-xalis-blockstates.2.zip")) &&
             vanilla2Plus.Files.Any(file => file.Path == "mods/continuity-3.0.1+26.2.jar" &&
                 file.Sha512.Equals("3436b39fcdddce87f8eda0f35095067477636df2667195df3cb8eae2d002d3ff8ac44de97332668ee50e13ad91be5c532cbc6121878f1e6c904c98c1c9c67c0b", StringComparison.OrdinalIgnoreCase)) &&
             vanilla2Plus.Files.Any(file => file.Path == "mods/citresewn-continuation-1.2.2-fork.13+26.2.jar" &&
                 file.Sha512.Equals("520f37c6c2c8dce4ad2648f8e7d8193c9f4796c83898ed0f9371232376362c54cddbe4c476ef3245162e9535c7d1e7799740f4417d051a6baf287daa0f685842", StringComparison.OrdinalIgnoreCase)) &&
             vanilla2Plus.Files.Any(file => file.Path == "mods/treeharvester-26.2.0-9.4.jar" &&
                 file.Sha512.Equals("a740bd962e9ab71d67b7683f6e51aae948781d0150c94eec62f873fc94de7f6fe4ad40a48cc8502b6ebf52609b0340898ad7a3a46c29ce6c14ba0020d2b3cf10", StringComparison.OrdinalIgnoreCase)) &&
             vanilla2Plus.Files.Any(file => file.Path == "mods/collective-26.2.0-8.40.jar" &&
                 file.Sha512.Equals("1c35fa28b2ec42cc130c578ad9bb1f320477d881816044a4ba28429ec633c8bd6725fd1378a8ae41b88337ab43aff174d55db0468ed23c5cd16cca776d001c93", StringComparison.OrdinalIgnoreCase)) &&
             vanilla2Plus.Files.Any(file => file.Path == "resourcepacks/§aRemodeled-Doors§8_§62.2.1.zip" &&
                 file.Sha512.Equals("efe81850001ef80bed6dc880df1084f5e607c6ed295c88e6fbd2834e273101e275b1275c53cf2b2c40a1e6a98ead268d57873acdfc782ae4773e2151df34c6ce", StringComparison.OrdinalIgnoreCase)),
            "Frontier preserves prior files and pins Tree Harvester, Collective, Remodeled Doors, and xali");
        using (var archive = ZipFile.OpenRead(vanilla2PlusPath))
        using (var overlay = archive.GetEntry("overrides/resourcepacks/Remodeled-Doors-26.2-xalis-blockstates.2.zip")!.Open())
        using (var memory = new MemoryStream())
        {
            overlay.CopyTo(memory);
            Equal(HashFile(Path.Combine(Environment.CurrentDirectory, "pack", "vanilla-2-plus", "resourcepacks", "Remodeled-Doors-26.2-xalis-blockstates.2.zip")),
                HashBytes(memory.ToArray()), "Frontier embeds the new pinned door compatibility ZIP");
        }
        using (var archive = ZipFile.OpenRead(vanilla2PlusPath))
        using (var config = JsonDocument.Parse(archive.GetEntry("overrides/config/guardvillagers.json")!.Open()))
            True(config.RootElement.GetProperty("followHero").GetBoolean() == false &&
                 config.RootElement.GetProperty("reputationRequirement").GetInt32() == int.MinValue &&
                 config.RootElement.GetProperty("giveGuardStuffHotv").GetBoolean() == false &&
                 config.RootElement.GetProperty("setGuardPatrolHotv").GetBoolean() == false,
                "pinned guard config allows inventory, follow, and patrol without Hero of the Village");
        using (var archive = ZipFile.OpenRead(vanilla2PlusPath))
        using (var config = JsonDocument.Parse(archive.GetEntry("overrides/config/voxyworldgenv2.json")!.Open()))
            True(config.RootElement.GetProperty("generationRadius").GetInt32() == 128 &&
                 config.RootElement.GetProperty("maxActiveTasks").GetInt32() == 3,
                "pinned Voxy WorldGen config keeps radius 128 and limits active tasks to three");

        var catalog = PackCatalog.VanillaPlusGroups.SelectMany(group => group.Items).ToArray();
        var vanilla2PlusCatalog = PackCatalog.Items;
        var localCatalogPaths = YungsJarNames.Concat(NewForkJarNames).Select(name => "mods/" + name)
            .Concat(["resourcepacks/xalis-enhanced-vanilla-26.2-minepack.2.zip", "resourcepacks/Remodeled-Doors-26.2-xalis-blockstates.2.zip"]);
        var testPackPath = Path.Combine(AppContext.BaseDirectory, TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var testPackForCatalog = PackArchive.Open(testPackPath, TestPackRelease.ArtifactSha512);
        True(catalog.Length == 79 && catalog.Count(item => item.FilePath.StartsWith("mods/", StringComparison.Ordinal)) == 65 &&
             catalog.Count(item => item.Kind == "resourcepack") == 13 && catalog.Count(item => item.Kind == "shader") == 1 &&
             catalog.Select(item => item.FilePath).ToHashSet(StringComparer.Ordinal)
                 .SetEquals(testPackForCatalog.Files.Select(file => file.Path).Concat(localCatalogPaths)) &&
             vanilla2PlusCatalog.Count == 84 && vanilla2PlusCatalog.Count(item => item.FilePath.StartsWith("mods/", StringComparison.Ordinal)) == 70 &&
             vanilla2PlusCatalog.Count(item => item.Kind == "resourcepack") == 13 &&
             vanilla2PlusCatalog.Count(item => item.Kind == "datapack") == 1 &&
             vanilla2PlusCatalog.Select(item => item.FilePath).ToHashSet(StringComparer.Ordinal)
                 .SetEquals(vanilla2Plus.Files.Select(file => file.Path).Concat(localCatalogPaths)) &&
             vanilla2PlusCatalog.All(item => item.ModrinthUrl is null
                 ? YungsJarNames.Concat(NewForkJarNames).Contains(Path.GetFileName(item.FilePath)) || item.FilePath is "resourcepacks/xalis-enhanced-vanilla-26.2-minepack.2.zip" or "resourcepacks/Remodeled-Doors-26.2-xalis-blockstates.2.zip"
                 : item.ModrinthUrl.Scheme == Uri.UriSchemeHttps && item.ModrinthUrl.Host == "modrinth.com" && !string.IsNullOrWhiteSpace(item.ProjectId)),
            "Vanilla Plus and Frontier catalogs exactly match their pinned releases");
        var initialOptions = TestPackRelease.InitialOptions.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        AssertOptimizedGraphicsOptions(TestPackRelease.InitialOptions, "new options generator");
        True(initialOptions[0] == "version:4903" && new[]
        {
            "key_key.sprint:key.keyboard.left.shift", "key_key.sneak:key.keyboard.left.control",
            "fov:0.25", "fullscreen:true", "exclusiveFullscreen:true", "guiScale:4"
        }.All(initialOptions.Contains) && !initialOptions.Any(line => line.StartsWith("fullscreenResolution:", StringComparison.Ordinal)),
            "new profile defaults include requested controls, FOV, fullscreen and GUI scale without a fixed monitor mode");
        True(Vanilla2PlusRelease.InitialResourcePacks.Length == 13 &&
             Vanilla2PlusRelease.InitialResourcePacks.SequenceEqual(TestPackRelease.InitialResourcePacks) &&
             Vanilla2PlusRelease.InitialOptions.Contains("file/Semos Animations Lib 2.0.4.zip", StringComparison.Ordinal) &&
             Vanilla2PlusRelease.InitialOptions.Contains("file/Freshly Modded 3.0.5.zip", StringComparison.Ordinal) &&
             Vanilla2PlusRelease.InitialOptions.Contains("file/xalis-enhanced-vanilla-26.2-minepack.2.zip", StringComparison.Ordinal) &&
             Vanilla2PlusRelease.InitialResourcePacks[^2] == "§aRemodeled-Doors§8_§62.2.1.zip" &&
             Vanilla2PlusRelease.InitialResourcePacks[^1] == "Remodeled-Doors-26.2-xalis-blockstates.2.zip" &&
             !Vanilla2PlusRelease.InitialOptions.Contains("LowOnFire", StringComparison.Ordinal) &&
             TestPackRelease.InitialOptions.Contains("Freshly Modded", StringComparison.Ordinal),
            "the shared resource-pack order is enabled for both new pack installations");
        Pass("pinned .mrpack opens and matches its SHA-512");
    }

    private static void AssertOptimizedGraphicsOptions(string options, string context)
    {
        var lines = options.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        foreach (var (key, value) in new[]
                 {
                     ("graphicsPreset", "\"custom\""),
                     ("renderDistance", "9"),
                     ("entityDistanceScaling", "2.0")
                 })
        {
            var matches = lines.Where(line => line.StartsWith(key + ":", StringComparison.Ordinal)).ToArray();
            Equal(1, matches.Length, $"{context} writes {key} exactly once");
            Equal(key + ":" + value, matches[0], $"{context} sets {key} to its optimized value");
        }
        using var preset = JsonDocument.Parse(lines.Single(line => line.StartsWith("graphicsPreset:", StringComparison.Ordinal))["graphicsPreset:".Length..]);
        Equal("custom", preset.RootElement.GetString(), $"{context} encodes graphicsPreset as the JSON string custom");
        Equal(0, lines.Count(line => line.StartsWith("simulationDistance:", StringComparison.Ordinal)),
            $"{context} leaves simulation distance at the Minecraft default");
    }

    private static void AssertNoOptimizedGraphicsOptions(string options, string context)
    {
        var lines = options.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        foreach (var key in new[] { "graphicsPreset", "renderDistance", "entityDistanceScaling", "simulationDistance" })
            Equal(0, lines.Count(line => line.StartsWith(key + ":", StringComparison.Ordinal)),
                $"{context} does not add {key}");
    }

    private static void VerifyInstallProgressProjection()
    {
        var projector = new InstallProgressProjector();
        var sizes = new long[] { 3, 8, 19, 41, 100 };
        var completed = 0;
        foreach (var size in sizes)
        {
            var first = projector.Update("files", new InstallProgress("download", "file progress", completed, sizes.Length, 1, size));
            Equal(completed, first.CompletedFiles, "byte progress does not advance file count");
            var interleaved = projector.Update("files", new InstallProgress("override", "other file progress", completed, sizes.Length, size / 2, size));
            Equal(completed, interleaved.CompletedFiles, "interleaved byte events keep monotonic file count");
            var retry = projector.Update("files", new InstallProgress("download", "retry progress", completed, sizes.Length, size, size));
            Equal(completed, retry.CompletedFiles, "retry progress does not count a file twice");
            completed++;
            var verified = projector.Update("files", new InstallProgress("complete", "file verified", completed, sizes.Length));
            Equal(completed, verified.CompletedFiles, "verified file increments the shared count once");
            Equal(sizes.Length, verified.TotalFiles, "byte size never changes the file total");
        }
        Equal(sizes.Length, completed, "five differently sized files reach a stable total");

        var unknown = new InstallProgressProjector().Update("files", new InstallProgress("download", "unknown", 0, 0, 12));
        True(unknown.IsIndeterminate, "unknown total uses indeterminate progress");
        var profile = projector.Update("profile", new InstallProgress("profile", "profile setup", 0, 0));
        True(profile.PhaseChanged && profile.IsIndeterminate, "profile setup is an explicit separate phase");
        var nextFiles = projector.Update("files", new InstallProgress("prepare", "next file set", 0, 2));
        True(nextFiles.PhaseChanged, "a new file phase resets the projection");
        Equal(0, nextFiles.CompletedFiles, "phase reset does not inherit the prior completed count");
        Pass("shared file progress stays monotonic across byte events, retries, unknown totals, and phase changes");
    }

    private static async Task VerifyInitialConfigurationAsync(string tempRoot)
    {
        const string irisPath = "config/iris.properties";
        var releaseRoot = Path.Combine(AppContext.BaseDirectory, "releases");
        var irisArchives = 0;
        foreach (var path in Directory.EnumerateFiles(releaseRoot, "*.mrpack", SearchOption.AllDirectories))
        {
            var pack = PackArchive.Open(path, HashFile(path));
            if (!pack.Overrides.Any(file => file.Path.Equals(irisPath, StringComparison.OrdinalIgnoreCase))) continue;
            True(InitialConfiguration.IsInitialUserConfig(pack, irisPath), "every pinned archive with Iris has hash-bound initial config ownership");
            irisArchives++;
        }
        Equal(32, irisArchives, "all pinned Iris archive hashes are classified");

        var unknownArchivePath = Path.Combine(tempRoot, "unknown-iris.mrpack");
        CreatePack(unknownArchivePath, "0.1.0", [], [new TestOverride(irisPath, Bytes("unknown archive"))]);
        var unknownArchive = PackArchive.Open(unknownArchivePath, HashFile(unknownArchivePath));
        True(!InitialConfiguration.IsInitialUserConfig(unknownArchive, irisPath), "an unknown archive does not gain Iris ownership from its path");

        var previousResourcePackCases = new[]
        {
            ("0.19.9", Vanilla2PlusRelease.FormerOptimizedArtifactFileName, Vanilla2PlusRelease.FormerOptimizedArtifactSha512,
                Vanilla2PlusRelease.InitialResourcePacks),
            ("0.19.8", Vanilla2PlusRelease.FormerCurrentArtifactFileName, Vanilla2PlusRelease.FormerCurrentArtifactSha512,
                Vanilla2PlusRelease.InitialResourcePacks),
            ("0.19.7", Vanilla2PlusRelease.PreviousCurrentArtifactFileName, Vanilla2PlusRelease.PreviousCurrentArtifactSha512,
                Vanilla2PlusRelease.LegacyCurrentResourcePacks),
            ("0.19.4", Vanilla2PlusRelease.XalisArtifactFileName, Vanilla2PlusRelease.XalisArtifactSha512,
                Vanilla2PlusRelease.PreviousResourcePacks[..^2]),
            ("0.19.5", Vanilla2PlusRelease.DoorsArtifactFileName, Vanilla2PlusRelease.DoorsArtifactSha512,
                Vanilla2PlusRelease.PreviousResourcePacks),
            ("0.19.6", Vanilla2PlusRelease.PreviousCandidateArtifactFileName, Vanilla2PlusRelease.PreviousCandidateArtifactSha512,
                Vanilla2PlusRelease.PreviousCandidateResourcePacks)
        };
        foreach (var (version, fileName, archiveHash, expectedPacks) in previousResourcePackCases)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "releases", "vanilla-2-plus", fileName);
            var pack = PackArchive.Open(path, archiveHash);
            var stage = Path.Combine(tempRoot, "initial-config-frontier-" + version + "-stage");
            Directory.CreateDirectory(stage);
            await pack.ExtractOverridesAsync(stage, CancellationToken.None);
            var defaults = InitialConfiguration.Create(pack, stage);
            var optimizedDefaults = version == "0.19.9";
            Equal(TestPackRelease.BuildInitialOptions(expectedPacks, optimizedDefaults),
                System.Text.Encoding.UTF8.GetString(defaults.Single(file => file.Path == "options.txt").Contents),
                $"Frontier {version} retains its exact pinned initial resource-pack order");
            if (optimizedDefaults)
                AssertOptimizedGraphicsOptions(System.Text.Encoding.UTF8.GetString(defaults.Single(file => file.Path == "options.txt").Contents),
                    $"former optimized Frontier {version} Repair defaults");
            else
                AssertNoOptimizedGraphicsOptions(System.Text.Encoding.UTF8.GetString(defaults.Single(file => file.Path == "options.txt").Contents),
                    $"legacy Frontier {version} Repair defaults");
            True(InitialConfiguration.IsInitialUserConfig(pack, irisPath) &&
                 InitialConfiguration.IsInitialUserConfig(pack, "config/guardvillagers.json") &&
                 InitialConfiguration.IsInitialUserConfig(pack, "config/voxyworldgenv2.json"),
                $"Frontier {version} retains its hash-bound initial config classification");
            if (version == "0.19.7")
            {
                var defaultPaths = defaults.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                True(defaultPaths.Contains("config/guardvillagers.json") && defaultPaths.Contains("config/voxyworldgenv2.json"),
                    "legacy Frontier 0.19.7 still recognizes Guard and Voxy configs as initial user data");
                foreach (var configPath in new[] { "config/guardvillagers.json", "config/voxyworldgenv2.json" })
                    Equal(HashFile(FixturePath(stage, configPath)), HashBytes(defaults.Single(file => file.Path == configPath).Contents),
                        $"legacy Frontier 0.19.7 keeps its initial {Path.GetFileName(configPath)} bytes");
            }
        }

        var formerOptimizedTestPack = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.FormerOptimizedArtifactFileName),
            TestPackRelease.FormerOptimizedArtifactSha512);
        var formerOptimizedTestStage = Path.Combine(tempRoot, "initial-config-vanilla-plus-0.18.3-stage");
        Directory.CreateDirectory(formerOptimizedTestStage);
        await formerOptimizedTestPack.ExtractOverridesAsync(formerOptimizedTestStage, CancellationToken.None);
        var formerOptimizedTestDefaults = InitialConfiguration.Create(formerOptimizedTestPack, formerOptimizedTestStage);
        var formerOptimizedTestOptions = System.Text.Encoding.UTF8.GetString(formerOptimizedTestDefaults.Single(file => file.Path == "options.txt").Contents);
        Equal(TestPackRelease.BuildInitialOptions(TestPackRelease.InitialResourcePacks, optimizedDefaults: true), formerOptimizedTestOptions,
            "former optimized Vanilla Plus 0.18.3 retains its curated order and corrected graphics defaults");
        AssertOptimizedGraphicsOptions(formerOptimizedTestOptions, "former optimized Vanilla Plus 0.18.3 Repair defaults");

        var previousTestPack = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.PreviousCurrentArtifactFileName),
            TestPackRelease.PreviousCurrentArtifactSha512);
        var previousTestStage = Path.Combine(tempRoot, "initial-config-vanilla-plus-0.18.1-stage");
        Directory.CreateDirectory(previousTestStage);
        await previousTestPack.ExtractOverridesAsync(previousTestStage, CancellationToken.None);
        var previousTestDefaults = InitialConfiguration.Create(previousTestPack, previousTestStage);
        Equal(TestPackRelease.BuildInitialOptions(TestPackRelease.LegacyCurrentResourcePacks),
            System.Text.Encoding.UTF8.GetString(previousTestDefaults.Single(file => file.Path == "options.txt").Contents),
            "legacy Vanilla Plus 0.18.1 keeps its 7-item default instead of inheriting the new list");
        AssertNoOptimizedGraphicsOptions(System.Text.Encoding.UTF8.GetString(previousTestDefaults.Single(file => file.Path == "options.txt").Contents),
            "legacy Vanilla Plus 0.18.1 Repair defaults");

        var formerTestPack = PackArchive.Open(
            Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.FormerCurrentArtifactFileName),
            TestPackRelease.FormerCurrentArtifactSha512);
        var formerTestStage = Path.Combine(tempRoot, "initial-config-vanilla-plus-0.18.2-stage");
        Directory.CreateDirectory(formerTestStage);
        await formerTestPack.ExtractOverridesAsync(formerTestStage, CancellationToken.None);
        var formerTestDefaults = InitialConfiguration.Create(formerTestPack, formerTestStage);
        Equal(TestPackRelease.BuildInitialOptions(TestPackRelease.InitialResourcePacks),
            System.Text.Encoding.UTF8.GetString(formerTestDefaults.Single(file => file.Path == "options.txt").Contents),
            "former-current Vanilla Plus 0.18.2 retains its 13-item order without new performance defaults");
        AssertNoOptimizedGraphicsOptions(System.Text.Encoding.UTF8.GetString(formerTestDefaults.Single(file => file.Path == "options.txt").Contents),
            "legacy Vanilla Plus 0.18.2 Repair defaults");

        var testPackPath = Path.Combine(AppContext.BaseDirectory, TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var frontierPackPath = Path.Combine(AppContext.BaseDirectory, Vanilla2PlusRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var testPack = PackArchive.Open(testPackPath, TestPackRelease.ArtifactSha512);
        var frontierPack = PackArchive.Open(frontierPackPath, Vanilla2PlusRelease.ArtifactSha512);
        var testStage = Path.Combine(tempRoot, "initial-config-test-stage");
        var frontierStage = Path.Combine(tempRoot, "initial-config-frontier-stage");
        Directory.CreateDirectory(testStage);
        Directory.CreateDirectory(frontierStage);
        await testPack.ExtractOverridesAsync(testStage, CancellationToken.None);
        await frontierPack.ExtractOverridesAsync(frontierStage, CancellationToken.None);
        var testDefaults = InitialConfiguration.Create(testPack, testStage);
        var frontierDefaults = InitialConfiguration.Create(frontierPack, frontierStage);

        var testPaths = testDefaults.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        True(testPaths.SetEquals(["options.txt", "config/BBEConfig.json", irisPath, "config/guardvillagers.json", "config/voxyworldgenv2.json"]),
            "Vanilla Plus creates its pinned Guard and Voxy defaults as well as options, BBE, and Iris");
        Equal(TestPackRelease.InitialOptions, System.Text.Encoding.UTF8.GetString(testDefaults.Single(file => file.Path == "options.txt").Contents),
            "Vanilla Plus preserves curated resource pack order and options");
        foreach (var (pack, defaults) in new[] { (testPack, testDefaults), (frontierPack, frontierDefaults) })
        {
            var options = System.Text.Encoding.UTF8.GetString(defaults.Single(file => file.Path == "options.txt").Contents);
            Equal(1, options.Split("enableVsync:false", StringSplitOptions.None).Length - 1,
                $"{pack.Name} initial options disable VSync exactly once");
            True(!options.Contains("enableVsync:true", StringComparison.Ordinal),
                $"{pack.Name} initial options do not enable VSync");
            True(options.StartsWith("version:4903" + Environment.NewLine, StringComparison.Ordinal),
                $"{pack.Name} initial options keep the required version line first");
            AssertOptimizedGraphicsOptions(options, $"{pack.Name} production-generated options");
        }
        using var bbe = JsonDocument.Parse(testDefaults.Single(file => file.Path == "config/BBEConfig.json").Contents);
        True(bbe.RootElement.GetProperty("bbe.config.storage.main").EnumerateArray().All(item => !item.GetProperty("value").GetBoolean()),
            "Better Block Entities defaults disable the existing chest and shulker optimizations");
        Equal(HashFile(FixturePath(testStage, irisPath)), HashBytes(testDefaults.Single(file => file.Path == irisPath).Contents),
            "Vanilla Plus initial Iris config comes byte-for-byte from its pinned archive");
        foreach (var file in testPack.Overrides.Where(item => InitialConfiguration.IsInitialUserConfig(testPack, item.Path)))
            Equal(HashFile(FixturePath(testStage, file.Path)), HashBytes(testDefaults.Single(item => item.Path == file.Path).Contents),
                $"Vanilla Plus initial override is copied from its pinned archive: {file.Path}");

        var frontierPaths = frontierDefaults.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        True(frontierPaths.SetEquals(["options.txt", "config/BBEConfig.json", irisPath, "config/guardvillagers.json", "config/voxyworldgenv2.json"]),
            "Frontier creates Iris, Guard, Voxy, options, and BBE defaults for its pinned release");
        Equal(Vanilla2PlusRelease.InitialOptions,
            System.Text.Encoding.UTF8.GetString(frontierDefaults.Single(file => file.Path == "options.txt").Contents),
            "Frontier preserves resource pack order, its Low On Fire exclusion, and optimized defaults");
        foreach (var file in frontierPack.Overrides.Where(item => InitialConfiguration.IsInitialUserConfig(frontierPack, item.Path)))
            Equal(HashFile(FixturePath(frontierStage, file.Path)), HashBytes(frontierDefaults.Single(item => item.Path == file.Path).Contents),
                $"Frontier initial override is copied from its pinned archive: {file.Path}");

        var missingRoot = Path.Combine(tempRoot, "initial-config-missing-target");
        var missingStage = Path.Combine(tempRoot, "initial-config-missing-stage");
        Directory.CreateDirectory(missingRoot);
        InitialConfiguration.WriteToRoot(missingStage, frontierDefaults);
        var restored = InitialConfiguration.RestoreMissing(missingRoot, missingStage, frontierDefaults);
        True(restored.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(frontierPaths), "Repair restores every absent Frontier initial default");
        foreach (var file in frontierDefaults)
            Equal(HashBytes(file.Contents), HashFile(FixturePath(missingRoot, file.Path)), $"restored default matches pinned content: {file.Path}");

        var existingRoot = Path.Combine(tempRoot, "initial-config-existing-target");
        var existingStage = Path.Combine(tempRoot, "initial-config-existing-stage");
        var existing = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["options.txt"] = [],
            ["config/BBEConfig.json"] = Bytes("{"),
            [irisPath] = Bytes("edited Iris settings"),
            ["config/guardvillagers.json"] = Bytes("edited Guard settings"),
            ["config/voxyworldgenv2.json"] = Bytes("edited Voxy settings")
        };
        foreach (var (path, contents) in existing)
        {
            var target = FixturePath(existingRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, contents);
        }
        InitialConfiguration.WriteToRoot(existingStage, frontierDefaults);
        Equal(0, InitialConfiguration.RestoreMissing(existingRoot, existingStage, frontierDefaults).Count,
            "Repair leaves every existing initial file alone, including empty and malformed content");
        foreach (var (path, contents) in existing)
            True(File.ReadAllBytes(FixturePath(existingRoot, path)).SequenceEqual(contents), $"existing player config bytes stay unchanged: {path}");

        foreach (var options in new[] { "version:4903\nenableVsync:true\n", "version:4903\n" })
        {
            var root = Path.Combine(tempRoot, "initial-config-existing-options-" + HashBytes(Bytes(options))[..8]);
            InitialConfiguration.WriteToRoot(root, frontierDefaults);
            var path = FixturePath(root, "options.txt");
            var original = Bytes(options);
            File.WriteAllBytes(path, original);
            Equal(0, InitialConfiguration.RestoreMissing(root, existingStage, frontierDefaults).Count,
                "Repair does not migrate or recreate an existing options.txt");
            True(File.ReadAllBytes(path).SequenceEqual(original), "Repair preserves existing options.txt bytes exactly");
        }

        using var validator = new InstallService();
        foreach (var (pack, archivePath) in new[] { (testPack, testPackPath), (frontierPack, frontierPackPath) })
        foreach (var legacy in new[] { false, true })
        {
            var root = CreateReleaseFixture(tempRoot, pack, legacy, out var instance);
            var accepted = validator.ValidateUninstallTarget(root, instance, archivePath, pack.ArchiveSha512);
            Equal(legacy, accepted.Files.Any(file => file.Path.Equals(irisPath, StringComparison.OrdinalIgnoreCase)),
                $"strict {(legacy ? "legacy" : "new")} manifest is accepted for {pack.Name}");
        }

        var legacyRoot = CreateReleaseFixture(tempRoot, frontierPack, legacy: true, out var legacyInstance);
        var legacyIris = FixturePath(legacyInstance, irisPath);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyIris)!);
        File.WriteAllText(legacyIris, "edited legacy Iris settings");
        var markerPath = Path.Combine(legacyRoot, ".minepack-active.json");
        var markerHash = HashFile(markerPath);
        var manifestPath = Path.Combine(legacyInstance, InstallationManifest.FileName);
        var originalManifest = File.ReadAllText(manifestPath);
        var corrupt = JsonNode.Parse(originalManifest)!.AsObject();
        corrupt[nameof(InstallationManifest.Files)]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(file => file[nameof(ManagedFile.Path)]!.GetValue<string>() == irisPath)[nameof(ManagedFile.Sha512)] = new string('0', 128);
        File.WriteAllText(manifestPath, corrupt.ToJsonString());
        RejectReleaseManifest(validator, legacyRoot, legacyInstance, frontierPackPath, frontierPack.ArchiveSha512,
            "corrupt legacy Iris metadata is rejected before mutation");
        Equal(markerHash, HashFile(markerPath), "corrupt legacy Iris metadata rejection preserves the active marker");
        Equal(HashBytes(Bytes("edited legacy Iris settings")), HashFile(legacyIris), "corrupt legacy Iris metadata rejection preserves player Iris bytes");
        File.WriteAllText(manifestPath, originalManifest);
        var wrongSize = JsonNode.Parse(originalManifest)!.AsObject();
        var legacyIrisRecord = wrongSize[nameof(InstallationManifest.Files)]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(file => file[nameof(ManagedFile.Path)]!.GetValue<string>() == irisPath);
        legacyIrisRecord[nameof(ManagedFile.Size)] = legacyIrisRecord[nameof(ManagedFile.Size)]!.GetValue<long>() + 1;
        File.WriteAllText(manifestPath, wrongSize.ToJsonString());
        RejectReleaseManifest(validator, legacyRoot, legacyInstance, frontierPackPath, frontierPack.ArchiveSha512,
            "corrupt legacy Iris size is rejected before mutation");
        Equal(markerHash, HashFile(markerPath), "corrupt legacy Iris size rejection preserves the active marker");
        Equal(HashBytes(Bytes("edited legacy Iris settings")), HashFile(legacyIris), "corrupt legacy Iris size rejection preserves player Iris bytes");
        File.WriteAllText(manifestPath, originalManifest);

        var replacementRoot = CreateReleaseFixture(tempRoot, testPack, legacy: true, out var replacementInstance);
        var replacementIris = FixturePath(replacementInstance, irisPath);
        Directory.CreateDirectory(Path.GetDirectoryName(replacementIris)!);
        File.WriteAllText(replacementIris, "edited replacement-test Iris");
        var replacementManifestPath = Path.Combine(replacementInstance, InstallationManifest.FileName);
        var replacementJson = JsonNode.Parse(File.ReadAllText(replacementManifestPath))!.AsObject();
        var replacementFiles = replacementJson[nameof(InstallationManifest.Files)]!.AsArray();
        replacementFiles.Remove(replacementFiles.Select(node => node!.AsObject())
            .First(file => file[nameof(ManagedFile.Path)]!.GetValue<string>()
                .EndsWith(".jar", StringComparison.OrdinalIgnoreCase)));
        File.WriteAllText(replacementManifestPath, replacementJson.ToJsonString());
        RejectReleaseManifest(validator, replacementRoot, replacementInstance, testPackPath, testPack.ArchiveSha512,
            "legacy Iris cannot replace a missing managed JAR");
        Equal(HashBytes(Bytes("edited replacement-test Iris")), HashFile(replacementIris), "rejected missing-JAR manifest preserves player Iris bytes");

        using (var failingRepair = new InstallService(new DownloadEngine(new DelegateHandler(_ =>
                   new HttpResponseMessage(HttpStatusCode.NotFound)))))
        {
            var repair = await failingRepair.RepairAsync(legacyInstance, frontierPackPath, frontierPack.ArchiveSha512);
            Equal("DOWNLOAD_HTTP", repair.Code, "legacy manifest passes strict release validation before fake download failure");
        }
        Equal(HashBytes(Bytes("edited legacy Iris settings")), HashFile(legacyIris), "Repair does not replace edited Iris from a legacy manifest");
        var legacyUninstall = await validator.UninstallAsync(legacyRoot, legacyInstance, frontierPackPath, frontierPack.ArchiveSha512);
        True(legacyUninstall.Success && File.ReadAllText(legacyIris) == "edited legacy Iris settings",
            "Uninstall preserves edited Iris from a legacy manifest");

        var currentRoot = CreateReleaseFixture(tempRoot, frontierPack, legacy: false, out var currentInstance);
        var currentConfigs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [irisPath] = "edited current Iris settings",
            ["config/guardvillagers.json"] = "edited current Guard settings",
            ["config/voxyworldgenv2.json"] = "edited current Voxy settings"
        };
        foreach (var (path, contents) in currentConfigs)
        {
            var target = FixturePath(currentInstance, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, contents);
        }
        var currentUninstall = await validator.UninstallAsync(currentRoot, currentInstance, frontierPackPath, frontierPack.ArchiveSha512);
        True(currentUninstall.Success && currentConfigs.All(pair => File.ReadAllText(FixturePath(currentInstance, pair.Key)) == pair.Value) &&
             !File.Exists(FixturePath(currentInstance, "options.txt")) && !File.Exists(FixturePath(currentInstance, "config/BBEConfig.json")),
            "Uninstall preserves existing player configs and does not create missing options or BBE defaults");
        Pass("initial config ownership, exact legacy metadata, and missing-only defaults");
    }

    private static void VerifyLocalization()
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var (name, expected) in new[]
                     {
                         ("ru-RU", "ru"), ("ru", "ru"), ("en-US", "en"), ("en-GB", "en"),
                         ("zh-CN", "zh-CN"), ("zh-SG", "zh-CN"), ("zh-TW", "zh-CN"), ("zh-Hans", "zh-CN"),
                         ("de-DE", "en"), ("ja-JP", "en")
                     })
                Equal(expected, LocalizedText.SelectUiCulture(CultureInfo.GetCultureInfo(name)).Name, $"UI culture for {name}");

            var resourceDirectory = Path.Combine(Environment.CurrentDirectory, "src", "MinePack.Core", "Resources");
            var english = ReadResourceFile(Path.Combine(resourceDirectory, "Strings.resx"));
            var russian = ReadResourceFile(Path.Combine(resourceDirectory, "Strings.ru.resx"));
            var chinese = ReadResourceFile(Path.Combine(resourceDirectory, "Strings.zh-CN.resx"));
            var invariantLauncherLabels = new HashSet<string>(StringComparer.Ordinal)
            {
                "UiLauncherOptionOfficial", "UiLauncherOptionPrism",
                "OfficialInstanceTargetDisplay", "PrismInstanceTargetDisplay"
            };
            True(english.Keys.Order().SequenceEqual(russian.Keys.Order()), "English and Russian resource keys match");
            True(english.Keys.Order().SequenceEqual(chinese.Keys.Order()), "English and Simplified Chinese resource keys match");
            foreach (var key in english.Keys)
            {
                True(Placeholders(english[key]).SequenceEqual(Placeholders(russian[key])), $"resource placeholders match for {key}");
                True(Placeholders(english[key]).SequenceEqual(Placeholders(chinese[key])), $"Chinese resource placeholders match for {key}");
                if (invariantLauncherLabels.Contains(key))
                    Equal(english[key], chinese[key], $"launcher label spelling is invariant for {key}");
                else if (!chinese[key].Any(char.IsLetter))
                    Equal(english[key], chinese[key], $"format-only resource is culture-neutral for {key}");
                else
                    True(chinese[key] != english[key] && chinese[key].Any(character => character is >= '\u3400' and <= '\u9FFF'),
                        $"Chinese resource is translated and does not fall back to English for {key}");
            }

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Equal("Ready to install", LocalizedText.Get("UiReady"), "English startup text");
            Equal("Preparing a separate game folder", LocalizedText.Get("PreparingInstance"), "English progress text");
            Equal("Pack files installed.", LocalizedText.Get("PackFilesInstalled"), "English success text");
            Equal("The pack file was not found.", LocalizedText.Get("PackFileMissing"), "English error text");
            Equal("Performance & Render Distance", LocalizedText.Get("CatalogPerformance"), "English catalog text");
            Equal("Building Blocks — 5", PackCatalog.Vanilla2PlusGroups[^2].Heading, "English Vanilla 2 Plus building category");
            Equal("World & Structures — 12", PackCatalog.Vanilla2PlusGroups[^1].Heading, "English Vanilla 2 Plus worldgen category");
            Equal("Technical Foundation — 15", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogTechnical").Heading, "English Frontier dependencies");
            Equal("Resource Packs — 13", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogResourcePacks").Heading, "English Frontier resource packs");
            Equal("Pack version 0.19.10", LocalizedText.Get("UiPackVersion", Vanilla2PlusRelease.PackVersion), "English selected pack version");
            Equal("Installer version 0.18.0", LocalizedText.Get("UiInstallerVersion", "0.18.0"), "English installer version");
            Equal("70 mods · 13 resource packs · 1 shader", LocalizedText.Get("UiPackCountsVanilla2Plus"), "English selected pack counts");
            Equal("65 mods · 13 resource packs · 1 shader", LocalizedText.Get("UiPackCountsVanillaPlus"), "English Vanilla Plus counts");
            Equal("Performance & Render Distance — 12|Graphics & Animations — 12|Tools & Quality of Life — 12|Sound — 2|Technical Foundation — 15|Resource Packs — 13|Shader — 1|World & Structures — 12",
                string.Join('|', PackCatalog.Groups.Select(group => group.Heading)), "English catalog headings and counts");
            Equal("Copied worlds: 2. Skipped existing names: 1; missing session.lock: 2; locked or unverified session.lock: 3.",
                LocalizedText.Get("WorldImportSummary", 2, 1, 2, 3), "English formatted world import summary");

            CultureInfo.CurrentUICulture = LocalizedText.SelectUiCulture(CultureInfo.GetCultureInfo("zh-SG"));
            Equal("zh-CN", CultureInfo.CurrentUICulture.Name, "Chinese UI culture canonicalized to Simplified Chinese");
            Equal("准备就绪，可以安装", LocalizedText.Get("UiReady"), "Chinese startup text");
            Equal("正在准备独立游戏文件夹", LocalizedText.Get("PreparingInstance"), "Chinese progress text");
            Equal("整合包文件已安装。", LocalizedText.Get("PackFilesInstalled"), "Chinese success text");
            Equal("未找到整合包文件。", LocalizedText.Get("PackFileMissing"), "Chinese error text");
            Equal("性能与区块渲染距离", LocalizedText.Get("CatalogPerformance"), "Chinese catalog text");
            Equal("整合包版本 0.19.10", LocalizedText.Get("UiPackVersion", Vanilla2PlusRelease.PackVersion), "Chinese selected pack version");
            Equal("安装程序版本 0.18.0", LocalizedText.Get("UiInstallerVersion", "0.18.0"), "Chinese installer version");
            Equal("70 个模组 · 13 个资源包 · 1 个光影包", LocalizedText.Get("UiPackCountsVanilla2Plus"), "Chinese selected pack counts");
            Equal("65 个模组 · 13 个资源包 · 1 个光影包", LocalizedText.Get("UiPackCountsVanillaPlus"), "Chinese Vanilla Plus counts");
            Equal("已复制存档：2。因名称已存在而跳过：1；缺少 session.lock：2；session.lock 已锁定或无法验证：3。",
                LocalizedText.Get("WorldImportSummary", 2, 1, 2, 3), "Chinese formatted world import summary");
            Equal("建筑方块 — 5", PackCatalog.Vanilla2PlusGroups[^2].Heading, "Chinese Vanilla 2 Plus building category");
            Equal("世界与结构 — 12", PackCatalog.Vanilla2PlusGroups[^1].Heading, "Chinese Vanilla 2 Plus worldgen category");
            Equal("资源包 — 13", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogResourcePacks").Heading,
                "Chinese Vanilla 2 Plus resource pack category");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            Equal("Готово к установке", LocalizedText.Get("UiReady"), "Russian startup text");
            Equal("Подготовка отдельного каталога", LocalizedText.Get("PreparingInstance"), "Russian progress text");
            Equal("Файлы сборки установлены.", LocalizedText.Get("PackFilesInstalled"), "Russian success text");
            Equal("Файл сборки не найден.", LocalizedText.Get("PackFileMissing"), "Russian error text");
            Equal("Производительность и дальность", LocalizedText.Get("CatalogPerformance"), "Russian catalog text");
            Equal("Строительные блоки — 5", PackCatalog.Vanilla2PlusGroups[^2].Heading, "Russian Vanilla 2 Plus building category");
            Equal("Мир и структуры — 12", PackCatalog.Vanilla2PlusGroups[^1].Heading, "Russian Vanilla 2 Plus worldgen category");
            Equal("Техническая основа — 15", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogTechnical").Heading, "Russian Frontier dependencies");
            Equal("Ресурспаки — 13", PackCatalog.Vanilla2PlusGroups.Single(group => group.Key == "CatalogResourcePacks").Heading, "Russian Frontier resource packs");
            Equal("Версия сборки 0.19.10", LocalizedText.Get("UiPackVersion", Vanilla2PlusRelease.PackVersion), "Russian selected pack version");
            Equal("Версия установщика 0.18.0", LocalizedText.Get("UiInstallerVersion", "0.18.0"), "Russian installer version");
            Equal("70 модов · 13 ресурспаков · 1 шейдер", LocalizedText.Get("UiPackCountsVanilla2Plus"), "Russian selected pack counts");
            Equal("65 модов · 13 ресурспаков · 1 шейдер", LocalizedText.Get("UiPackCountsVanillaPlus"), "Russian Vanilla Plus counts");
            Equal("Производительность и дальность — 12|Графика и анимации — 12|Инструменты и удобство — 12|Звук — 2|Техническая основа — 15|Ресурспаки — 13|Шейдер — 1|Мир и структуры — 12",
                string.Join('|', PackCatalog.Groups.Select(group => group.Heading)), "Russian catalog headings and counts");
            Equal("Скопировано миров: 2. Пропущено совпадений имён: 1; нет session.lock: 2; session.lock занят или не проверен: 3.",
                LocalizedText.Get("WorldImportSummary", 2, 1, 2, 3), "Russian formatted world import summary");

            try
            {
                _ = LocalizedText.Get("MissingLocalizationKey");
                throw new InvalidOperationException("A missing localization key was not rejected.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Missing localization resource", StringComparison.Ordinal)) { }
            Equal(originalCulture.Name, CultureInfo.CurrentCulture.Name, "localization leaves formatting culture unchanged");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
        Pass("UI culture selection, resource keys, placeholders, and localized messages");
    }

    private static Dictionary<string, string> ReadResourceFile(string path) => XDocument.Load(path).Root!
        .Elements("data")
        .ToDictionary(item => (string)item.Attribute("name")!, item => item.Element("value")!.Value, StringComparer.Ordinal);

    private static int[] Placeholders(string value) => Regex.Matches(value, @"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")
        .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
        .Order()
        .ToArray();

    private static void VerifyArchiveRejections(string tempRoot)
    {
        Rejects(tempRoot, "../escape.jar", "a", "PATH_BLOCKED", "parent path traversal");
        Rejects(tempRoot, "/escape.jar", "a", "PATH_BLOCKED", "absolute path");
        Rejects(tempRoot, "C:/escape.jar", "a", "PATH_BLOCKED", "drive path");
        Rejects(tempRoot, "mods/bad.jar", "a", "PACK_INVALID_HASH", "invalid SHA-512");
        Rejects(tempRoot, "mods/bad.jar", "a", "DOWNLOAD_URL_BLOCKED", "non-allowlisted URL");
        Rejects(tempRoot, "mods/Managed.jar", "a", "PACK_DUPLICATE_PATH", "case-insensitive duplicate path", duplicate: true);
        Rejects(tempRoot, "mods/test.jar", "a", "PACK_DUPLICATE_PATH", "download and override path conflict", addOverride: true);
        Rejects(tempRoot, "saves/world/level.dat", "a", "PACK_RESERVED_PATH", "world data override");
        Console.WriteLine("PASS: unsafe archive paths, hashes, URLs, duplicates, and protected data are rejected");
    }

    private static void Rejects(string tempRoot, string filePath, string content, string expectedCode, string scenario,
        bool duplicate = false, bool addOverride = false)
    {
        var path = Path.Combine(tempRoot, "reject-" + Guid.NewGuid().ToString("N") + ".mrpack");
        TestFile[] files;
        if (duplicate) files = [new TestFile(filePath, Bytes(content)), new TestFile(filePath.ToLowerInvariant(), Bytes(content))];
        else if (expectedCode == "PACK_RESERVED_PATH") files = [];
        else files = [new TestFile(filePath, Bytes(content), Url: expectedCode == "DOWNLOAD_URL_BLOCKED" ? "http://example.invalid/mod.jar" : TestDownload.AbsoluteUri,
            InvalidHash: expectedCode == "PACK_INVALID_HASH")];
        var overrides = addOverride ? new[] { new TestOverride("mods/test.jar", Bytes("override")) } :
            expectedCode == "PACK_RESERVED_PATH" ? new[] { new TestOverride(filePath, Bytes("user data")) } : [];
        CreatePack(path, "0.1.0", files, overrides);
        try
        {
            _ = PackArchive.Open(path);
            throw new InvalidOperationException($"Expected {expectedCode} for {scenario}.");
        }
        catch (InstallerException ex) when (ex.Code == expectedCode)
        {
            // Expected rejection.
        }
        finally { File.Delete(path); }
    }

    private static void VerifyManifestValidation(string tempRoot)
    {
        var instance = Path.Combine(tempRoot, "manifest-validation");
        Directory.CreateDirectory(instance);
        var manifestPath = Path.Combine(instance, InstallationManifest.FileName);
        var manifest = new InstallationManifest
        {
            PackVersion = "0.1.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = new string('A', 128),
            InstalledAt = DateTimeOffset.UtcNow,
            Files = [new ManagedFile("mods/test.jar", new string('B', 128), [TestDownload.AbsoluteUri], false, 1)]
        };
        var json = JsonSerializer.Serialize(manifest);

        string Change(Action<JsonObject> change)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            change(root);
            return root.ToJsonString();
        }

        void Reject(string candidate, string expectedCode, string label)
        {
            File.WriteAllText(manifestPath, candidate);
            try
            {
                _ = InstallationManifest.Load(instance);
                throw new InvalidOperationException($"Expected {label} to be rejected.");
            }
            catch (InstallerException ex) when (ex.Code == expectedCode) { }
            Pass(label);
        }

        JsonObject ManifestFile(JsonObject root) => root[nameof(InstallationManifest.Files)]!.AsArray()[0]!.AsObject();

        Reject("[]", "MANIFEST_INVALID", "manifest root must be an object");
        Reject(Change(root => root.Remove(nameof(InstallationManifest.SchemaVersion))), "MANIFEST_INVALID", "missing schema version is rejected");
        foreach (var malformed in new[] { "null", "\"1\"", "{}", "2147483648" })
            Reject(Change(root => root[nameof(InstallationManifest.SchemaVersion)] = JsonNode.Parse(malformed)),
                "MANIFEST_INVALID", "invalid schema version JSON kind is controlled");
        Reject(Change(root => root[nameof(InstallationManifest.SchemaVersion)] = 2),
            "MANIFEST_SCHEMA_UNSUPPORTED", "unsupported manifest schema keeps its distinct code");
        Reject(Change(root => root.Remove(nameof(InstallationManifest.Files))), "MANIFEST_INVALID", "missing files field is rejected");
        Reject(Change(root => root[nameof(InstallationManifest.Files)] = null), "MANIFEST_INVALID", "null files field is rejected");
        Reject(Change(root => root[nameof(InstallationManifest.Files)]!.AsArray()[0] = null),
            "MANIFEST_INVALID", "null managed-file entries are rejected");
        Reject(Change(root => ManifestFile(root).Remove(nameof(ManagedFile.Size))), "MANIFEST_INVALID", "missing managed-file size is rejected");
        foreach (var malformed in new[] { "null", "\"1\"", "{}", "9223372036854775808" })
            Reject(Change(root => ManifestFile(root)[nameof(ManagedFile.Size)] = JsonNode.Parse(malformed)),
                "MANIFEST_INVALID", "invalid managed-file size JSON kind is controlled");
        Reject(Change(root => ManifestFile(root).Remove(nameof(ManagedFile.IsOverride))),
            "MANIFEST_INVALID", "missing override type is rejected");
        Reject(Change(root => ManifestFile(root).Remove(nameof(ManagedFile.Downloads))),
            "MANIFEST_INVALID", "missing downloads field is rejected");
        Reject(Change(root => ManifestFile(root)[nameof(ManagedFile.Downloads)] = null),
            "MANIFEST_INVALID", "null downloads field is rejected");
        Reject(Change(root => ManifestFile(root)[nameof(ManagedFile.Downloads)]!.AsArray()[0] = null),
            "MANIFEST_INVALID", "null download entries are rejected");
        Reject(Change(root => ManifestFile(root)[nameof(ManagedFile.Sha512)] = null),
            "MANIFEST_INVALID", "null managed-file hashes are rejected");
        Reject(Change(root => ManifestFile(root)[nameof(ManagedFile.Path)] = null),
            "MANIFEST_INVALID", "null managed-file paths are rejected");
        Reject(Change(root => root[nameof(InstallationManifest.Files)]!.AsArray().Add(
                root[nameof(InstallationManifest.Files)]![0]!.DeepClone())),
            "MANIFEST_INVALID", "duplicate managed-file paths are rejected");
        Reject(Change(root => ManifestFile(root)[nameof(ManagedFile.Path)] = "saves/world/level.dat"),
            "MANIFEST_INVALID", "reserved managed-file paths are rejected");

        var overrideInstance = Path.Combine(tempRoot, "manifest-empty-override-downloads");
        new InstallationManifest
        {
            PackVersion = manifest.PackVersion,
            MinecraftVersion = manifest.MinecraftVersion,
            FabricLoaderVersion = manifest.FabricLoaderVersion,
            PackArchiveSha512 = manifest.PackArchiveSha512,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = [new ManagedFile("config/test.txt", new string('C', 128), [], true, 0)]
        }.SaveAtomic(overrideInstance);
        Equal(0, InstallationManifest.Load(overrideInstance).Files[0].Downloads.Length,
            "schema 1 accepts an explicit empty downloads array for overrides");

        var emptyInstance = Path.Combine(tempRoot, "manifest-explicit-empty-files");
        new InstallationManifest
        {
            PackVersion = manifest.PackVersion,
            MinecraftVersion = manifest.MinecraftVersion,
            FabricLoaderVersion = manifest.FabricLoaderVersion,
            PackArchiveSha512 = manifest.PackArchiveSha512,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = []
        }.SaveAtomic(emptyInstance);
        Equal(0, InstallationManifest.Load(emptyInstance).Files.Count,
            "schema 1 accepts an explicit empty files array");
    }

    private static async Task VerifyUninstallPreflightAsync(string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "uninstall-preflight-root");
        var indexedBytes = Bytes("preflight indexed file");
        var overrideBytes = Bytes("preflight override");
        var packPath = Path.Combine(tempRoot, "uninstall-preflight.mrpack");
        CreatePack(packPath, "0.1.0", [new TestFile("mods/test.jar", indexedBytes)],
            [new TestOverride("config/test.txt", overrideBytes)]);
        var packHash = HashFile(packPath);
        var downloadRequests = 0;
        using var installer = new InstallService(new DownloadEngine(new DelegateHandler(_ =>
        {
            Interlocked.Increment(ref downloadRequests);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(indexedBytes) };
        })));
        var installed = await installer.InstallAsync(packPath, packHash, installRoot);
        True(installed.Success, "preflight fixture installs");
        var instance = installed.GameDirectory!;
        var manifestPath = Path.Combine(instance, InstallationManifest.FileName);
        var originalManifest = File.ReadAllText(manifestPath);
        var managedPath = Path.Combine(instance, "mods", "test.jar");
        var overridePath = Path.Combine(instance, "config", "test.txt");
        var worldPath = Path.Combine(instance, "saves", "world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(worldPath)!);
        File.WriteAllText(worldPath, "preserved world");

        var launcherRoot = Path.Combine(tempRoot, "uninstall-preflight-fake-launcher");
        Directory.CreateDirectory(launcherRoot);
        var profilePath = Path.Combine(launcherRoot, "launcher_profiles.json");
        File.WriteAllText(profilePath, LauncherProfile.BuildFixtureCandidate("{\"profiles\":{}}",
            instance + Path.DirectorySeparatorChar, TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion));
        using var launcher = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { });
        var activeMarkerPath = Path.Combine(installRoot, ".minepack-active.json");
        var profileHash = HashFile(profilePath);
        var managedHash = HashFile(managedPath);
        var overrideHash = HashFile(overridePath);
        var worldHash = HashFile(worldPath);
        var activeMarkerHash = HashFile(activeMarkerPath);

        using (var managedFileLock = new FileStream(managedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                using var instanceUse = InstanceUseGuard.AcquireForTesting(instance, ["mods/test.jar"],
                    Array.Empty<(int ProcessId, string ProcessName)>(), _ => string.Empty);
                launcher.RemoveOwnProfile(instance);
                throw new InvalidOperationException("Expected a locked managed file to refuse profile removal.");
            }
            catch (InstallerException ex) when (ex.Code == "INSTANCE_IN_USE") { }
        }
        AssertNoWrites("managed-file use preflight");
        Pass("managed-file preflight blocks scoped profile removal before Launcher or instance writes");

        var corruptManagedBytes = Bytes("corrupt before repair preflight");
        File.WriteAllBytes(managedPath, corruptManagedBytes);
        managedHash = HashFile(managedPath);
        var requestsBeforeRepair = Volatile.Read(ref downloadRequests);
        var logsBeforeRepair = Directory.GetFiles(Path.Combine(installRoot, "logs"), "*.jsonl").Length;
        using (var secondManagedLock = new FileStream(overridePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var blockedRepair = await installer.RepairAsync(instance, packPath, packHash);
            True(!blockedRepair.Success && blockedRepair.Code == "INSTANCE_IN_USE",
                "repair reports a controlled refusal when a later managed file is locked");
            Equal(requestsBeforeRepair, Volatile.Read(ref downloadRequests),
                "locked second managed file is rejected before replacement downloads");
            Equal(logsBeforeRepair, Directory.GetFiles(Path.Combine(installRoot, "logs"), "*.jsonl").Length,
                "locked second managed file is rejected before a repair log is written");
            True(secondManagedLock.CanRead, "blocked repair preserves the locked second managed file handle");
        }
        AssertNoWrites("locked second managed file preflight");
        File.WriteAllBytes(managedPath, indexedBytes);
        managedHash = HashFile(managedPath);
        Pass("repair preflights every managed file before changing the first corrupted file");

        void AssertNoWrites(string label)
        {
            Equal(profileHash, HashFile(profilePath), $"{label} preserves fake Launcher profile bytes");
            Equal(managedHash, HashFile(managedPath), $"{label} preserves managed-file bytes");
            Equal(overrideHash, HashFile(overridePath), $"{label} preserves managed override bytes");
            Equal(worldHash, HashFile(worldPath), $"{label} preserves world bytes");
            Equal(activeMarkerHash, HashFile(activeMarkerPath), $"{label} preserves active marker bytes");
        }

        void RejectUiFlow(string selectedRoot, string archive, string archiveHash, string expectedCode, string label)
        {
            try
            {
                var active = installer.GetActiveInstancePath(selectedRoot)
                    ?? throw new InstallerException("INSTANCE_NOT_FOUND", LocalizedText.Get("UninstallInstanceNotFound"));
                _ = InstallationManifest.Load(active);
                installer.ValidateUninstallTarget(selectedRoot, active, archive, archiveHash);
                launcher.RemoveOwnProfile(active);
                throw new InvalidOperationException($"Expected {label} to stop before profile removal.");
            }
            catch (InstallerException ex) when (ex.Code == expectedCode) { }
            AssertNoWrites(label);
            Pass(label);
        }

        void RejectManifestChange(Action<JsonObject> change, string label, string expectedCode = "MANIFEST_INVALID")
        {
            var root = JsonNode.Parse(originalManifest)!.AsObject();
            change(root);
            File.WriteAllText(manifestPath, root.ToJsonString());
            try { RejectUiFlow(installRoot, packPath, packHash, expectedCode, label); }
            finally { File.WriteAllText(manifestPath, originalManifest); }
        }

        JsonObject ManifestFile(JsonObject root, string path) => root[nameof(InstallationManifest.Files)]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(file => file[nameof(ManagedFile.Path)]!.GetValue<string>() == path);

        var missingRoot = Path.Combine(tempRoot, "empty-uninstall-root");
        var missingInstance = Path.Combine(missingRoot, "instances", "missing-instance");
        Directory.CreateDirectory(Path.Combine(missingRoot, "instances"));
        RejectUiFlow(missingRoot, packPath, packHash, "INSTANCE_NOT_FOUND", "empty selected root refuses profile removal");
        try
        {
            installer.ValidateUninstallTarget(missingRoot, instance, packPath, packHash);
            throw new InvalidOperationException("Selected-root mismatch unexpectedly passed preflight.");
        }
        catch (InstallerException ex) when (ex.Code == "INSTANCE_NOT_FOUND") { }
        AssertNoWrites("selected-root mismatch");
        try
        {
            installer.ValidateUninstallTarget(missingRoot, missingInstance, packPath, packHash);
            throw new InvalidOperationException("Missing active instance unexpectedly passed preflight.");
        }
        catch (InstallerException ex) when (ex.Code == "INSTANCE_NOT_FOUND") { }
        AssertNoWrites("missing active instance");

        var inactiveInstance = Path.Combine(installRoot, "instances", "inactive-target");
        Directory.CreateDirectory(Path.Combine(inactiveInstance, "mods"));
        Directory.CreateDirectory(Path.Combine(inactiveInstance, "config"));
        File.Copy(manifestPath, Path.Combine(inactiveInstance, InstallationManifest.FileName));
        File.Copy(managedPath, Path.Combine(inactiveInstance, "mods", "test.jar"));
        File.Copy(Path.Combine(instance, "config", "test.txt"), Path.Combine(inactiveInstance, "config", "test.txt"));
        var inactiveManagedHash = HashFile(Path.Combine(inactiveInstance, "mods", "test.jar"));
        var inactiveManifest = installer.ValidateUninstallTarget(installRoot, inactiveInstance, packPath, packHash);
        Equal("0.1.0", inactiveManifest.PackVersion,
            "explicit inactive target resolves to its trusted release before user confirmation");
        Equal(inactiveManagedHash, HashFile(Path.Combine(inactiveInstance, "mods", "test.jar")),
            "inactive target preflight preserves its managed file");
        AssertNoWrites("inactive target preflight");
        Pass("valid inactive target is preflighted without mutating its files or active Launcher profile");

        var missingArchive = Path.Combine(tempRoot, "missing-uninstall.mrpack");
        RejectUiFlow(installRoot, missingArchive, packHash, "PACK_NOT_FOUND", "missing pinned archive is rejected before profile removal");
        var corruptArchive = Path.Combine(tempRoot, "corrupt-uninstall.mrpack");
        File.Copy(packPath, corruptArchive);
        File.AppendAllText(corruptArchive, "changed");
        RejectUiFlow(installRoot, corruptArchive, packHash, "PACK_HASH_MISMATCH", "corrupt pinned archive is rejected before profile removal");

        File.WriteAllText(manifestPath, "{");
        RejectUiFlow(installRoot, packPath, packHash, "MANIFEST_INVALID", "malformed manifest is rejected before profile removal");
        File.WriteAllText(manifestPath, originalManifest);
        RejectManifestChange(root => root[nameof(InstallationManifest.PackVersion)] = "9.9.9",
            "release mismatch is rejected before profile removal", "RELEASE_MISMATCH");
        RejectManifestChange(root => ManifestFile(root, "config/test.txt")[nameof(ManagedFile.Sha512)] = new string('0', 128),
            "override hash mismatch is rejected before profile removal");
        RejectManifestChange(root => ManifestFile(root, "config/test.txt")[nameof(ManagedFile.Size)] = 99,
            "override size mismatch is rejected before profile removal");
        RejectManifestChange(root => ManifestFile(root, "config/test.txt")[nameof(ManagedFile.IsOverride)] = false,
            "override type mismatch is rejected before profile removal");
        RejectManifestChange(root => ManifestFile(root, "config/test.txt")[nameof(ManagedFile.Downloads)]!.AsArray()
                .Add("https://cdn.modrinth.com/data/extra/version/file.jar"),
            "extra override URL is rejected before profile removal");
        RejectManifestChange(root => ManifestFile(root, "mods/test.jar")[nameof(ManagedFile.Downloads)]!.AsArray()
                .Add("https://cdn.modrinth.com/data/extra/version/file.jar"),
            "extra indexed URL is rejected before profile removal");
        RejectManifestChange(root => ManifestFile(root, "mods/test.jar")[nameof(ManagedFile.Size)] = 99,
            "indexed size mismatch is rejected before profile removal");

        var modsDirectory = Path.Combine(instance, "mods");
        var savedModsDirectory = Path.Combine(instance, "mods-before-reparse-check");
        var externalManagedDirectory = Path.Combine(tempRoot, "external-managed-target");
        Directory.CreateDirectory(externalManagedDirectory);
        File.Copy(managedPath, Path.Combine(externalManagedDirectory, "test.jar"));
        Directory.Move(modsDirectory, savedModsDirectory);
        try
        {
            Directory.CreateSymbolicLink(modsDirectory, externalManagedDirectory);
            RejectUiFlow(installRoot, packPath, packHash, "PATH_REPARSE_BLOCKED",
                "managed target reparse point is rejected before profile removal");
        }
        finally
        {
            if (Directory.Exists(modsDirectory)) Directory.Delete(modsDirectory);
            Directory.Move(savedModsDirectory, modsDirectory);
        }

        try
        {
            installer.ValidateUninstallTarget(installRoot + Path.DirectorySeparatorChar,
                instance + Path.DirectorySeparatorChar, packPath, packHash);
            launcher.RemoveOwnProfile(instance);
        }
        catch (InstallerException ex)
        {
            throw new InvalidOperationException($"Equivalent paths with trailing separators were rejected ({ex.Code}).", ex);
        }
        using (var profile = JsonDocument.Parse(File.ReadAllText(profilePath)))
            True(!profile.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "valid schema 1 preflight permits scoped removal with equivalent paths");
        profileHash = HashFile(profilePath);

        File.WriteAllText(manifestPath, "{");
        var logDirectory = Path.Combine(installRoot, "logs");
        var logsBefore = Directory.GetFiles(logDirectory, "*.jsonl").Length;
        var uninstall = await installer.UninstallAsync(installRoot, instance, packPath, packHash);
        True(!uninstall.Success && uninstall.Code == "MANIFEST_INVALID", "Core Uninstall repeats manifest preflight before mutation");
        Equal(logsBefore, Directory.GetFiles(logDirectory, "*.jsonl").Length, "failed Core preflight creates no new log side effect");
        File.WriteAllText(manifestPath, originalManifest);
        AssertNoWrites("failed Core preflight");

        var unknownSizeRoot = Path.Combine(tempRoot, "uninstall-unknown-size-root");
        var unknownSizeBytes = Bytes("unknown index size");
        var unknownSizePack = Path.Combine(tempRoot, "uninstall-unknown-size.mrpack");
        CreatePack(unknownSizePack, "0.2.0", [new TestFile("mods/unknown-size.jar", unknownSizeBytes, OmitSize: true)]);
        var unknownSizeHash = HashFile(unknownSizePack);
        using var unknownSizeInstaller = new InstallService(new DownloadEngine(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(unknownSizeBytes) })));
        var unknownSizeInstall = await unknownSizeInstaller.InstallAsync(unknownSizePack, unknownSizeHash, unknownSizeRoot);
        True(unknownSizeInstall.Success, "unknown indexed size fixture installs");
        _ = unknownSizeInstaller.ValidateUninstallTarget(unknownSizeRoot, unknownSizeInstall.GameDirectory!,
            unknownSizePack, unknownSizeHash);
        Pass("unknown archive file size stays supported without using installed bytes as an oracle");
    }

    private static async Task VerifyInstallRepairAndUninstallAsync(string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "install-root");
        var packageBytes = Bytes("smoke payload v1");
        var packPath = Path.Combine(tempRoot, "valid-test-pack.mrpack");
        CreatePack(packPath, "0.1.0", [new TestFile("mods/test.jar", packageBytes)]);
        var packHash = HashFile(packPath);
        var installOperationLog = new OperationLog("install", "9.9.9", installRoot, "0.1.0",
            TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, packHash);
        var failDownloads = false;
        var currentDownloadBytes = packageBytes;
        var handler = new DelegateHandler(_ => failDownloads
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(currentDownloadBytes) });

        using var installer = new InstallService(new DownloadEngine(handler));
        var install = await installer.InstallAsync(packPath, packHash, installRoot, operationLog: installOperationLog);
        True(install.Success, "fixture pack installs successfully");
        var reactivated = await installer.InstallAsync(packPath, packHash, installRoot, operationLog: installOperationLog);
        True(reactivated.Success, "reactivating the same release repairs under the supplied operation context");
        var unexpectedInstallCancel = await installer.InstallAsync(packPath, packHash,
            Path.Combine(tempRoot, "unexpected-install-cancel-root"), new DelegateProgress<InstallProgress>(item =>
            {
                if (item.Stage == "prepare") throw new OperationCanceledException("non-caller fixture cancellation");
            }));
        True(!unexpectedInstallCancel.Success && unexpectedInstallCancel.Code == "INSTALL_FAILED",
            "non-caller Install cancellation is reported as failure, not user cancellation");
        var instance = install.GameDirectory ?? throw new InvalidOperationException("Install did not return its instance directory.");
        Equal(instance, installer.GetActiveInstancePath(installRoot), "successful instance becomes active");
        var managedPath = Path.Combine(instance, "mods", "test.jar");
        Equal(HashBytes(packageBytes), HashFile(managedPath), "installed managed file hash");
        True(File.Exists(Path.Combine(instance, InstallationManifest.FileName)), "install manifest is written");
        var launcherFailureSecret = "TOKEN_SENTINEL?access_token=QUERY_SENTINEL {\"account\":\"ACCOUNT_SENTINEL\"}";
        var fakeLauncherTarget = new MinecraftLauncherTarget(MinecraftLauncherKind.Win32,
            Path.Combine(tempRoot, "fake-launcher.exe"));
        var fakeLauncher = new FakeLauncherPlatform([fakeLauncherTarget])
        {
            FailStart = true,
            StartFailureMessage = launcherFailureSecret
        };
        var launcherStart = await new MinecraftLauncherController(fakeLauncher).ConfigureAndStartAsync(
            fakeLauncherTarget, static () => Task.CompletedTask, installOperationLog);
        Equal(MinecraftLauncherStartStatus.Failed, launcherStart.Status,
            "fake Launcher start failure remains a separate profile outcome");
        installOperationLog.Complete("profile_pending", "LAUNCHER_START_FAILED");
        var installLogPath = installOperationLog.CurrentLogPath
            ?? throw new InvalidOperationException("Install operation log was not bound to its validated root.");
        Equal(installLogPath, install.LogPath, "install returns its current operation log");
        var installLog = File.ReadAllText(installLogPath);
        True(installLog.Contains("download_attempt", StringComparison.Ordinal) &&
             installLog.Contains("download_response", StringComparison.Ordinal) &&
             installLog.Contains("download_hash_verified", StringComparison.Ordinal), "download diagnostics record attempt, HTTP response, and verified hash");
        True(!installLog.Contains(TestDownload.AbsoluteUri, StringComparison.Ordinal), "download diagnostics omit the full URL path");
        True(!installLog.Contains(launcherFailureSecret, StringComparison.Ordinal) &&
             installLog.Contains("launcher_start_failed", StringComparison.Ordinal) &&
             installLog.Contains("innerHResult", StringComparison.Ordinal),
            "Launcher exceptions log safe types and HRESULTs without raw sentinel messages");
        var records = installLog.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToList();
        try
        {
            True(records.All(record => record.RootElement.GetProperty("operationId").GetString() == installOperationLog.OperationId),
                "install, reactivation, repair, and Launcher stages share one operation id");
            True(records.All(record => record.RootElement.GetProperty("installerVersion").GetString() == "9.9.9"),
                "operation log uses the supplied installer assembly version");
            var stages = records.Select(record => record.RootElement.GetProperty("stage").GetString()).ToHashSet();
            True(new[] { "preflight", "download", "verify", "prepare", "commit", "profile", "end" }.All(stages.Contains),
                "correlated install records preflight through profile and end stages");
            True(records.Any(record => record.RootElement.GetProperty("eventName").GetString() == "install_reactivated"),
                "composed reactivation is recorded in the same operation log");
        }
        finally { foreach (var record in records) record.Dispose(); }
        var exportPath = Path.Combine(tempRoot, "current-operation-export.jsonl");
        True(installOperationLog.TryExportCurrent(exportPath), "current operation diagnostic export succeeds");
        True(File.ReadAllBytes(installLogPath).SequenceEqual(File.ReadAllBytes(exportPath)),
            "diagnostic export contains only the current safe JSONL");
        True(!File.ReadAllText(exportPath).Contains("TOKEN_SENTINEL", StringComparison.Ordinal) &&
             !File.ReadAllText(exportPath).Contains("QUERY_SENTINEL", StringComparison.Ordinal) &&
             !File.ReadAllText(exportPath).Contains("ACCOUNT_SENTINEL", StringComparison.Ordinal),
            "current diagnostic export excludes token, query, and account sentinels");
        Pass("verified download installs to a separate versioned instance");

        var worldPath = Path.Combine(instance, "saves", "world", "level.dat");
        var screenshotPath = Path.Combine(instance, "screenshots", "keep.png");
        var unknownPath = Path.Combine(instance, "custom-user-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(worldPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        File.WriteAllText(worldPath, "keep world");
        File.WriteAllText(screenshotPath, "keep screenshot");
        File.WriteAllText(unknownPath, "keep user file");
        await VerifyWorldImportSafetyAsync(tempRoot, instance);
        File.WriteAllText(managedPath, "corrupted");

        var repairOperationLog = new OperationLog("repair", "9.9.9", installRoot, "0.1.0",
            TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, packHash);
        var repair = await installer.RepairAsync(instance, packPath, packHash, operationLog: repairOperationLog);
        repairOperationLog.Complete("completed");
        True(repair.Success, $"repair succeeds ({repair.Code}: {repair.Message})");
        Equal(HashBytes(packageBytes), HashFile(managedPath), "repair restores expected managed hash");
        var repairLog = File.ReadAllText(repairOperationLog.CurrentLogPath
            ?? throw new InvalidOperationException("Repair operation log was not bound to its validated root."));
        True(repairLog.Contains("managed_file_check", StringComparison.Ordinal) &&
             repairLog.Contains("\"stage\":\"verify\"", StringComparison.Ordinal) &&
             repairLog.Contains("repair_complete", StringComparison.Ordinal),
            "Repair logs the existing managed-file verification and transaction completion stages");
        var managedBeforeUnexpectedCancel = HashFile(managedPath);
        var manifestBeforeUnexpectedCancel = HashFile(Path.Combine(instance, InstallationManifest.FileName));
        var markerBeforeUnexpectedCancel = HashFile(Path.Combine(installRoot, ".minepack-active.json"));
        var unexpectedRepairCancel = await installer.RepairAsync(instance, packPath, packHash,
            new DelegateProgress<InstallProgress>(item =>
            {
                if (item.Stage == "repair") throw new OperationCanceledException("non-caller fixture cancellation");
            }));
        True(!unexpectedRepairCancel.Success && unexpectedRepairCancel.Code == "REPAIR_FAILED",
            "non-caller Repair cancellation is reported as failure, not user cancellation");
        Equal(managedBeforeUnexpectedCancel, HashFile(managedPath), "non-caller Repair cancellation preserves managed bytes");
        Equal(manifestBeforeUnexpectedCancel, HashFile(Path.Combine(instance, InstallationManifest.FileName)),
            "non-caller Repair cancellation preserves manifest bytes");
        Equal(markerBeforeUnexpectedCancel, HashFile(Path.Combine(installRoot, ".minepack-active.json")),
            "non-caller Repair cancellation preserves active marker bytes");
        True(File.Exists(worldPath), "repair preserves world");
        Pass("repair restores modified managed data and preserves user data");

        failDownloads = true;
        var nextPackPath = Path.Combine(tempRoot, "failed-update.mrpack");
        var nextBytes = Bytes("smoke payload v2");
        CreatePack(nextPackPath, "0.2.0", [new TestFile("mods/test.jar", nextBytes)]);
        var nextInstance = InstancePath(installRoot, "0.2.0", HashFile(nextPackPath));
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            var canceledInstall = await installer.InstallAsync(nextPackPath, HashFile(nextPackPath), installRoot, cancellationToken: canceled.Token);
            True(!canceledInstall.Success && canceledInstall.Code == "CANCELLED", "pre-commit cancellation is reported");
        }
        True(!Directory.Exists(nextInstance), "cancellation before activation leaves no final version directory");
        Equal(instance, installer.GetActiveInstancePath(installRoot), "cancellation leaves the prior active marker unchanged");

        var failedInstall = await installer.InstallAsync(nextPackPath, HashFile(nextPackPath), installRoot);
        True(!failedInstall.Success, "simulated download failure is reported");
        var failureLogPath = failedInstall.LogPath ?? installer.GetLatestLogPath(installRoot)
            ?? throw new InvalidOperationException("Failed install did not leave a diagnostic log.");
        var failureLog = File.ReadAllText(failureLogPath);
        True(failureLog.Contains("download_retry", StringComparison.Ordinal) && failureLog.Contains("DOWNLOAD_HTTP", StringComparison.Ordinal),
            "failed downloads record retries and a stable reason code");
        Equal(instance, installer.GetActiveInstancePath(installRoot), "failed installation leaves active marker unchanged");
        Equal(HashBytes(packageBytes), HashFile(managedPath), "failed installation leaves prior instance unchanged");
        True(!Directory.Exists(nextInstance), "failed download leaves no final version directory");
        Pass("failed download leaves active instance and its files unchanged");

        var uninstallOperationLog = new OperationLog("uninstall", "9.9.9", installRoot, "0.1.0",
            TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, packHash);
        var uninstall = await installer.UninstallAsync(installRoot, instance, packPath, packHash,
            operationLog: uninstallOperationLog);
        uninstallOperationLog.Complete("completed");
        True(uninstall.Success, "uninstall succeeds");
        var uninstallLog = File.ReadAllText(uninstallOperationLog.CurrentLogPath
            ?? throw new InvalidOperationException("Uninstall operation log was not bound to its validated root."));
        True(uninstallLog.Contains("release_metadata_verified", StringComparison.Ordinal) &&
             uninstallLog.Contains("\"stage\":\"verify\"", StringComparison.Ordinal) &&
             uninstallLog.Contains("uninstall_complete", StringComparison.Ordinal),
            "Uninstall logs its existing pinned-release preflight and completed transaction");
        True(!File.Exists(managedPath), "uninstall removes managed file");
        True(File.Exists(worldPath) && File.Exists(Path.Combine(instance, "saves", "new-world", "level.dat")) &&
             File.Exists(screenshotPath) && File.Exists(unknownPath), "uninstall preserves worlds, screenshots, and unknown files");
        True(installer.GetActiveInstancePath(installRoot) is null, "uninstall clears its active marker");
        Pass("uninstall removes only managed files and preserves user data");

        failDownloads = false;
        currentDownloadBytes = packageBytes;
        var reinstall = await installer.InstallAsync(packPath, packHash, installRoot);
        True(reinstall.Success, "same release reinstalls after uninstall preserves user data without a manifest");
        var reinstalledInstance = reinstall.GameDirectory!;
        True(!reinstalledInstance.Equals(instance, StringComparison.OrdinalIgnoreCase) &&
             File.Exists(worldPath) && File.Exists(screenshotPath) && File.Exists(unknownPath),
            "reinstall leaves the uninstalled instance and its user data untouched");
        True(File.Exists(Path.Combine(reinstalledInstance, InstallationManifest.FileName)),
            "reinstall writes a new local manifest");
        Equal(reinstalledInstance, installer.GetActiveInstancePath(installRoot), "reinstalled instance becomes active");
        Pass("reinstall after uninstall uses a fresh instance without losing user data");
        await VerifyUnvalidatedWorldLogBindingAsync(tempRoot,
            Path.Combine(reinstalledInstance, InstallationManifest.FileName));

        var markerRoot = Path.Combine(tempRoot, "marker-install-root");
        Directory.CreateDirectory(markerRoot);
        var activeMarker = Path.Combine(markerRoot, ".minepack-active.json");
        const string unrelatedMarker = "{\"owner\":\"unrecognized\"}";
        File.WriteAllText(activeMarker, unrelatedMarker);
        var markerBytes = Bytes("marker commit fixture");
        currentDownloadBytes = markerBytes;
        var markerPack = Path.Combine(tempRoot, "marker-pack.mrpack");
        CreatePack(markerPack, "0.3.0", [new TestFile("mods/test.jar", markerBytes)]);
        failDownloads = false;
        var markerInstall = await installer.InstallAsync(markerPack, HashFile(markerPack), markerRoot);
        True(!markerInstall.Success && markerInstall.Code == "ACTIVE_MARKER_RECOVERY_REQUIRED",
            $"unowned active marker prevents activation (success={markerInstall.Success}, code={markerInstall.Code}, message={markerInstall.Message})");
        Equal(unrelatedMarker, File.ReadAllText(activeMarker), "unowned marker remains unchanged");
        var markerInstance = InstancePath(markerRoot, "0.3.0", HashFile(markerPack));
        True(!Directory.Exists(markerInstance), "failed marker commit removes the moved but inactive version directory");

        File.Delete(activeMarker);
        var retry = await installer.InstallAsync(markerPack, HashFile(markerPack), markerRoot,
            new DelegateProgress<InstallProgress>(item =>
            {
                if (item.Stage == "complete") throw new InvalidOperationException("post-commit progress callback failure");
            }));
        True(retry.Success, "same release can be retried after marker failure; post-commit progress is best-effort");
        Equal(markerInstance, installer.GetActiveInstancePath(markerRoot), "successful retry atomically activates the new version");
        Pass("failed marker commit leaves no orphan and retry succeeds");
        var markerUninstall = await installer.UninstallAsync(markerRoot, markerInstance, markerPack, HashFile(markerPack));
        True(markerUninstall.Success, "marker fixture cleanup succeeds");
    }

    private static async Task VerifyPrismInstanceLifecycleAsync(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "Prism lifecycle данные");
        var dataRoot = Path.Combine(root, "Prism data");
        var instancesRoot = Path.Combine(root, "separate volume instances");
        var stateRoot = Path.Combine(root, "installer state");
        var executableDirectory = Path.Combine(root, "Prism Launcher app");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(instancesRoot);
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(dataRoot, "prismlauncher.cfg"),
            $"[General]\r\nInstanceDir = \"{instancesRoot}\"\r\n");
        var executable = Path.Combine(executableDirectory, "prismlauncher.exe");
        File.WriteAllBytes(executable, [0x4D, 0x5A]);
        var fingerprint = InstallationLayout.ComputePrismFingerprint(dataRoot, instancesRoot);
        var prismTarget = new PrismLauncherTarget(executable, dataRoot, instancesRoot, fingerprint, "smoke fixture");
        var bytes = Bytes("prism lifecycle payload");
        var packPath = Path.Combine(root, "lifecycle.mrpack");
        CreatePack(packPath, "0.1.0", [new TestFile("mods/lifecycle.jar", bytes)],
            minecraftVersion: TestPackRelease.MinecraftVersion,
            fabricLoaderVersion: TestPackRelease.FabricLoaderVersion);
        var packHash = HashFile(packPath);
        var pack = PackArchive.Open(packPath, packHash);
        var release = new KnownPackRelease("Frontier", "0.1.0", TestPackRelease.MinecraftVersion,
            TestPackRelease.FabricLoaderVersion, "fixture.mrpack", packHash);
        var instanceName = release.InstanceDirectoryPrefix;
        var layout = InstallationLayout.PrismForTesting(prismTarget, instanceName, stateRoot, release);
        var downloads = 0;
        using var installer = new InstallService(new DownloadEngine(new DelegateHandler(_ =>
        {
            Interlocked.Increment(ref downloads);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        })));

        var installed = await installer.InstallAsync(packPath, packHash, layout);
        True(installed.Success, $"Prism fixture installs through the common pipeline ({installed.Code}: {installed.Message})");
        File.Copy(packPath, Path.Combine(root, "fixture.mrpack"));
        var prismCatalog = InstalledInstanceCatalog.Enumerate(layout, root);
        True(prismCatalog.Count == 1 && prismCatalog[0].IsTrusted &&
             Path.GetFullPath(prismCatalog[0].Path).Equals(Path.GetFullPath(layout.GameDirectory), StringComparison.OrdinalIgnoreCase) &&
             prismCatalog[0].Layout is not null,
            "Prism catalog lists only the trusted nested game directory with its verified layout");
        var foreignWrapper = Path.Combine(instancesRoot, "foreign-prism-instance");
        Directory.CreateDirectory(foreignWrapper);
        File.WriteAllText(Path.Combine(foreignWrapper, "instance.cfg"), "[General]\nname=MinePack for 26.2\n");
        True(InstalledInstanceCatalog.Enumerate(layout, root).Count == 1,
            "Prism catalog does not claim an unrelated instance from its visible name");
        var foreignLayout = layout.ForInstance(foreignWrapper);
        var foreignEntry = new InstalledInstanceEntry(foreignLayout.GameDirectory, Path.GetFileName(foreignWrapper),
            InstalledInstanceState.Residue, null, null) { Layout = foreignLayout };
        try
        {
            _ = InstanceRemovalService.PrepareCleanupForTesting(foreignEntry, instancesRoot, root,
                Path.Combine(root, "missing official launcher"));
            throw new InvalidOperationException("Expected foreign Prism wrapper cleanup to be refused.");
        }
        catch (InstallerException ex) when (ex.Code == "INSTANCE_CLEANUP_UNAVAILABLE") { }
        Equal(layout.GameDirectory, installed.GameDirectory, "Prism install returns the nested game directory");
        True(File.Exists(Path.Combine(layout.InstanceDirectory, "instance.cfg")), "Prism instance.cfg is published with wrapper");
        True(File.Exists(Path.Combine(layout.InstanceDirectory, "mmc-pack.json")), "Prism component pins are published with wrapper");
        True(PrismLauncherService.HasOwnedBinding(layout), "Prism wrapper and independent local ownership record bind the install");
        Equal(layout.GameDirectory, installer.GetActiveInstancePath(layout), "schema 2 active marker resolves nested Prism game directory through the selected layout");
        var pendingSiblingName = instanceName + "-reinstall-" + Guid.NewGuid().ToString("N");
        var pendingSibling = layout.ForInstance(Path.Combine(instancesRoot, pendingSiblingName));
        Directory.CreateDirectory(pendingSibling.GameDirectory);
        PrismLauncherService.PrepareFreshInstance(pendingSibling.InstanceDirectory, pendingSibling, pack);
        PrismLauncherService.WriteLocalOwnershipRecord(pendingSibling, pack);
        _ = ManagedFileTransaction.BeginRepair(pendingSibling, pack);
        try
        {
            ManagedFileTransaction.EnsureNoPendingForOtherOwnedInstances(layout);
            throw new InvalidOperationException("Expected another owned Prism journal to block the shared active marker.");
        }
        catch (InstallerException ex) when (ex.Code == "TRANSACTION_RECOVERY_REQUIRED") { }
        finally
        {
            Directory.Delete(pendingSibling.InstanceDirectory, recursive: true);
            File.Delete(Path.Combine(stateRoot, "ownership", pendingSiblingName + ".json"));
        }
        Equal(layout.GameDirectory, installer.GetActiveInstancePath(layout),
            "cross-instance pending Prism journal is rejected without changing the shared active marker");
        var cfg = File.ReadAllText(Path.Combine(layout.InstanceDirectory, "instance.cfg"));
        True(cfg.Contains("name=MinePack for 26.2", StringComparison.Ordinal), "Prism display name uses the shared exact versioned name");
        using (var components = JsonDocument.Parse(File.ReadAllText(Path.Combine(layout.InstanceDirectory, "mmc-pack.json"))))
            Equal(TestPackRelease.MinecraftVersion,
                components.RootElement.GetProperty("components")[0].GetProperty("version").GetString(),
                "Prism metadata pins the Minecraft component");

        var sourceWorld = Path.Combine(root, "world source", "saves", "imported-world");
        Directory.CreateDirectory(sourceWorld);
        File.WriteAllText(Path.Combine(sourceWorld, "level.dat"), "import into Prism game directory");
        File.WriteAllBytes(Path.Combine(sourceWorld, "session.lock"), Bytes("prism source lock"));
        var imported = await WorldImportService.ImportAsync(Path.Combine(root, "world source"), layout);
        Equal(1, imported.Imported, "Prism world import uses the selected nested game directory");
        Equal("import into Prism game directory", File.ReadAllText(Path.Combine(layout.GameDirectory,
            "saves", "imported-world", "level.dat")), "Prism world import writes under minecraft/saves");

        var revokedWorld = Path.Combine(root, "revoked world source", "saves", "ownership-drop-world");
        Directory.CreateDirectory(revokedWorld);
        File.WriteAllText(Path.Combine(revokedWorld, "level.dat"), "ownership changed during import");
        File.WriteAllBytes(Path.Combine(revokedWorld, "session.lock"), Bytes("revoked source lock"));
        WorldImportFailureException? ownershipFailure = null;
        using (WorldImportService.UseCheckpointForTesting(checkpoint =>
               {
                   if (checkpoint == "after-world-file-copy:ownership-drop-world")
                       PrismLauncherService.RemoveLocalOwnershipRecord(layout, pack);
               }))
        {
            try { await WorldImportService.ImportAsync(Path.Combine(root, "revoked world source"), layout); }
            catch (WorldImportFailureException ex) when (ex.Code == "PRISM_INSTANCE_OWNERSHIP_CONFLICT")
            { ownershipFailure = ex; }
        }
        True(ownershipFailure is not null, "Prism binding is rechecked after world staging awaits");
        True(!Directory.Exists(Path.Combine(layout.GameDirectory, "saves", "ownership-drop-world")),
            "world is not published when its Prism ownership binding changes during copy");
        True(!Directory.EnumerateDirectories(Path.Combine(layout.GameDirectory, "saves"), ".minepack-import-*").Any(),
            "ownership failure removes only the unpublished import staging directory");
        PrismLauncherService.WriteLocalOwnershipRecord(layout, pack);
        True(PrismLauncherService.HasOwnedBinding(layout), "fixture restores its independent Prism ownership record");

        var foreignManifestGame = Path.Combine(foreignWrapper, "minecraft");
        Directory.CreateDirectory(foreignManifestGame);
        File.Copy(Path.Combine(layout.GameDirectory, InstallationManifest.FileName),
            Path.Combine(foreignManifestGame, InstallationManifest.FileName));
        var foreignImportLayout = layout.ForInstance(foreignWrapper);
        try
        {
            await WorldImportService.ImportAsync(Path.Combine(root, "world source"), foreignImportLayout);
            throw new InvalidOperationException("Expected Prism world import without an independent ownership binding to fail.");
        }
        catch (InstallerException ex) when (ex.Code == "PRISM_INSTANCE_OWNERSHIP_CONFLICT") { }
        True(!Directory.Exists(Path.Combine(foreignManifestGame, "saves")),
            "Prism world import rejects a copied manifest before writing into an unowned wrapper");

        var managed = Path.Combine(layout.GameDirectory, "mods", "lifecycle.jar");
        var options = Path.Combine(layout.GameDirectory, "options.txt");
        var world = Path.Combine(layout.GameDirectory, "saves", "sentinel", "level.dat");
        var prismConfig = Path.Combine(layout.InstanceDirectory, "instance.cfg");
        var prismComponents = Path.Combine(layout.InstanceDirectory, "mmc-pack.json");
        Directory.CreateDirectory(Path.GetDirectoryName(world)!);
        File.WriteAllText(world, "preserve prism world");
        var userOptions = Bytes("version:4903\nenableVsync:true\ncustom:preserve\n");
        File.WriteAllBytes(options, userOptions);

        var userConfig = Bytes("[General]\r\nConfigVersion=1.2\r\nInstanceType=OneSix\r\nname=Manual local title\r\nnotes=keep these notes\r\nJavaPath=C:\\Java\\custom\\bin\\javaw.exe\r\nOverrideMemory=false\r\nMaxMemAlloc=4096\r\nMinMemAlloc=256\r\n");
        File.WriteAllBytes(prismConfig, userConfig);
        var userComponentNode = JsonNode.Parse(File.ReadAllText(prismComponents))!.AsObject();
        userComponentNode["customSetting"] = "keep component note";
        userComponentNode["components"]![0]!["cachedVersion"] = "old cached display version";
        userComponentNode["components"]![0]!["customField"] = "keep component field";
        File.WriteAllText(prismComponents, userComponentNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var userComponents = File.ReadAllBytes(prismComponents);
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-prism-metadata-repair-commit:instance.cfg")
                       throw new IOException("Prism metadata repair interruption");
               }))
        {
            var interruptedRepair = await installer.RepairAsync(layout, packPath, packHash);
            True(!interruptedRepair.Success, "Prism Repair reports a metadata commit interruption");
        }
        True(File.ReadAllBytes(prismConfig).SequenceEqual(userConfig),
            "Prism metadata rollback restores the exact previous instance.cfg bytes");
        True(File.ReadAllBytes(prismComponents).SequenceEqual(userComponents),
            "Prism metadata rollback leaves untouched component metadata byte-for-byte");

        File.WriteAllText(managed, "corrupt Prism managed file");
        var repaired = await installer.RepairAsync(layout, packPath, packHash);
        True(repaired.Success, $"Prism Repair succeeds ({repaired.Code}: {repaired.Message})");
        Equal(HashBytes(bytes), HashFile(managed), "Prism Repair restores only managed bytes");
        True(File.ReadAllBytes(options).SequenceEqual(userOptions), "Prism Repair preserves a user's existing VSync option byte-for-byte");
        Equal("preserve prism world", File.ReadAllText(world), "Prism Repair preserves user worlds");
        var normalizedConfig = File.ReadAllText(prismConfig);
        True(normalizedConfig.Contains("name=MinePack for 26.2", StringComparison.Ordinal) &&
             normalizedConfig.Contains("notes=keep these notes", StringComparison.Ordinal) &&
             normalizedConfig.Contains("JavaPath=C:\\Java\\custom\\bin\\javaw.exe", StringComparison.Ordinal) &&
             normalizedConfig.Contains("OverrideMemory=false", StringComparison.Ordinal) &&
             normalizedConfig.Contains("MaxMemAlloc=4096", StringComparison.Ordinal) &&
             normalizedConfig.Contains("MinMemAlloc=256", StringComparison.Ordinal),
            "Prism Repair normalizes only its owned profile name and preserves Java, memory, and notes fields");
        using (var normalizedComponents = JsonDocument.Parse(File.ReadAllBytes(prismComponents)))
        {
            var rootNode = normalizedComponents.RootElement;
            Equal("keep component note", rootNode.GetProperty("customSetting").GetString(),
                "Prism Repair preserves unknown component manifest fields");
            Equal("keep component field", rootNode.GetProperty("components")[0].GetProperty("customField").GetString(),
                "Prism Repair preserves unknown component fields");
            Equal(TestPackRelease.MinecraftVersion,
                rootNode.GetProperty("components")[0].GetProperty("cachedVersion").GetString(),
                "Prism Repair normalizes cached display version without changing the pinned component version");
        }

        var normalizedConfigBytes = File.ReadAllBytes(prismConfig);
        var pinnedComponentsBytes = File.ReadAllBytes(prismComponents);
        var userNamedConfig = System.Text.Encoding.UTF8.GetString(normalizedConfigBytes)
            .Replace("name=MinePack for 26.2", "name=Manual local title", StringComparison.Ordinal);
        var repairReplacementConfig = Bytes(userNamedConfig.Replace(
            "name=Manual local title", "name=MinePack for 26.2", StringComparison.Ordinal));
        File.WriteAllText(prismConfig, userNamedConfig);
        var concurrentRepairEdit = Bytes("[General]\r\nInstanceType=OneSix\r\nname=External edit before metadata replace\r\n");
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-prism-metadata-repair-commit:instance.cfg")
                       File.WriteAllBytes(prismConfig, concurrentRepairEdit);
               }))
        {
            var racedRepair = await installer.RepairAsync(layout, packPath, packHash);
            True(!racedRepair.Success && File.ReadAllBytes(prismConfig).SequenceEqual(concurrentRepairEdit),
                "Prism Repair refuses an external metadata edit made immediately before replacement");
        }
        File.WriteAllText(prismConfig, userNamedConfig);
        var recoveredRepairRace = await installer.RepairAsync(layout, packPath, packHash);
        True(recoveredRepairRace.Success && File.ReadAllBytes(prismConfig).SequenceEqual(repairReplacementConfig),
            "Prism Repair recovers after the exact original metadata bytes are restored");

        File.WriteAllText(prismConfig, userNamedConfig);
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name is "after-prism-metadata-repair-commit:instance.cfg" or
                       "before-prism-metadata-restore-write:instance.cfg")
                       throw new IOException("Prism metadata rollback interruption before atomic restore");
               }))
        {
            var interruptedRestore = await installer.RepairAsync(layout, packPath, packHash);
            True(!interruptedRestore.Success, "Prism Repair leaves a recoverable journal when metadata rollback is interrupted");
        }
        var concurrentConfig = Bytes("[General]\r\nInstanceType=OneSix\r\nname=External edit during recovery\r\n");
        File.WriteAllBytes(prismConfig, concurrentConfig);
        var rejectedConcurrentEdit = await installer.RepairAsync(layout, packPath, packHash);
        True(!rejectedConcurrentEdit.Success && rejectedConcurrentEdit.Code == "TRANSACTION_RECOVERY_REQUIRED" &&
             File.ReadAllBytes(prismConfig).SequenceEqual(concurrentConfig),
            "Prism metadata recovery refuses to overwrite a concurrent edit after RestoreIntent");
        File.WriteAllBytes(prismConfig, repairReplacementConfig);
        var resumedMetadataRepair = await installer.RepairAsync(layout, packPath, packHash);
        True(resumedMetadataRepair.Success && File.ReadAllText(prismConfig).Contains("name=MinePack for 26.2", StringComparison.Ordinal),
            "Prism metadata recovery resumes from the exact committed bytes and normalizes the owned name");

        var foreignComponents = JsonNode.Parse(pinnedComponentsBytes)!.AsObject();
        foreignComponents["components"]![0]!["version"] = "26.2.1";
        File.WriteAllText(prismComponents, foreignComponents.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var foreignMetadataBytes = File.ReadAllBytes(prismComponents);
        var rejectedForeignComponents = await installer.RepairAsync(layout, packPath, packHash);
        True(!rejectedForeignComponents.Success && rejectedForeignComponents.Code == "PRISM_INSTANCE_OWNERSHIP_CONFLICT",
            "Prism Repair fails closed when Minecraft component pin was changed by another tool");
        True(File.ReadAllBytes(prismConfig).SequenceEqual(normalizedConfigBytes) &&
             File.ReadAllBytes(prismComponents).SequenceEqual(foreignMetadataBytes) &&
             HashFile(managed).Equals(HashBytes(bytes), StringComparison.OrdinalIgnoreCase),
            "foreign Prism component pin rejection occurs before writing instance or game files");
        File.WriteAllBytes(prismComponents, pinnedComponentsBytes);

        File.Delete(prismConfig);
        File.Delete(prismComponents);
        var restoredMetadata = await installer.RepairAsync(layout, packPath, packHash);
        True(restoredMetadata.Success && File.Exists(prismConfig) && File.Exists(prismComponents),
            "Prism Repair restores missing launcher metadata from the independent ownership proof");
        True(File.ReadAllBytes(options).SequenceEqual(userOptions), "restoring Prism metadata leaves user options unchanged");

        var reinstalled = await installer.InstallAsync(packPath, packHash, layout);
        True(reinstalled.Success, $"reinstall of the same owned Prism instance repairs in place ({reinstalled.Code})");
        True(File.ReadAllBytes(options).SequenceEqual(userOptions), "same-release Prism install preserves existing options");
        var retainedConfig = File.ReadAllBytes(prismConfig);
        var retainedComponents = File.ReadAllBytes(prismComponents);
        var concurrentUninstallEdit = Bytes("[General]\r\nInstanceType=OneSix\r\nname=External edit before metadata removal\r\n");
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-prism-metadata-uninstall-move:instance.cfg")
                       File.WriteAllBytes(prismConfig, concurrentUninstallEdit);
               }))
        {
            var racedUninstall = await installer.UninstallAsync(layout, packPath, packHash);
            True(!racedUninstall.Success && File.ReadAllBytes(prismConfig).SequenceEqual(concurrentUninstallEdit),
                "Prism Uninstall refuses an external metadata edit immediately before quarantine");
        }
        File.WriteAllBytes(prismConfig, retainedConfig);
        var recoveredUninstallRace = await installer.RepairAsync(layout, packPath, packHash);
        True(recoveredUninstallRace.Success && File.ReadAllBytes(prismConfig).SequenceEqual(retainedConfig),
            "Prism Uninstall rollback recovers after exact original metadata bytes are restored");

        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-prism-metadata-uninstall-move:instance.cfg")
                       throw new IOException("Prism metadata uninstall interruption");
               }))
        {
            var interruptedUninstall = await installer.UninstallAsync(layout, packPath, packHash);
            True(!interruptedUninstall.Success, "Prism Uninstall reports an interrupted metadata removal");
        }
        True(File.ReadAllBytes(prismConfig).SequenceEqual(retainedConfig) &&
             File.ReadAllBytes(prismComponents).SequenceEqual(retainedComponents) &&
             File.Exists(Path.Combine(layout.GameDirectory, InstallationManifest.FileName)) &&
             PrismLauncherService.HasOwnedBinding(layout),
            "Prism Uninstall rollback restores metadata, manifest, and ownership binding");

        var uninstall = await installer.UninstallAsync(layout, packPath, packHash);
        True(uninstall.Success, $"Prism Uninstall succeeds ({uninstall.Code}: {uninstall.Message})");
        True(!File.Exists(prismConfig) && !File.Exists(prismComponents) &&
             File.ReadAllBytes(Path.Combine(layout.InstanceDirectory, PrismLauncherService.UninstalledConfigName)).SequenceEqual(retainedConfig) &&
             File.ReadAllBytes(Path.Combine(layout.InstanceDirectory, PrismLauncherService.UninstalledComponentManifestName)).SequenceEqual(retainedComponents),
            "Prism Uninstall removes standard launch metadata and retains its exact bytes in owned residue");
        True(File.Exists(options) && File.ReadAllBytes(options).SequenceEqual(userOptions) &&
             File.ReadAllText(world) == "preserve prism world" && !File.Exists(managed),
            "Prism Uninstall preserves user options and worlds while removing managed game files");
        var residueCatalog = InstalledInstanceCatalog.Enumerate(layout, root);
        True(residueCatalog.Count == 1 && residueCatalog[0].State == InstalledInstanceState.Residue && !residueCatalog[0].IsTrusted,
            "uninstalled Prism wrapper is catalogued as non-launchable residue");

        var residueReinstall = await installer.InstallAsync(packPath, packHash, layout);
        True(residueReinstall.Success && residueReinstall.GameDirectory is not null &&
             !Path.GetFullPath(residueReinstall.GameDirectory).Equals(Path.GetFullPath(layout.GameDirectory), StringComparison.OrdinalIgnoreCase),
            $"reinstall from retained Prism residue creates a new instance ID ({residueReinstall.Code})");
        True(File.ReadAllBytes(options).SequenceEqual(userOptions) && File.ReadAllText(world) == "preserve prism world",
            "reinstall from Prism residue leaves the previous user data intact");
        var prismWorld = Path.Combine(layout.GameDirectory, "saves", "fixture-world");
        Directory.CreateDirectory(prismWorld);
        File.WriteAllText(Path.Combine(prismWorld, "session.lock"), "fixture session lock");
        File.WriteAllText(Path.Combine(prismWorld, "level.dat"), "preserve residue world");
        var prismProfileRoot = Path.Combine(root, "synthetic official profiles");
        Directory.CreateDirectory(prismProfileRoot);
        var prismProfilePath = Path.Combine(prismProfileRoot, "launcher_profiles.json");
        File.WriteAllText(prismProfilePath, System.Text.Json.JsonSerializer.Serialize(new
        {
            profiles = new { manual = new { gameDir = layout.GameDirectory } }
        }));
        try
        {
            _ = InstanceRemovalService.PrepareCleanupForTesting(residueCatalog[0], layout.InstancesRoot, root, prismProfileRoot);
            throw new InvalidOperationException("Expected an official profile pointing to Prism's nested game directory to block cleanup.");
        }
        catch (InstallerException ex) when (ex.Code == "INSTANCE_CLEANUP_PROFILE_REFERENCE") { }
        File.WriteAllText(prismProfilePath, "{\"profiles\":{}}\n");

        var prismPending = layout.InstanceDirectory + ".minepack-transaction";
        Directory.CreateDirectory(prismPending);
        File.WriteAllText(Path.Combine(prismPending, "journal.json"), "{}");
        try
        {
            _ = InstanceRemovalService.PrepareCleanupForTesting(residueCatalog[0], layout.InstancesRoot, root,
                Path.Combine(root, "no official launcher installed"));
            throw new InvalidOperationException("Expected pending Prism recovery to block cleanup.");
        }
        catch (InstallerException ex) when (ex.Code == "TRANSACTION_RECOVERY_REQUIRED") { }
        Directory.Delete(prismPending, recursive: true);

        var activeMarkerPath = Path.Combine(stateRoot, ".minepack-active.json");
        var activeMarkerBytes = File.ReadAllBytes(activeMarkerPath);
        File.WriteAllText(activeMarkerPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 2,
            InstanceDirectory = Path.GetFileName(layout.InstanceDirectory),
            LayoutFingerprint = layout.Fingerprint
        }));
        try
        {
            _ = InstanceRemovalService.PrepareCleanupForTesting(residueCatalog[0], layout.InstancesRoot, root,
                Path.Combine(root, "no official launcher installed"));
            throw new InvalidOperationException("Expected a Prism marker targeting the residue to block cleanup.");
        }
        catch (InstallerException ex) when (ex.Code == "INSTANCE_CLEANUP_UNAVAILABLE") { }
        File.WriteAllBytes(activeMarkerPath, activeMarkerBytes);

        var prismResidueRequest = InstanceRemovalService.PrepareCleanupForTesting(residueCatalog[0],
            layout.InstancesRoot, root, Path.Combine(root, "no official launcher installed"));
        var prismRecycleRoot = Path.Combine(root, "task recycle fixture");
        Directory.CreateDirectory(prismRecycleRoot);
        var prismMovedWrapper = Path.Combine(prismRecycleRoot, Path.GetFileName(layout.InstanceDirectory));
        var prismCleanup = await InstanceRemovalService.RemoveAsync(prismResidueRequest, path =>
        {
            Directory.Move(path, prismMovedWrapper);
            return Task.CompletedTask;
        });
        True(!Directory.Exists(layout.InstanceDirectory) && Directory.Exists(prismMovedWrapper) &&
             File.ReadAllText(Path.Combine(prismMovedWrapper, "minecraft", "saves", "fixture-world", "level.dat")) ==
             "preserve residue world" && !prismCleanup.OwnershipRecordRetained,
            "Prism-only residue with session.lock can be moved to a task-local recycle fixture without official profiles");
        True(Directory.Exists(residueReinstall.GameDirectory), "Prism cleanup leaves the other installed instance intact");
        Equal(3, downloads, "managed downloads cover initial install, deliberate corruption repair, and a fresh residue reinstall");
        Pass("Prism Install/Repair/Uninstall lifecycle, metadata rollback/recovery, residue, and user-data preservation use isolated fixtures");
    }

    private static async Task VerifyOperationLoggingBestEffortAsync(string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "operation-log-denied-root");
        var payload = Bytes("logging sink fixture");
        var packPath = Path.Combine(tempRoot, "operation-log-denied.mrpack");
        CreatePack(packPath, "0.4.0", [new TestFile("mods/logging.jar", payload)]);
        var packHash = HashFile(packPath);
        using var installer = new InstallService(new DownloadEngine(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));
        var install = await installer.InstallAsync(packPath, packHash, installRoot);
        True(install.Success, "log-sink fixture installs before denying diagnostics");

        var logsDirectory = Path.Combine(installRoot, "logs");
        Directory.Move(logsDirectory, Path.Combine(installRoot, "logs-preserved"));
        File.WriteAllText(logsDirectory, "synthetic denied log sink");
        var instance = install.GameDirectory!;
        File.WriteAllText(Path.Combine(instance, "mods", "logging.jar"), "corrupt");
        var repair = await installer.RepairAsync(instance, packPath, packHash);
        True(repair.Success && repair.LogPath is null,
            "healthy Repair remains successful when the operation log cannot be created");

        var manifestPath = Path.Combine(instance, InstallationManifest.FileName);
        var manifestBytes = File.ReadAllBytes(manifestPath);
        File.WriteAllText(manifestPath, "{");
        var failedRepair = await installer.RepairAsync(instance, packPath, packHash);
        True(!failedRepair.Success && failedRepair.Code == "MANIFEST_INVALID" && failedRepair.LogPath is null,
            "a denied log sink does not mask the original Repair failure code");
        File.WriteAllBytes(manifestPath, manifestBytes);

        var uninstall = await installer.UninstallAsync(installRoot, instance, packPath, packHash);
        True(uninstall.Success && uninstall.LogPath is null,
            "Uninstall remains successful when the operation log cannot be created");
        Pass("operation logging is best-effort for successful Repair/Uninstall and preserves failure codes");
    }

    private static async Task VerifyOperationLogTargetRebindAsync(string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "operation-log-rebind-root");
        var firstBytes = Bytes("first release remains intact");
        var secondBytes = Bytes("second release target");
        var firstPackPath = Path.Combine(tempRoot, "operation-log-rebind-first.mrpack");
        var secondPackPath = Path.Combine(tempRoot, "operation-log-rebind-second.mrpack");
        CreatePack(firstPackPath, "0.5.0", [new TestFile("mods/rebind.jar", firstBytes)]);
        CreatePack(secondPackPath, "0.6.0", [new TestFile("mods/rebind.jar", secondBytes)]);
        var firstPackHash = HashFile(firstPackPath);
        var secondPackHash = HashFile(secondPackPath);
        var currentDownload = firstBytes;
        using var installer = new InstallService(new DownloadEngine(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(currentDownload) })));
        var operationLog = new OperationLog("install", "9.9.9", installRoot, "0.5.0",
            TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, firstPackHash);

        var first = await installer.InstallAsync(firstPackPath, firstPackHash, installRoot, operationLog: operationLog);
        True(first.Success, "first synthetic release installs with the shared operation context");
        currentDownload = secondBytes;
        var second = await installer.InstallAsync(secondPackPath, secondPackHash, installRoot, operationLog: operationLog);
        True(second.Success, "second synthetic release installs with the same context in the same root");
        var reactivatedFirst = await installer.InstallAsync(firstPackPath, firstPackHash, installRoot,
            operationLog: operationLog);
        True(reactivatedFirst.Success, "reactivating the first release rebinds before its metadata event");
        operationLog.Complete("completed");

        var firstManaged = Path.Combine(first.GameDirectory!, "mods", "rebind.jar");
        var secondManaged = Path.Combine(second.GameDirectory!, "mods", "rebind.jar");
        Equal(HashBytes(firstBytes), HashFile(firstManaged), "second install preserves the first target's managed bytes");
        Equal(HashBytes(secondBytes), HashFile(secondManaged), "second target contains its own pinned managed bytes");

        var logPath = operationLog.CurrentLogPath
            ?? throw new InvalidOperationException("Rebound operation log was not available.");
        var records = File.ReadAllLines(logPath).Select(line => JsonDocument.Parse(line)).ToList();
        try
        {
            True(records.All(record => record.RootElement.GetProperty("operationId").GetString() == operationLog.OperationId),
                "both release installs remain in one operation log");
            var firstTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first.GameDirectory!));
            var secondTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second.GameDirectory!));
            var finalRecord = records.Last().RootElement;
            Equal(firstTarget, finalRecord.GetProperty("targetPath").GetString(),
                "final operation entry follows reactivation to the first canonical target");
            Equal("0.5.0", finalRecord.GetProperty("packVersion").GetString(),
                "final operation entry uses the reactivated first pack identity");
            Equal(firstPackHash, finalRecord.GetProperty("packArchiveSha512").GetString(),
                "final operation entry uses the reactivated first archive hash");
            var reactivationVerification = records.Last(record =>
                record.RootElement.GetProperty("eventName").GetString() == "release_metadata_verified").RootElement;
            Equal(firstTarget, reactivationVerification.GetProperty("targetPath").GetString(),
                "release verification is attributed to the first target before Repair starts");
            Equal("0.5.0", reactivationVerification.GetProperty("packVersion").GetString(),
                "release verification is attributed to the first release before Repair starts");
            Equal(firstPackHash, reactivationVerification.GetProperty("packArchiveSha512").GetString(),
                "release verification carries the first release hash");
            True(records.Any(record => record.RootElement.GetProperty("eventName").GetString() == "install_started" &&
                                       record.RootElement.GetProperty("targetPath").GetString() == secondTarget),
                "second install events rebind target without creating a new log file");
        }
        finally { foreach (var record in records) record.Dispose(); }
        Equal(1, Directory.GetFiles(Path.Combine(installRoot, "logs"), "*.jsonl").Length,
            "successive same-root targets keep one current operation log file");

        var otherRoot = Path.Combine(tempRoot, "operation-log-rebind-other-root");
        var thirdBytes = Bytes("other root synthetic release");
        var thirdPackPath = Path.Combine(tempRoot, "operation-log-rebind-third.mrpack");
        CreatePack(thirdPackPath, "0.7.0", [new TestFile("mods/rebind.jar", thirdBytes)]);
        currentDownload = thirdBytes;
        var otherRootResult = await installer.InstallAsync(thirdPackPath, HashFile(thirdPackPath), otherRoot,
            operationLog: operationLog);
        True(otherRootResult.Success && otherRootResult.LogPath is null && operationLog.CurrentLogPath is null,
            "a context bound to another root becomes unavailable instead of exposing its old log");
        True(!Directory.Exists(Path.Combine(otherRoot, "logs")),
            "a cross-root context does not create a log under the unbound root");
        Pass("one operation log safely rebinds its canonical target for successive releases in the same root");
    }

    private static async Task VerifyUnvalidatedWorldLogBindingAsync(string tempRoot, string sourceManifestPath)
    {
        var safeTempRoot = ValidateSmokeTempRoot(tempRoot);
        var targetRoot = Path.Combine(safeTempRoot, "world-log-unvalidated-target");
        var instance = Path.Combine(targetRoot, "game");
        Directory.CreateDirectory(instance);
        File.Copy(sourceManifestPath, Path.Combine(instance, InstallationManifest.FileName));
        var source = Path.Combine(safeTempRoot, "world-log-unvalidated-source");
        var world = Path.Combine(source, "world");
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "level.dat"), "synthetic world");
        File.WriteAllBytes(Path.Combine(world, "session.lock"), Bytes("synthetic lock"));

        var operationLog = new OperationLog("import", "9.9.9", instance);
        var result = await WorldImportService.ImportAsync(source, instance, operationLog: operationLog);
        Equal(1, result.Imported, "world import can proceed for a validated manifest outside the normal root layout");
        True(!operationLog.IsAvailable && operationLog.CurrentLogPath is null,
            "nonstandard target layout leaves the current diagnostic explicitly unavailable");
        True(!Directory.Exists(Path.Combine(targetRoot, "logs")) && !Directory.Exists(Path.Combine(safeTempRoot, "logs")),
            "nonstandard target layout does not create logs in an unvalidated parent");
        Pass("operation log binding refuses unvalidated world-import parents without changing import behavior");
    }

    private static async Task VerifyWorldImportSafetyAsync(string tempRoot, string instance)
    {
        var safeTempRoot = ValidateSmokeTempRoot(tempRoot);
        var sourceSaves = Path.Combine(safeTempRoot, "source-profile", "saves");
        var targetSaves = Path.Combine(instance, "saves");
        var collision = Path.Combine(sourceSaves, "world");
        var copied = Path.Combine(sourceSaves, "new-world");
        var missingLockWorld = Path.Combine(sourceSaves, "missing-lock-world");
        var blockedLockWorld = Path.Combine(sourceSaves, "blocked-lock-world");
        Directory.CreateDirectory(Path.Combine(collision));
        Directory.CreateDirectory(Path.Combine(copied, "region"));
        Directory.CreateDirectory(missingLockWorld);
        Directory.CreateDirectory(blockedLockWorld);
        File.WriteAllText(Path.Combine(collision, "level.dat"), "do not overwrite");
        File.WriteAllText(Path.Combine(copied, "level.dat"), "copy world");
        File.WriteAllText(Path.Combine(copied, "region", "r.0.0.mca"), "copy region");
        File.WriteAllBytes(Path.Combine(copied, "session.lock"), Bytes("source session lock bytes"));
        File.WriteAllText(Path.Combine(missingLockWorld, "level.dat"), "missing lock world");
        File.WriteAllText(Path.Combine(blockedLockWorld, "level.dat"), "locked world");
        var blockedLockPath = Path.Combine(blockedLockWorld, "session.lock");
        File.WriteAllBytes(blockedLockPath, Bytes("blocked source lock"));
        var missingLockPath = Path.Combine(missingLockWorld, "session.lock");
        var sourceBefore = SnapshotWorldFiles(sourceSaves);
        var javaProbe = CompileMinecraftSessionLockProbe();
        var copyReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var continueCopy = new ManualResetEventSlim();
        var manifest = InstallationManifest.Load(instance);
        var operationLog = new OperationLog("import", "9.9.9", instance, manifest.PackVersion,
            manifest.MinecraftVersion, manifest.FabricLoaderVersion, manifest.PackArchiveSha512);
        WorldImportResult imported;
        using (var blockedSourceLock = new FileStream(blockedLockPath, FileMode.Open, FileAccess.Read, FileShare.None))
        using (WorldImportService.UseCheckpointForTesting(checkpoint =>
               {
                   if (checkpoint != "after-world-file-copy:new-world") return;
                   copyReached.TrySetResult();
                   if (!continueCopy.Wait(TimeSpan.FromSeconds(30)))
                       throw new TimeoutException("Smoke did not release the world copy checkpoint.");
               }))
        {
            var importTask = WorldImportService.ImportAsync(sourceSaves, instance, operationLog: operationLog);
            try
            {
                await copyReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Equal(2, RunMinecraftSessionLockProbe(safeTempRoot, javaProbe, Path.Combine(copied, "session.lock")),
                    "Minecraft WRITE+tryLock probe cannot acquire source lock while import is copying");
            }
            catch
            {
                continueCopy.Set();
                try { await importTask; } catch { }
                throw;
            }
            finally { continueCopy.Set(); }
            imported = await importTask;
        }
        operationLog.Complete("completed");

        Equal(0, RunMinecraftSessionLockProbe(safeTempRoot, javaProbe, Path.Combine(copied, "session.lock")),
            "Minecraft WRITE+tryLock probe acquires source lock after import releases it");
        Equal(1, imported.Imported, "world import copies a world with a protected source lock");
        Equal(1, imported.SkippedExisting, "world import counts an existing target name");
        Equal(1, imported.SkippedMissingLock, "world import counts a missing session.lock");
        Equal(1, imported.SkippedLockedOrUnverified, "world import counts a locked or unverifiable session.lock");
        Equal(3, imported.Skipped, "world import total includes each reported skip reason");
        Equal("keep world", File.ReadAllText(Path.Combine(targetSaves, "world", "level.dat")),
            "existing world is not overwritten");
        Equal("copy region", File.ReadAllText(Path.Combine(targetSaves, "new-world", "region", "r.0.0.mca")),
            "world subdirectories are copied");
        var sourceLockHash = HashFile(Path.Combine(copied, "session.lock"));
        Equal(sourceLockHash, HashFile(Path.Combine(targetSaves, "new-world", "session.lock")),
            "existing source session.lock bytes are copied from the held stream");
        True(!File.Exists(missingLockPath) && !Directory.Exists(Path.Combine(targetSaves, "missing-lock-world")),
            "missing session.lock is not created and the unverified world is skipped");
        True(!Directory.Exists(Path.Combine(targetSaves, "blocked-lock-world")),
            "a world with a locked source session.lock is skipped");
        using (new FileStream(blockedLockPath, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        True(sourceBefore.SequenceEqual(SnapshotWorldFiles(sourceSaves)),
            "world import leaves every source file byte-identical");
        var importLogPath = operationLog.CurrentLogPath
            ?? throw new InvalidOperationException("World import operation log was not bound to its validated root.");
        using (var importRecord = JsonDocument.Parse(File.ReadLines(importLogPath).First()))
            Equal(operationLog.OperationId, importRecord.RootElement.GetProperty("operationId").GetString(),
                "world import stages use their explicit operation id");
        True(File.ReadAllText(importLogPath).Contains("world_import_summary", StringComparison.Ordinal) &&
             File.ReadAllText(importLogPath).Contains("\"installerVersion\":\"9.9.9\"", StringComparison.Ordinal),
            "world import logs its partial summary with the supplied installer version");
        True(!Directory.EnumerateDirectories(targetSaves, ".minepack-import-*").Any(),
            "no import staging directories remain after successful copy");
        Pass("world import holds Minecraft-compatible source protection and reports duplicate, missing-lock, and locked skips");

        var cancelSaves = Path.Combine(safeTempRoot, "cancel-source", "saves");
        var firstWorld = Path.Combine(cancelSaves, "a-before-cancel");
        var canceledWorld = Path.Combine(cancelSaves, "b-cancel");
        Directory.CreateDirectory(firstWorld);
        Directory.CreateDirectory(Path.Combine(canceledWorld, "region"));
        File.WriteAllText(Path.Combine(firstWorld, "level.dat"), "previously copied");
        File.WriteAllBytes(Path.Combine(firstWorld, "session.lock"), Bytes("cancel first lock"));
        File.WriteAllText(Path.Combine(canceledWorld, "level.dat"), "cancel target");
        File.WriteAllText(Path.Combine(canceledWorld, "region", "r.0.0.mca"), "cancel region");
        File.WriteAllBytes(Path.Combine(canceledWorld, "session.lock"), Bytes("cancel second lock"));
        var cancelSnapshot = SnapshotWorldFiles(cancelSaves);
        using var cancellation = new CancellationTokenSource();
        WorldImportCancelledException? cancellationResult = null;
        using (WorldImportService.UseCheckpointForTesting(checkpoint =>
               {
                   if (checkpoint == "after-world-file-copy:b-cancel") cancellation.Cancel();
               }))
        {
            try { await WorldImportService.ImportAsync(cancelSaves, instance, cancellationToken: cancellation.Token); }
            catch (WorldImportCancelledException ex) { cancellationResult = ex; }
        }
        True(cancellationResult is not null, "mid-copy cancellation returns a partial import result");
        Equal(1, cancellationResult!.PartialResult.Imported, "partial result counts the world published before cancellation");
        True(Directory.Exists(Path.Combine(targetSaves, "a-before-cancel")) &&
             !Directory.Exists(Path.Combine(targetSaves, "b-cancel")),
            "mid-copy cancellation keeps completed worlds and does not publish the current staging world");
        True(!Directory.EnumerateDirectories(targetSaves, ".minepack-import-*").Any(),
            "mid-copy cancellation removes only the incomplete staging directory");
        True(cancelSnapshot.SequenceEqual(SnapshotWorldFiles(cancelSaves)),
            "mid-copy cancellation leaves source worlds byte-identical");
        Pass("mid-copy cancellation retains prior imports and reports accurate partial counts");

        var ioSaves = Path.Combine(safeTempRoot, "io-source", "saves");
        var ioFirst = Path.Combine(ioSaves, "a-before-io");
        var ioFailureWorld = Path.Combine(ioSaves, "b-io-failure");
        Directory.CreateDirectory(ioFirst);
        Directory.CreateDirectory(ioFailureWorld);
        File.WriteAllText(Path.Combine(ioFirst, "level.dat"), "before io failure");
        File.WriteAllBytes(Path.Combine(ioFirst, "session.lock"), Bytes("io first lock"));
        File.WriteAllText(Path.Combine(ioFailureWorld, "level.dat"), "copy will fail");
        File.WriteAllBytes(Path.Combine(ioFailureWorld, "session.lock"), Bytes("io second lock"));
        var lockedWorldFile = Path.Combine(ioFailureWorld, "locked.dat");
        File.WriteAllText(lockedWorldFile, "held by another writer");
        var ioSnapshot = SnapshotWorldFiles(ioSaves);
        WorldImportFailureException? ioFailure = null;
        using (new FileStream(lockedWorldFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { await WorldImportService.ImportAsync(ioSaves, instance); }
            catch (WorldImportFailureException ex) { ioFailure = ex; }
        }
        True(ioFailure is not null, "copy I/O failure reports a partial result");
        Equal(1, ioFailure!.PartialResult.Imported, "I/O failure summary counts the world copied before failure");
        True(Directory.Exists(Path.Combine(targetSaves, "a-before-io")) &&
             !Directory.Exists(Path.Combine(targetSaves, "b-io-failure")),
            "I/O failure retains earlier worlds and leaves the current world unpublished");
        True(!Directory.EnumerateDirectories(targetSaves, ".minepack-import-*").Any(),
            "I/O failure removes the incomplete staging directory");
        True(ioSnapshot.SequenceEqual(SnapshotWorldFiles(ioSaves)), "I/O failure leaves source worlds byte-identical");
        Pass("world import I/O failure preserves completed worlds and carries truthful counters");

        var pendingFolder = instance + ".minepack-transaction";
        Directory.CreateDirectory(pendingFolder);
        File.WriteAllText(Path.Combine(pendingFolder, "journal.json"), "{}");
        try
        {
            await WorldImportService.ImportAsync(cancelSaves, instance);
            throw new InvalidOperationException("World import unexpectedly bypassed pending transaction recovery.");
        }
        catch (InstallerException ex) when (ex.Code == "TRANSACTION_RECOVERY_REQUIRED")
        {
            True(!Directory.Exists(Path.Combine(targetSaves, "b-cancel")),
                "pending transaction refuses world import before copying a target world");
        }
        finally { Directory.Delete(pendingFolder, recursive: true); }
        Pass("world import refuses a target with pending transaction recovery");

        var reparseSaves = Path.Combine(safeTempRoot, "reparse-source", "saves");
        var outsideWorld = Path.Combine(safeTempRoot, "reparse-world-target");
        Directory.CreateDirectory(reparseSaves);
        Directory.CreateDirectory(outsideWorld);
        File.WriteAllText(Path.Combine(outsideWorld, "level.dat"), "outside source root");
        var reparseWorld = Path.Combine(reparseSaves, "linked-world");
        Directory.CreateSymbolicLink(reparseWorld, outsideWorld);
        try
        {
            await WorldImportService.ImportAsync(reparseSaves, instance);
            throw new InvalidOperationException("World import unexpectedly followed a source reparse point.");
        }
        catch (WorldImportFailureException ex) when (ex.Code == "PATH_REPARSE_BLOCKED")
        {
            True(!Directory.Exists(Path.Combine(targetSaves, "linked-world")),
                "source reparse rejection happens before publishing a world");
        }
        Pass("world import refuses reparse-point source worlds");
    }

    private static string[] SnapshotWorldFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => new KeyValuePair<string, string>(Path.GetRelativePath(root, path), HashFile(path)))
        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
        .Select(item => item.Key + "=" + item.Value)
        .ToArray();

    private static (string JavaPath, string ClassDirectory) CompileMinecraftSessionLockProbe()
    {
        True(IsJava25Version("javac 25.0.1") && IsJava25Version("openjdk version \"25.0.1\""),
            "Java 25 prerequisite accepts compiler and runtime version banners");
        True(!IsJava25Version("javac 21.0.25") && !IsJava25Version("java version \"250.0.1\""),
            "Java 25 prerequisite rejects a different compiler or runtime major version");
        var jdkBin = FindJava25Bin();

        var classDirectory = Path.Combine(AppContext.BaseDirectory, "world-session-lock-probe");
        Directory.CreateDirectory(classDirectory);
        var source = Path.Combine(classDirectory, "MinecraftSessionLockProbe.java");
        File.WriteAllText(source, """
            import java.io.IOException;
            import java.nio.channels.FileChannel;
            import java.nio.channels.FileLock;
            import java.nio.channels.OverlappingFileLockException;
            import java.nio.file.Path;
            import java.nio.file.StandardOpenOption;

            public final class MinecraftSessionLockProbe {
                public static void main(String[] args) {
                    try (FileChannel channel = FileChannel.open(Path.of(args[0]), StandardOpenOption.WRITE)) {
                        FileLock lock = channel.tryLock();
                        if (lock == null) { System.exit(2); return; }
                        lock.release();
                    } catch (IOException | OverlappingFileLockException ex) {
                        System.exit(2);
                    }
                }
            }
            """);
        var start = new ProcessStartInfo(Path.Combine(jdkBin, "javac.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-d");
        start.ArgumentList.Add(classDirectory);
        start.ArgumentList.Add(source);
        using var compiler = Process.Start(start) ?? throw new InvalidOperationException("Could not start javac for the session.lock fixture.");
        if (!compiler.WaitForExit(30000))
        {
            compiler.Kill(entireProcessTree: true);
            throw new TimeoutException("javac did not finish compiling the session.lock fixture.");
        }
        Equal(0, compiler.ExitCode, "Java 25 session.lock probe compiles");
        return (Path.Combine(jdkBin, "java.exe"), classDirectory);
    }

    private static string FindJava25Bin()
    {
        foreach (var variable in new[] { "MINEPACK_TEST_JAVA_HOME", "JAVA_HOME" })
        {
            var configured = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(configured)) continue;
            var bin = Path.GetFullPath(Path.Combine(configured, "bin"));
            if (HasJava25Tools(bin)) return bin;
            throw new InvalidOperationException($"{variable} must name a JDK with Java 25 java.exe and javac.exe: {configured}");
        }

        var candidates = new List<string>();
        for (var current = new DirectoryInfo(Environment.CurrentDirectory); current is not null; current = current.Parent)
            candidates.Add(Path.Combine(current.FullName, "mods", "_work", "_tools", "jdk-25.0.4.1+1", "bin"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrismLauncher", "java", "java-runtime-epsilon", "bin"));
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (HasJava25Tools(candidate)) return candidate;
        throw new InvalidOperationException("Java 25 smoke prerequisites were not found. Set MINEPACK_TEST_JAVA_HOME to an installed Java 25 JDK.");
    }

    private static bool HasJava25Tools(string bin)
    {
        foreach (var executable in new[] { "java.exe", "javac.exe" })
        {
            var path = Path.Combine(bin, executable);
            if (!File.Exists(path)) return false;
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-version");
            try
            {
                using var process = Process.Start(start);
                if (process is null) return false;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill(entireProcessTree: true);
                    return false;
                }
                if (process.ExitCode != 0 || !IsJava25Version(stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult()))
                    return false;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsJava25Version(string banner) =>
        Regex.IsMatch(banner, @"\b(?:openjdk|java|javac)(?:\s+version)?\s+""?25(?:[.\s""]|$)", RegexOptions.CultureInvariant);

    private static int RunMinecraftSessionLockProbe(string tempRoot, (string JavaPath, string ClassDirectory) probe,
        string lockPath)
    {
        var safeRoot = ValidateSmokeTempRoot(tempRoot);
        var fullLockPath = Path.GetFullPath(lockPath);
        var rootPrefix = Path.EndsInDirectorySeparator(safeRoot) ? safeRoot : safeRoot + Path.DirectorySeparatorChar;
        if (!fullLockPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Java session.lock probe accepts only paths under its generated smoke temp root.");
        SafePath.EnsureNoReparsePoints(safeRoot, fullLockPath);
        var start = new ProcessStartInfo(probe.JavaPath) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-cp");
        start.ArgumentList.Add(probe.ClassDirectory);
        start.ArgumentList.Add("MinecraftSessionLockProbe");
        start.ArgumentList.Add(fullLockPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Java session.lock probe.");
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The Java session.lock probe did not finish.");
        }
        return process.ExitCode;
    }

    private static async Task VerifyVariantSwitchingAsync(string tempRoot)
    {
        var installRoot = Path.Combine(tempRoot, "variant-switch-root");
        var plusBytes = Bytes("vanilla plus managed file");
        var twoPlusBytes = Bytes("vanilla 2 plus managed file");
        var currentDownloadBytes = plusBytes;
        var downloadRequests = 0;
        var handler = new DelegateHandler(_ =>
        {
            downloadRequests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(currentDownloadBytes) };
        });
        var plusPackPath = Path.Combine(tempRoot, "variant-plus.mrpack");
        var twoPlusPackPath = Path.Combine(tempRoot, "variant-two-plus.mrpack");
        CreatePack(plusPackPath, TestPackRelease.PackVersion, [new TestFile("mods/test.jar", plusBytes)]);
        CreatePack(twoPlusPackPath, Vanilla2PlusRelease.PackVersion, [new TestFile("mods/test.jar", twoPlusBytes)]);
        var plusHash = HashFile(plusPackPath);
        var twoPlusHash = HashFile(twoPlusPackPath);

        using var installer = new InstallService(new DownloadEngine(handler));
        var plusInstall = await installer.InstallAsync(plusPackPath, plusHash, installRoot);
        True(plusInstall.Success, "Vanilla Plus fixture installs first");
        var plusInstance = plusInstall.GameDirectory!;
        var plusWorld = Path.Combine(plusInstance, "saves", "plus-world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(plusWorld)!);
        File.WriteAllText(plusWorld, "keep Vanilla Plus world");

        currentDownloadBytes = twoPlusBytes;
        var twoPlusInstall = await installer.InstallAsync(twoPlusPackPath, twoPlusHash, installRoot);
        True(twoPlusInstall.Success, "Vanilla 2 Plus installs as a separate active instance");
        var twoPlusInstance = twoPlusInstall.GameDirectory!;
        var twoPlusWorld = Path.Combine(twoPlusInstance, "saves", "two-plus-world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(twoPlusWorld)!);
        File.WriteAllText(twoPlusWorld, "keep Vanilla 2 Plus world");
        True(!plusInstance.Equals(twoPlusInstance, StringComparison.OrdinalIgnoreCase) && File.Exists(plusWorld),
            "switching variants keeps the previous isolated instance and its world");
        var activeMarkerPath = Path.Combine(installRoot, ".minepack-active.json");

        var corruptMarkerBytes = Bytes("{\"SchemaVersion\":1,\"broken\":true}");
        File.WriteAllBytes(activeMarkerPath, corruptMarkerBytes);
        downloadRequests = 0;
        var refusedRecovery = await installer.InstallAsync(plusPackPath, plusHash, installRoot);
        True(!refusedRecovery.Success && refusedRecovery.Code == "ACTIVE_MARKER_RECOVERY_REQUIRED",
            "corrupt schema-1 marker requires explicit recovery confirmation before Install");
        Equal(0, downloadRequests, "corrupt marker refusal happens before download requests");
        Equal(HashBytes(corruptMarkerBytes), HashFile(activeMarkerPath), "marker refusal preserves corrupt marker bytes");

        var missingServiceRecovery = await installer.InstallAsync(plusPackPath, plusHash, installRoot,
            allowActiveMarkerRecovery: true);
        True(!missingServiceRecovery.Success && missingServiceRecovery.Code == "LAUNCHER_CONFIGURATION_REQUIRED",
            "existing-target marker recovery requires the composed Launcher/profile operation");
        Equal(HashBytes(corruptMarkerBytes), HashFile(activeMarkerPath),
            "missing activation service leaves the original corrupt marker unchanged");

        var recoveryLauncherRoot = Path.Combine(tempRoot, "variant-recovery-launcher");
        var recoveryProfilePath = Path.Combine(recoveryLauncherRoot, "launcher_profiles.json");
        using var recoveryLauncher = CreateSyntheticActivationLauncher(recoveryLauncherRoot, recoveryProfilePath,
            twoPlusInstance, PackArchive.Open(plusPackPath, plusHash).MinecraftVersion, out var recoveryProfileBytes);
        FileStream? recoveryProfileBlocker = null;
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-activation-profile-replace")
                       recoveryProfileBlocker = new FileStream(recoveryProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
               }))
        {
            InstallResult blockedRecovery;
            try
            {
                blockedRecovery = await installer.InstallAsync(plusPackPath, plusHash, installRoot,
                    launcher: recoveryLauncher, allowActiveMarkerRecovery: true);
            }
            finally { recoveryProfileBlocker?.Dispose(); }
            True(!blockedRecovery.Success && blockedRecovery.Code == "LAUNCHER_PROFILE_WRITE_FAILED",
                "profile-write refusal during existing-target marker recovery remains controlled");
        }
        Equal(HashBytes(corruptMarkerBytes), HashFile(activeMarkerPath),
            "existing-target recovery profile failure leaves the original corrupt marker byte-identical");
        Equal(HashBytes(recoveryProfileBytes), HashFile(recoveryProfilePath),
            "existing-target recovery profile failure preserves the original Launcher JSON");

        var recoveredInstall = await installer.InstallAsync(plusPackPath, plusHash, installRoot, launcher: recoveryLauncher,
            allowActiveMarkerRecovery: true);
        True(recoveredInstall.Success, "explicit recovery reactivates a trusted existing selection");
        Equal(plusInstance, installer.GetActiveInstancePath(installRoot), "explicit recovery writes a valid selected marker");
        using (var profileDocument = JsonDocument.Parse(File.ReadAllBytes(recoveryProfilePath)))
            Equal(plusInstance, profileDocument.RootElement.GetProperty("profiles")
                    .GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString(),
                "existing-target marker recovery updates the scoped Launcher profile before activation");
        var markerBackups = Directory.GetFiles(Path.Combine(installRoot, "recovery"), "active-marker-*.json");
        Equal(1, markerBackups.Length, "corrupt active marker is retained in a recovery backup");
        Equal(HashBytes(corruptMarkerBytes), HashFile(markerBackups[0]), "marker recovery backup preserves the original bytes");

        currentDownloadBytes = plusBytes;
        currentDownloadBytes = twoPlusBytes;
        var returnToTwoPlus = await installer.InstallAsync(twoPlusPackPath, twoPlusHash, installRoot);
        True(returnToTwoPlus.Success, "selecting Vanilla 2 Plus reactivates its preserved instance");
        Equal(twoPlusInstance, installer.GetActiveInstancePath(installRoot), "active marker returns to Vanilla 2 Plus");

        var activeMarkerBytes = File.ReadAllBytes(activeMarkerPath);
        var activeMarkerHash = HashBytes(activeMarkerBytes);
        var inactivePlusManaged = Path.Combine(plusInstance, "mods", "test.jar");
        File.WriteAllText(inactivePlusManaged, "corrupted inactive Vanilla Plus file");
        currentDownloadBytes = plusBytes;
        var inactiveRepair = await installer.RepairAsync(plusInstance, plusPackPath, plusHash);
        True(inactiveRepair.Success, "selected inactive instance can be repaired without launcher activation");
        Equal(activeMarkerHash, HashFile(activeMarkerPath), "inactive Repair leaves the other active marker unchanged");
        Equal(HashBytes(plusBytes), HashFile(inactivePlusManaged), "inactive Repair restores its pinned managed file");

        var profileRoot = Path.Combine(tempRoot, "inactive-uninstall-launcher");
        Directory.CreateDirectory(profileRoot);
        var profileFixture = Path.Combine(profileRoot, "launcher_profiles.json");
        var activePackFixture = PackArchive.Open(twoPlusPackPath, twoPlusHash);
        var profileBytes = Bytes(LauncherProfile.BuildFixtureCandidate(
            "{\"settings\":{\"keep\":true},\"profiles\":{\"vanilla\":{\"name\":\"Vanilla\"}}}",
            twoPlusInstance, activePackFixture.MinecraftVersion, activePackFixture.FabricLoaderVersion));
        File.WriteAllBytes(profileFixture, profileBytes);
        using var inactiveUninstallLauncher = new FabricLauncherService(profileRoot, ensureLauncherClosed: static () => { });
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-file-move-0") throw new IOException("inactive uninstall late failure");
                   if (name == "after-rollback-file-0") throw new IOException("inactive uninstall rollback interruption");
               }))
        {
            var interrupted = await installer.UninstallAsync(installRoot, plusInstance, plusPackPath, plusHash,
                inactiveUninstallLauncher);
            True(!interrupted.Success && interrupted.Code == "TRANSACTION_RECOVERY_REQUIRED",
                "inactive Uninstall retains a pending journal after an injected late rollback interruption");
        }
        Equal(activeMarkerHash, HashFile(activeMarkerPath), "inactive Uninstall rollback preserves another version's active marker");
        Equal(HashBytes(profileBytes), HashFile(profileFixture), "inactive Uninstall does not touch unrelated Launcher profile data");
        True(installer.GetPendingOperation(installRoot) is not null, "inactive rollback interruption remains discoverable");
        var recoveredInactive = await installer.RepairAsync(plusInstance, plusPackPath, plusHash,
            launcher: inactiveUninstallLauncher);
        True(recoveredInactive.Success, "next selected-instance Repair recovers the inactive Uninstall journal");
        Equal(activeMarkerHash, HashFile(activeMarkerPath), "pending inactive recovery still leaves the other marker untouched");
        Equal(HashBytes(profileBytes), HashFile(profileFixture), "pending inactive recovery leaves the profile fixture untouched");

        var inactiveUninstall = await installer.UninstallAsync(installRoot, plusInstance, plusPackPath, plusHash,
            inactiveUninstallLauncher);
        True(inactiveUninstall.Success, "trusted inactive instance can be uninstalled explicitly");
        Equal(activeMarkerHash, HashFile(activeMarkerPath), "inactive Uninstall preserves the active marker byte-for-byte");
        True(File.Exists(plusWorld) && File.Exists(twoPlusWorld) && !File.Exists(Path.Combine(plusInstance, InstallationManifest.FileName)),
            "inactive Uninstall preserves both versions' worlds and leaves the removed instance as data residue");

        var currentMarker = File.ReadAllBytes(activeMarkerPath);
        var futureMarker = Bytes("{\"SchemaVersion\":99,\"InstanceDirectory\":\"instances/test\"}");
        File.WriteAllBytes(activeMarkerPath, futureMarker);
        downloadRequests = 0;
        var futureRefusal = await installer.InstallAsync(twoPlusPackPath, twoPlusHash, installRoot,
            allowActiveMarkerRecovery: true);
        True(!futureRefusal.Success && futureRefusal.Code == "ACTIVE_MARKER_UNSUPPORTED",
            "future active marker schemas are never overwritten even after explicit recovery");
        Equal(0, downloadRequests, "future marker refusal happens before download requests");
        Equal(HashBytes(futureMarker), HashFile(activeMarkerPath), "future marker bytes remain untouched");
        File.WriteAllBytes(activeMarkerPath, currentMarker);

        var twoPlusManaged = Path.Combine(twoPlusInstance, "mods", "test.jar");
        File.WriteAllText(twoPlusManaged, "damaged active Vanilla 2 Plus file");
        currentDownloadBytes = twoPlusBytes;
        var repair = await installer.RepairAsync(twoPlusInstance, twoPlusPackPath, twoPlusHash);
        True(repair.Success,
            $"Repair uses the active Vanilla 2 Plus archive (code={repair.Code}, message={repair.Message})");
        Equal(HashBytes(twoPlusBytes), HashFile(twoPlusManaged), "Vanilla 2 Plus Repair restores its pinned file");
        var uninstall = await installer.UninstallAsync(installRoot, twoPlusInstance, twoPlusPackPath, twoPlusHash);
        True(uninstall.Success && installer.GetActiveInstancePath(installRoot) is null,
            "Uninstall uses the active Vanilla 2 Plus archive and clears its marker");
        True(File.Exists(twoPlusWorld) && File.Exists(plusWorld) && Directory.Exists(plusInstance),
            "uninstalling active Vanilla 2 Plus preserves both worlds and the inactive residue directory");

        var freshRecoveryRoot = Path.Combine(tempRoot, "fresh-marker-recovery-root");
        Directory.CreateDirectory(freshRecoveryRoot);
        var emptyMarkerPath = Path.Combine(freshRecoveryRoot, ".minepack-active.json");
        File.WriteAllBytes(emptyMarkerPath, []);
        currentDownloadBytes = plusBytes;
        var freshRecovery = await installer.InstallAsync(plusPackPath, plusHash, freshRecoveryRoot,
            allowActiveMarkerRecovery: true);
        True(freshRecovery.Success, "explicit recovery of an empty marker permits a fresh isolated install");
        var emptyMarkerBackup = Directory.GetFiles(Path.Combine(freshRecoveryRoot, "recovery"), "active-marker-*.json").Single();
        Equal(HashBytes([]), HashFile(emptyMarkerBackup), "fresh-root recovery keeps the empty corrupt marker byte-for-byte");
        Equal(Path.Combine(freshRecoveryRoot, "instances"), Path.GetDirectoryName(freshRecovery.GameDirectory!),
            "fresh marker recovery installs only into the selected root");

        var (pinnedArchive, pinnedHash) = InstalledInstanceCatalog.PinnedArchive(TestPackRelease.PackVersion, AppContext.BaseDirectory);
        var pinnedPack = PackArchive.Open(pinnedArchive, pinnedHash);
        var absentRoot = CreatePinnedReleaseFixture(tempRoot, pinnedPack, out var absentInstance);
        var absentMarkerPath = Path.Combine(absentRoot, ".minepack-active.json");
        File.Delete(absentMarkerPath);
        var absentMarkerUninstall = await installer.UninstallAsync(absentRoot, absentInstance, pinnedArchive, pinnedHash);
        True(absentMarkerUninstall.Success && !File.Exists(absentMarkerPath),
            "inactive Uninstall succeeds without inventing or creating an absent active marker");
        Pass("explicit recovery, inactive repair/uninstall, pending rollback, and variant switching preserve worlds and markers");
    }

    private static async Task VerifyActivationProfileRollbackAsync(string tempRoot)
    {
        var (packPath, packHash) = InstalledInstanceCatalog.PinnedArchive(TestPackRelease.PackVersion, AppContext.BaseDirectory);
        var pack = PackArchive.Open(packPath, packHash);
        var installRoot = CreatePinnedReleaseFixture(tempRoot, pack, out var instance);
        var previousInstance = Path.Combine(installRoot, "instances", "previous-active");
        Directory.CreateDirectory(previousInstance);
        new InstallationManifest
        {
            PackVersion = pack.VersionId,
            MinecraftVersion = pack.MinecraftVersion,
            FabricLoaderVersion = pack.FabricLoaderVersion,
            PackArchiveSha512 = pack.ArchiveSha512,
            Files = []
        }.SaveAtomic(previousInstance);
        var markerPath = Path.Combine(installRoot, ".minepack-active.json");
        var markerBytes = Bytes("{\"SchemaVersion\":1,\"InstanceDirectory\":\"instances/previous-active\"}");
        File.WriteAllBytes(markerPath, markerBytes);

        var launcherRoot = Path.Combine(tempRoot, "activation-launcher-root");
        var profilePath = Path.Combine(launcherRoot, "launcher_profiles.json");
        using var launcher = CreateSyntheticActivationLauncher(launcherRoot, profilePath, previousInstance,
            pack.MinecraftVersion, out var profileBytes);
        var profileHash = HashBytes(profileBytes);
        var markerHash = HashBytes(markerBytes);
        using var installer = new InstallService();

        FileStream? profileBlocker = null;
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-activation-profile-replace")
                       profileBlocker = new FileStream(profilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
               }))
        {
            try
            {
                _ = await installer.ActivateExistingInstanceAsync(installRoot, instance, packPath, packHash, launcher);
                throw new InvalidOperationException("Expected profile write refusal during explicit activation.");
            }
            catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_WRITE_FAILED") { }
            finally { profileBlocker?.Dispose(); }
        }
        Equal(profileHash, HashFile(profilePath), "profile-write failure preserves the original Launcher JSON bytes");
        Equal(markerHash, HashFile(markerPath), "profile-write failure leaves the previous active marker untouched");

        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-active-marker-replace") throw new IOException("synthetic marker write refusal");
               }))
        {
            try
            {
                _ = await installer.ActivateExistingInstanceAsync(installRoot, instance, packPath, packHash, launcher);
                throw new InvalidOperationException("Expected active marker write refusal during explicit activation.");
            }
            catch (InstallerException ex) when (ex.Code == "ACTIVE_MARKER_WRITE_FAILED") { }
        }
        Equal(profileHash, HashFile(profilePath), "marker-write failure after profile update restores the original Launcher JSON");
        Equal(markerHash, HashFile(markerPath), "marker-write failure leaves the original active marker byte-identical");

        using (var cancellation = new CancellationTokenSource())
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-activation-profile-replace") cancellation.Cancel();
               }))
        {
            try
            {
                _ = await installer.ActivateExistingInstanceAsync(installRoot, instance, packPath, packHash, launcher,
                    cancellationToken: cancellation.Token);
                throw new InvalidOperationException("Expected activation cancellation before marker commit.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        Equal(profileHash, HashFile(profilePath), "activation cancellation after profile replacement restores the original Launcher JSON");
        Equal(markerHash, HashFile(markerPath), "activation cancellation before marker commit preserves the original active marker");

        var externalProfile = Bytes("{\"profiles\":{\"external\":{\"name\":\"preserve external edit\"}}}");
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-activation-profile-replace") File.WriteAllBytes(profilePath, externalProfile);
                   if (name == "before-active-marker-replace") throw new IOException("synthetic marker write refusal");
               }))
        {
            try
            {
                _ = await installer.ActivateExistingInstanceAsync(installRoot, instance, packPath, packHash, launcher);
                throw new InvalidOperationException("Expected activation recovery when Launcher JSON changes externally.");
            }
            catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_RECOVERY_REQUIRED")
            {
                True(ex.Message.Contains(".profile-backup", StringComparison.Ordinal),
                    "activation reports the retained original profile backup path");
            }
        }
        Equal(HashBytes(externalProfile), HashFile(profilePath), "activation rollback preserves Launcher JSON changed externally");
        Equal(markerHash, HashFile(markerPath), "external profile change does not switch the active marker");
        var activationBackups = Directory.GetFiles(launcherRoot, ".minepack-activation-*.profile-backup");
        Equal(1, activationBackups.Length, "failed safe rollback retains one bounded original Launcher profile backup");
        Equal(profileHash, HashFile(activationBackups[0]), "retained activation backup contains the original Launcher JSON bytes");

        File.WriteAllBytes(profilePath, profileBytes);
        using var wrongVersionLauncher = new FabricLauncherService(Path.Combine(tempRoot, "wrong-version-launcher"),
            minecraftVersion: "26.3", ensureLauncherClosed: static () => { });
        try
        {
            _ = await installer.ActivateExistingInstanceAsync(installRoot, instance, packPath, packHash, wrongVersionLauncher);
            throw new InvalidOperationException("Expected version mismatch refusal for historical activation target.");
        }
        catch (InstallerException ex) when (ex.Code == "FABRIC_VERSION_MISMATCH") { }
        Equal(markerHash, HashFile(markerPath), "wrong-version Launcher service is refused before marker mutation");
        Equal(profileHash, HashFile(profilePath), "wrong-version Launcher service is refused before profile mutation");

        _ = await installer.ActivateExistingInstanceAsync(installRoot, instance, packPath, packHash, launcher);
        Equal(Path.GetFullPath(instance), installer.GetActiveInstancePath(installRoot),
            "successful composed activation selects the requested trusted instance");
        using (var document = JsonDocument.Parse(File.ReadAllBytes(profilePath)))
        {
            Equal(Path.GetFullPath(instance), document.RootElement.GetProperty("profiles")
                .GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString(),
                "successful composed activation points the Launcher profile at the selected instance");
            Equal("-Xmx8G", document.RootElement.GetProperty("profiles")
                .GetProperty(LauncherProfile.ProfileKey).GetProperty("javaArgs").GetString(),
                "activation applies the fixed heap default to the trusted current release");
        }
        var customProfile = JsonNode.Parse(File.ReadAllBytes(profilePath))!.AsObject();
        customProfile["profiles"]![LauncherProfile.ProfileKey]!["javaArgs"] = "-Xmx4G -XX:+UseG1GC";
        File.WriteAllText(profilePath, customProfile.ToJsonString());
        _ = await installer.ActivateExistingInstanceAsync(installRoot, instance, packPath, packHash, launcher);
        using (var custom = JsonDocument.Parse(File.ReadAllBytes(profilePath)))
            Equal("-Xmx4G -XX:+UseG1GC", custom.RootElement.GetProperty("profiles")
                .GetProperty(LauncherProfile.ProfileKey).GetProperty("javaArgs").GetString(),
                "repeated activation preserves explicit custom JVM arguments without adding another Xmx");
        Equal(1, Directory.GetFiles(launcherRoot, ".minepack-activation-*.profile-backup").Length,
            "successful activation removes its own rollback backup and leaves only the earlier recovery backup");
        Pass("activation profile and marker update share one lease and scoped rollback");
    }

    private static FabricLauncherService CreateSyntheticActivationLauncher(string launcherRoot, string profilePath,
        string previousGameDirectory, string minecraftVersion, out byte[] profileBytes)
    {
        Directory.CreateDirectory(launcherRoot);
        var versionId = $"fabric-loader-{TestPackRelease.FabricLoaderVersion}-{minecraftVersion}";
        var versionsDirectory = Path.Combine(launcherRoot, "versions", versionId);
        Directory.CreateDirectory(versionsDirectory);
        File.WriteAllText(Path.Combine(versionsDirectory, versionId + ".json"),
            $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"{minecraftVersion}\",\"time\":\"fixture\",\"releaseTime\":\"fixture\"}}");
        File.WriteAllBytes(Path.Combine(versionsDirectory, versionId + ".jar"), []);
        var stableProfile = Bytes($"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"{minecraftVersion}\"}}");
        var expectedJarHash = HashBytes([]);
        profileBytes = Bytes(LauncherProfile.BuildFixtureCandidate(
            "{\"settings\":{\"preserve\":true},\"profiles\":{\"vanilla\":{\"name\":\"Vanilla\"}}}",
            previousGameDirectory, minecraftVersion, TestPackRelease.FabricLoaderVersion));
        File.WriteAllBytes(profilePath, profileBytes);
        return new FabricLauncherService(launcherRoot, expectedSha512: HashBytes(stableProfile),
            expectedClientJarSha512: expectedJarHash, expectedClientJarSize: 0, minecraftVersion: minecraftVersion,
            ensureLauncherClosed: static () => { });
    }

    private static async Task VerifyManagedFileTransactionsAsync(string tempRoot)
    {
        var packPath = Path.Combine(tempRoot, "transaction-pack.mrpack");
        var firstBytes = Bytes("transaction managed file one");
        var secondBytes = Bytes("transaction managed file two");
        CreatePack(packPath, "0.2.0", [], [
            new TestOverride("mods/first.jar", firstBytes),
            new TestOverride("mods/second.jar", secondBytes)
        ]);
        var packHash = HashFile(packPath);
        using var installer = new InstallService();

        var rollbackRoot = Path.Combine(tempRoot, "transaction-rollback-root");
        var rollbackInstall = await installer.InstallAsync(packPath, packHash, rollbackRoot);
        True(rollbackInstall.Success, "transaction rollback fixture installs from archive overrides");
        var rollbackInstance = rollbackInstall.GameDirectory!;
        var firstPath = Path.Combine(rollbackInstance, "mods", "first.jar");
        var secondPath = Path.Combine(rollbackInstance, "mods", "second.jar");
        var originalFirst = Bytes("first pre-repair bytes");
        var originalSecond = Bytes("second pre-repair bytes");
        File.WriteAllBytes(firstPath, originalFirst);
        File.WriteAllBytes(secondPath, originalSecond);

        var commitFailureInjected = false;
        var rollbackFailureInjected = false;
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (!commitFailureInjected && name == "after-replacement-move-0-before-journal")
                   {
                       commitFailureInjected = true;
                       throw new IOException("smoke commit interruption");
                   }
                   if (commitFailureInjected && !rollbackFailureInjected && name == "after-rollback-file-1")
                   {
                       rollbackFailureInjected = true;
                       throw new IOException("smoke rollback interruption");
                   }
               }))
        {
            var interrupted = await installer.RepairAsync(rollbackInstance, packPath, packHash);
            True(!interrupted.Success && interrupted.Code == "TRANSACTION_RECOVERY_REQUIRED",
                "failed rollback returns a recoverable transaction result");
        }
        var rollbackTransactionFolder = rollbackInstance + ".minepack-transaction";
        True(Directory.Exists(rollbackTransactionFolder), "failed rollback retains its journal and recovery files");
        Equal(HashBytes(originalFirst), HashFile(firstPath), "failed rollback keeps the original first file");
        Equal(HashBytes(originalSecond), HashFile(secondPath), "failed rollback keeps the original second file");
        var recovered = await installer.RepairAsync(rollbackInstance, packPath, packHash);
        True(recovered.Success, $"next operation rolls back pending transaction and repairs ({recovered.Code})");
        Equal(HashBytes(firstBytes), HashFile(firstPath), "recovered repair has the pinned first-file hash");
        Equal(HashBytes(secondBytes), HashFile(secondPath), "recovered repair has the pinned second-file hash");

        File.WriteAllBytes(firstPath, originalFirst);
        File.WriteAllBytes(secondPath, originalSecond);
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-replacement-move-1-before-journal")
                       throw new IOException("smoke second replacement interruption");
               }))
        {
            var secondMoveFailure = await installer.RepairAsync(rollbackInstance, packPath, packHash);
            True(!secondMoveFailure.Success && secondMoveFailure.Code == "REPAIR_FAILED",
                "failure after the second replacement move is reported after rollback");
        }
        Equal(HashBytes(originalFirst), HashFile(firstPath), "second-move failure restores the first original");
        Equal(HashBytes(originalSecond), HashFile(secondPath), "second-move failure restores the second original");

        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-final-verification-before-journal") throw new IOException("smoke final verification interruption");
               }))
        {
            var finalVerifyFailure = await installer.RepairAsync(rollbackInstance, packPath, packHash);
            True(!finalVerifyFailure.Success && finalVerifyFailure.Code == "REPAIR_FAILED",
                "failure after final verification is reported after rollback");
        }
        Equal(HashBytes(originalFirst), HashFile(firstPath), "final-verification failure restores the first original");
        Equal(HashBytes(originalSecond), HashFile(secondPath), "final-verification failure restores the second original");
        True(!Directory.Exists(rollbackTransactionFolder), "successful rollback removes only its transaction data");

        var crashRoot = Path.Combine(tempRoot, "transaction-crash-root");
        var crashInstall = await installer.InstallAsync(packPath, packHash, crashRoot);
        True(crashInstall.Success, "child-crash fixture installs");
        var crashInstance = crashInstall.GameDirectory!;
        var crashFirst = Path.Combine(crashInstance, "mods", "first.jar");
        var crashOriginal = Bytes("pre-crash corrupted bytes");
        File.WriteAllBytes(crashFirst, crashOriginal);
        using (var child = StartTransactionCrashChild(crashInstance, packPath, packHash))
        {
            True(child.WaitForExit(30000), "transaction child reaches the replacement-move crash point");
            Equal(86, child.ExitCode, "transaction child exits at the injected process-crash point");
        }
        var crashPack = PackArchive.Open(packPath, packHash);
        var pending = ManagedFileTransaction.OpenPending(crashInstance)
            ?? throw new InvalidOperationException("Crashed transaction journal was not retained.");
        var crashManifest = pending.LoadTrustedManifest(crashPack);
        using (var use = InstanceUseGuard.Acquire(crashInstance, crashManifest.Files.Select(file => file.Path)))
            pending.Recover(crashRoot, crashPack, crashManifest, use, null);
        Equal(HashBytes(crashOriginal), HashFile(crashFirst), "restart recovery restores the exact pre-crash original before a new repair");
        var crashRepair = await installer.RepairAsync(crashInstance, packPath, packHash);
        True(crashRepair.Success, "repair succeeds after restart recovery");
        Equal(HashBytes(firstBytes), HashFile(crashFirst), "post-recovery repair restores the pinned file");

        var forgedRoot = Path.Combine(tempRoot, "transaction-forged-completed-root");
        var forgedInstall = await installer.InstallAsync(packPath, packHash, forgedRoot);
        True(forgedInstall.Success, "forged-terminal fixture installs");
        var forgedInstance = forgedInstall.GameDirectory!;
        File.WriteAllBytes(Path.Combine(forgedInstance, "mods", "first.jar"), originalFirst);
        File.WriteAllBytes(Path.Combine(forgedInstance, "mods", "second.jar"), originalSecond);
        var forgedPack = PackArchive.Open(packPath, packHash);
        var forgedManifest = InstallationManifest.Load(forgedInstance);
        var forgedTransaction = ManagedFileTransaction.BeginRepair(forgedRoot, forgedInstance, forgedPack);
        await forgedPack.ExtractOverridesAsync(forgedTransaction.StagingRoot, CancellationToken.None);
        forgedTransaction.AddFile("mods/first.jar", HashBytes(originalFirst), originalFirst.Length, HashBytes(firstBytes));
        forgedTransaction.AddFile("mods/second.jar", HashBytes(originalSecond), originalSecond.Length, HashBytes(secondBytes));
        forgedTransaction.MarkPrepared();
        forgedTransaction.EnsureCommitSpace();
        using (var use = InstanceUseGuard.Acquire(forgedInstance, forgedManifest.Files.Select(file => file.Path)))
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-replacement-move-0-before-journal") throw new IOException("leave a partial commit");
               }))
        {
            try { forgedTransaction.CommitFiles(use); }
            catch (IOException) { }
        }
        var forgedJournalPath = Path.Combine(forgedTransaction.Folder, "journal.json");
        var forgedJournal = JsonNode.Parse(File.ReadAllText(forgedJournalPath))!;
        forgedJournal["Phase"] = "Completed";
        File.WriteAllText(forgedJournalPath, forgedJournal.ToJsonString());
        var forgedPending = ManagedFileTransaction.OpenPending(forgedInstance)!;
        using (var use = InstanceUseGuard.Acquire(forgedInstance, forgedManifest.Files.Select(file => file.Path)))
        {
            try
            {
                forgedPending.Recover(forgedRoot, forgedPack, forgedManifest, use, null);
                throw new InvalidOperationException("A forged Completed phase was accepted for a partial repair.");
            }
            catch (InstallerException ex) when (ex.Code == "TRANSACTION_RECOVERY_REQUIRED") { }
        }
        var forgedBackup = Path.Combine(forgedTransaction.Folder, "backup", "files", "0000.bin");
        Equal(HashBytes(originalFirst), HashFile(forgedBackup), "forged Completed phase cannot delete the moved original backup");
        Equal(HashBytes(originalSecond), HashFile(Path.Combine(forgedInstance, "mods", "second.jar")),
            "forged Completed phase leaves the untouched second original intact");

        var sentinelRoot = Path.Combine(tempRoot, "transaction-sentinel-root");
        var sentinelInstall = await installer.InstallAsync(packPath, packHash, sentinelRoot);
        True(sentinelInstall.Success, "finished-folder fixture installs");
        var sentinelInstance = sentinelInstall.GameDirectory!;
        var sentinelTarget = Path.Combine(sentinelInstance, "mods", "first.jar");
        File.WriteAllBytes(sentinelTarget, originalFirst);
        var sentinelPack = PackArchive.Open(packPath, packHash);
        var sentinelManifest = InstallationManifest.Load(sentinelInstance);
        var sentinelTransaction = ManagedFileTransaction.BeginRepair(sentinelRoot, sentinelInstance, sentinelPack);
        await sentinelPack.ExtractOverridesAsync(sentinelTransaction.StagingRoot, CancellationToken.None);
        sentinelTransaction.AddFile("mods/first.jar", HashBytes(originalFirst), originalFirst.Length, HashBytes(firstBytes));
        sentinelTransaction.MarkPrepared();
        sentinelTransaction.EnsureCommitSpace();
        using (var use = InstanceUseGuard.Acquire(sentinelInstance, sentinelManifest.Files.Select(file => file.Path)))
        {
            sentinelTransaction.CommitFiles(use);
            sentinelTransaction.MarkCompleted();
            var sentinel = Path.Combine(sentinelTransaction.Folder, "backup", "unexpected-user-data.txt");
            File.WriteAllText(sentinel, "preserve unrecognized transaction data");
            try
            {
                sentinelTransaction.Recover(sentinelRoot, sentinelPack, sentinelManifest, use, null);
                throw new InvalidOperationException("Transaction cleanup deleted an unrecognized sentinel.");
            }
            catch (InstallerException ex) when (ex.Code == "TRANSACTION_RECOVERY_REQUIRED") { }
            True(File.Exists(sentinel), "completed transaction refuses cleanup and preserves an unknown sentinel");
            True(File.Exists(Path.Combine(sentinelTransaction.Folder, "backup", "files", "0000.bin")),
                "completed transaction preserves originals until its cleanup inventory is trusted");
        }

        var uninstallPackPath = Path.Combine(AppContext.BaseDirectory,
            TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var uninstallPack = PackArchive.Open(uninstallPackPath, TestPackRelease.ArtifactSha512);
        var uninstallRoot = CreatePinnedReleaseFixture(tempRoot, uninstallPack, out var uninstallInstance);
        var uninstallManagedEntry = uninstallPack.Files.First();
        var uninstallManaged = SafePath.Resolve(uninstallInstance, uninstallManagedEntry.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(uninstallManaged)!);
        File.WriteAllBytes(uninstallManaged, Bytes("pinned managed file before uninstall rollback"));
        var uninstallManagedBefore = HashFile(uninstallManaged);
        var uninstallWorld = Path.Combine(uninstallInstance, "saves", "kept", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(uninstallWorld)!);
        File.WriteAllText(uninstallWorld, "preserve world through uninstall rollback");
        var launcherRoot = Path.Combine(tempRoot, "transaction-uninstall-launcher");
        Directory.CreateDirectory(launcherRoot);
        var profilePath = Path.Combine(launcherRoot, "launcher_profiles.json");
        var profile = JsonNode.Parse(LauncherProfile.BuildFixtureCandidate("{\"profiles\":{}}", uninstallInstance,
            uninstallPack.MinecraftVersion, uninstallPack.FabricLoaderVersion))!.AsObject();
        profile["profiles"]!.AsObject()[LauncherProfile.ProfileKey]!.AsObject().Remove("minepackInstallerId");
        File.WriteAllText(profilePath, profile.ToJsonString());
        var profileBefore = HashFile(profilePath);
        var markerBefore = HashFile(Path.Combine(uninstallRoot, ".minepack-active.json"));
        var manifestBefore = HashFile(Path.Combine(uninstallInstance, InstallationManifest.FileName));
        FileStream? blockerStream = null;
        using (var launcher = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { }))
        using (var blocker = ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-manifest-remove")
                   {
                       var manifestPath = Path.Combine(uninstallInstance, InstallationManifest.FileName);
                       blockerStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                   }
               }))
        {
            var failedUninstall = await installer.UninstallAsync(uninstallRoot, uninstallInstance,
                uninstallPackPath, uninstallPack.ArchiveSha512, launcher);
            blockerStream?.Dispose();
            True(!failedUninstall.Success && failedUninstall.Code == "UNINSTALL_FAILED",
                $"manifest finalization failure reports uninstall failure after rollback (success={failedUninstall.Success}, code={failedUninstall.Code})");
        }
        Equal(uninstallManagedBefore, HashFile(uninstallManaged), "uninstall rollback restores managed file bytes");
        Equal(manifestBefore, HashFile(Path.Combine(uninstallInstance, InstallationManifest.FileName)),
            "uninstall rollback restores the manifest before returning");
        Equal(markerBefore, HashFile(Path.Combine(uninstallRoot, ".minepack-active.json")),
            "uninstall rollback restores its active marker");
        Equal(profileBefore, HashFile(profilePath), "uninstall rollback restores the markerless Launcher profile bytes");
        Equal("preserve world through uninstall rollback", File.ReadAllText(uninstallWorld),
            "uninstall rollback preserves the world");

        using (var launcher = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { }))
        {
            var completedMarkerlessUninstall = await installer.UninstallAsync(uninstallRoot, uninstallInstance,
                uninstallPackPath, uninstallPack.ArchiveSha512, launcher);
            True(completedMarkerlessUninstall.Success,
                "markerless uninstall finalizes against the trusted manifest snapshot");
        }
        True(!File.Exists(Path.Combine(uninstallInstance, InstallationManifest.FileName)) &&
             !File.Exists(Path.Combine(uninstallRoot, ".minepack-active.json")),
            "completed markerless uninstall removes only its manifest and active marker");
        using (var profileDocument = JsonDocument.Parse(File.ReadAllText(profilePath)))
            True(!profileDocument.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "completed markerless uninstall removes its owned Launcher profile");
        True(File.Exists(uninstallWorld), "completed uninstall preserves worlds");

        await VerifyCreateOnlyRollbackAsync(tempRoot);
        await VerifyProfileRemovalFailureCasesAsync(tempRoot, uninstallPackPath, uninstallPack, uninstallManagedEntry, installer);

        var orphanRoot = Path.Combine(tempRoot, "transaction-orphan-root");
        var orphanFolder = Path.Combine(orphanRoot, "instances", "orphan.minepack-transaction");
        Directory.CreateDirectory(orphanFolder);
        try
        {
            ManagedFileTransaction.EnsureNoPendingUnderRoot(orphanRoot);
            throw new InvalidOperationException("A transaction sibling without its instance directory was ignored.");
        }
        catch (InstallerException ex) when (ex.Code == "TRANSACTION_RECOVERY_REQUIRED") { }
        True(Directory.Exists(orphanFolder), "root discovery refuses an orphaned transaction folder without deleting it");
        await VerifyRepairPreparationAtomicityAsync(tempRoot);
        Pass("repair transaction restores on late failure and process crash, and refuses unproven cleanup");
    }

    private static async Task VerifyRepairPreparationAtomicityAsync(string tempRoot)
    {
        var firstUrl = "https://cdn.modrinth.com/data/fixture/version/first.jar";
        var lastUrl = "https://cdn.modrinth.com/data/fixture/version/last.jar";
        var firstBytes = Bytes("preparation first indexed file");
        var lastBytes = Bytes("preparation last indexed file");
        var packPath = Path.Combine(tempRoot, "transaction-prepare-pack.mrpack");
        CreatePack(packPath, "0.3.0", [
            new TestFile("mods/a.jar", firstBytes, Url: firstUrl),
            new TestFile("mods/z.jar", lastBytes, Url: lastUrl)
        ]);
        var packHash = HashFile(packPath);
        var failLast = false;
        using var installer = new InstallService(new DownloadEngine(new DelegateHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == lastUrl && failLast)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var bytes = request.RequestUri?.AbsoluteUri == firstUrl ? firstBytes : lastBytes;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        })));
        var installRoot = Path.Combine(tempRoot, "transaction-prepare-root");
        var installed = await installer.InstallAsync(packPath, packHash, installRoot);
        True(installed.Success, "prepare-failure fixture installs both indexed files");
        var instance = installed.GameDirectory!;
        var firstPath = Path.Combine(instance, "mods", "a.jar");
        var lastPath = Path.Combine(instance, "mods", "z.jar");
        var firstOriginal = Bytes("corrupt first before prepare");
        var lastOriginal = Bytes("corrupt last before prepare");
        File.WriteAllBytes(firstPath, firstOriginal);
        File.WriteAllBytes(lastPath, lastOriginal);
        var manifestPath = Path.Combine(instance, InstallationManifest.FileName);
        var markerPath = Path.Combine(installRoot, ".minepack-active.json");
        var launcherRoot = Path.Combine(tempRoot, "transaction-prepare-launcher");
        Directory.CreateDirectory(launcherRoot);
        var profilePath = Path.Combine(launcherRoot, "launcher_profiles.json");
        File.WriteAllText(profilePath, "{\"profiles\":{\"fixture\":{\"name\":\"Unrelated\"}}}");
        using var launcher = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { });

        var before = Snapshot();
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            var result = await installer.RepairAsync(instance, packPath, packHash,
                cancellationToken: canceled.Token, launcher: launcher);
            True(!result.Success && result.Code == "CANCELLED", "cancellation during preparation is reported");
        }
        Equal(before, Snapshot(), "cancelled repair leaves instance, manifest, active marker, and Launcher profile byte-identical");

        using (var canceledDuringPrepare = new CancellationTokenSource())
        {
            var firstReplacementPrepared = false;
            var progress = new DelegateProgress<InstallProgress>(item =>
            {
                if (item.Stage != "prepare" || item.CompletedFiles != 1) return;
                firstReplacementPrepared = true;
                canceledDuringPrepare.Cancel();
            });
            var result = await installer.RepairAsync(instance, packPath, packHash, progress,
                canceledDuringPrepare.Token, launcher);
            True(firstReplacementPrepared && !result.Success && result.Code == "CANCELLED",
                "cancellation after the first prepared replacement stops before the next one");
        }
        Equal(before, Snapshot(), "mid-preparation cancellation leaves instance, profile, marker, and manifest unchanged");
        True(!Directory.Exists(instance + ".minepack-transaction"),
            "mid-preparation cancellation removes only its empty transaction staging");

        failLast = true;
        var failed = await installer.RepairAsync(instance, packPath, packHash, launcher: launcher);
        True(!failed.Success && failed.Code == "DOWNLOAD_HTTP", "last replacement download failure aborts preparation");
        Equal(before, Snapshot(), "last download failure leaves instance, manifest, active marker, and Launcher profile byte-identical");

        string Snapshot() => string.Join('|', new[] { firstPath, lastPath, manifestPath, markerPath, profilePath }
            .Select(HashFile));
    }

    private static async Task VerifyCreateOnlyRollbackAsync(string tempRoot)
    {
        var packPath = Path.Combine(AppContext.BaseDirectory,
            TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var pack = PackArchive.Open(packPath, TestPackRelease.ArtifactSha512);
        var root = CreateReleaseFixture(tempRoot, pack, legacy: false, out var instance);
        var manifest = InstallationManifest.Load(instance);
        var staging = Path.Combine(tempRoot, "transaction-create-only-stage");
        Directory.CreateDirectory(staging);
        await pack.ExtractOverridesAsync(staging, CancellationToken.None);
        var defaults = InitialConfiguration.Create(pack, staging);
        InitialConfiguration.WriteToRoot(staging, defaults);
        var createdDefault = defaults.First();
        var target = SafePath.Resolve(instance, createdDefault.Path);
        var world = Path.Combine(instance, "saves", "untouched", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(world)!);
        File.WriteAllText(world, "preserve world while rolling back a created default");
        var manifestHash = HashFile(Path.Combine(instance, InstallationManifest.FileName));
        var markerHash = HashFile(Path.Combine(root, ".minepack-active.json"));

        var transaction = ManagedFileTransaction.BeginRepair(root, instance, pack);
        InitialConfiguration.WriteToRoot(transaction.StagingRoot, defaults);
        var staged = SafePath.Resolve(transaction.StagingRoot, createdDefault.Path);
        transaction.AddFile(createdDefault.Path, null, 0, HashFile(staged), createOnly: true);
        transaction.MarkPrepared();
        transaction.EnsureCommitSpace();
        var protectedPaths = manifest.Files.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path))
            .Select(file => file.Path).Concat(defaults.Select(file => file.Path));
        using (var use = InstanceUseGuard.Acquire(instance, protectedPaths))
        using (ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "after-replacement-move-0-before-journal")
                       throw new IOException("smoke create-only interruption");
               }))
        {
            try { transaction.CommitFiles(use); }
            catch (IOException) { }
            transaction.Rollback(root, use, null, pack, manifest);
        }

        True(!File.Exists(target), "repair rollback removes only its newly created default");
        Equal(manifestHash, HashFile(Path.Combine(instance, InstallationManifest.FileName)),
            "create-only rollback leaves the validated manifest unchanged");
        Equal(markerHash, HashFile(Path.Combine(root, ".minepack-active.json")),
            "create-only rollback leaves the active marker unchanged");
        Equal("preserve world while rolling back a created default", File.ReadAllText(world),
            "create-only rollback preserves worlds");
        True(!Directory.Exists(transaction.Folder), "successful create-only rollback cleans its transaction folder");
        Pass("create-only default rollback removes only the file created by the failed repair");
    }

    private static async Task VerifyProfileRemovalFailureCasesAsync(string tempRoot, string packPath,
        PackArchive pack, PackFile managedEntry, InstallService installer)
    {
        var managedBytes = Bytes("transaction profile receipt managed file");

        var prepareRoot = CreatePinnedReleaseFixture(tempRoot, pack, out var prepareInstance);
        var prepareManaged = SafePath.Resolve(prepareInstance, managedEntry.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(prepareManaged)!);
        File.WriteAllBytes(prepareManaged, managedBytes);
        var prepareLauncherRoot = Path.Combine(tempRoot, "transaction-profile-prepare-launcher");
        Directory.CreateDirectory(prepareLauncherRoot);
        var prepareProfilePath = Path.Combine(prepareLauncherRoot, "launcher_profiles.json");
        WriteMarkerlessFixtureProfile(prepareProfilePath, prepareInstance, pack);
        var prepareProfileHash = HashFile(prepareProfilePath);
        var prepareManifestHash = HashFile(Path.Combine(prepareInstance, InstallationManifest.FileName));
        var prepareMarkerHash = HashFile(Path.Combine(prepareRoot, ".minepack-active.json"));
        using (var launcher = new FabricLauncherService(prepareLauncherRoot, ensureLauncherClosed: static () => { }))
        using (var checkpoints = ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-profile-backup") throw new IOException("smoke profile backup refusal");
               }))
        {
            var result = await installer.UninstallAsync(prepareRoot, prepareInstance, packPath, pack.ArchiveSha512, launcher);
            True(!result.Success && result.Code == "UNINSTALL_FAILED",
                "profile receipt preparation failure rolls back before managed-file commit");
        }
        Equal(HashBytes(managedBytes), HashFile(prepareManaged), "profile preparation failure keeps managed files unchanged");
        Equal(prepareProfileHash, HashFile(prepareProfilePath), "profile preparation failure keeps Launcher JSON unchanged");
        Equal(prepareManifestHash, HashFile(Path.Combine(prepareInstance, InstallationManifest.FileName)),
            "profile preparation failure keeps the manifest unchanged");
        Equal(prepareMarkerHash, HashFile(Path.Combine(prepareRoot, ".minepack-active.json")),
            "profile preparation failure keeps the active marker unchanged");
        True(!Directory.Exists(prepareInstance + ".minepack-transaction"),
            "preparation rollback accepts its intentionally incomplete removal entry set");
        using (var launcher = new FabricLauncherService(prepareLauncherRoot, ensureLauncherClosed: static () => { }))
        {
            var retry = await installer.UninstallAsync(prepareRoot, prepareInstance, packPath, pack.ArchiveSha512, launcher);
            True(retry.Success, "uninstall can retry after profile preparation refusal is rolled back");
        }

        var writeRoot = CreatePinnedReleaseFixture(tempRoot, pack, out var writeInstance);
        var writeManaged = SafePath.Resolve(writeInstance, managedEntry.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(writeManaged)!);
        File.WriteAllBytes(writeManaged, managedBytes);
        var writeLauncherRoot = Path.Combine(tempRoot, "transaction-profile-write-launcher");
        Directory.CreateDirectory(writeLauncherRoot);
        var writeProfilePath = Path.Combine(writeLauncherRoot, "launcher_profiles.json");
        WriteMarkerlessFixtureProfile(writeProfilePath, writeInstance, pack);
        var writeProfileHash = HashFile(writeProfilePath);
        var writeManifestHash = HashFile(Path.Combine(writeInstance, InstallationManifest.FileName));
        var writeMarkerHash = HashFile(Path.Combine(writeRoot, ".minepack-active.json"));
        FileStream? profileBlocker = null;
        using (var launcher = new FabricLauncherService(writeLauncherRoot, ensureLauncherClosed: static () => { }))
        using (var checkpoints = ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name == "before-profile-replace")
                       profileBlocker = new FileStream(writeProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
               }))
        {
            var result = await installer.UninstallAsync(writeRoot, writeInstance, packPath, pack.ArchiveSha512, launcher);
            profileBlocker?.Dispose();
            True(!result.Success && result.Code == "UNINSTALL_FAILED",
                "profile replacement sharing denial is reported after file rollback");
        }
        Equal(HashBytes(managedBytes), HashFile(writeManaged), "profile-write refusal restores quarantined managed files");
        Equal(writeProfileHash, HashFile(writeProfilePath), "profile-write refusal leaves Launcher JSON byte-identical");
        Equal(writeManifestHash, HashFile(Path.Combine(writeInstance, InstallationManifest.FileName)),
            "profile-write refusal preserves the manifest");
        Equal(writeMarkerHash, HashFile(Path.Combine(writeRoot, ".minepack-active.json")),
            "profile-write refusal preserves the active marker");
        True(!Directory.Exists(writeInstance + ".minepack-transaction"),
            "profile-write refusal completes its rollback without pending recovery");

        var externalRoot = CreatePinnedReleaseFixture(tempRoot, pack, out var externalInstance);
        var externalManaged = SafePath.Resolve(externalInstance, managedEntry.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(externalManaged)!);
        File.WriteAllBytes(externalManaged, managedBytes);
        var externalLauncherRoot = Path.Combine(tempRoot, "transaction-profile-external-launcher");
        Directory.CreateDirectory(externalLauncherRoot);
        var externalProfilePath = Path.Combine(externalLauncherRoot, "launcher_profiles.json");
        WriteMarkerlessFixtureProfile(externalProfilePath, externalInstance, pack);
        var externalManifestHash = HashFile(Path.Combine(externalInstance, InstallationManifest.FileName));
        var externalMarkerHash = HashFile(Path.Combine(externalRoot, ".minepack-active.json"));
        string? externalProfile = null;
        using (var launcher = new FabricLauncherService(externalLauncherRoot, ensureLauncherClosed: static () => { }))
        using (var checkpoints = ManagedFileTransaction.UseCheckpointsForTesting(name =>
               {
                   if (name != "before-profile-read" || externalProfile is not null) return;
                   var json = JsonNode.Parse(File.ReadAllText(externalProfilePath))!.AsObject();
                   json["externalChange"] = "preserve this update";
                   externalProfile = json.ToJsonString();
                   File.WriteAllText(externalProfilePath, externalProfile);
               }))
        {
            var result = await installer.UninstallAsync(externalRoot, externalInstance, packPath, pack.ArchiveSha512, launcher);
            True(!result.Success && result.Code == "TRANSACTION_RECOVERY_REQUIRED",
                "external Launcher JSON change is refused with a retained recovery transaction");
        }
        Equal(externalProfile!, File.ReadAllText(externalProfilePath),
            "rollback never overwrites Launcher JSON changed after the receipt was prepared");
        Equal(HashBytes(managedBytes), HashFile(externalManaged),
            "external profile change still restores quarantined managed files");
        Equal(externalManifestHash, HashFile(Path.Combine(externalInstance, InstallationManifest.FileName)),
            "external profile change restores the manifest");
        Equal(externalMarkerHash, HashFile(Path.Combine(externalRoot, ".minepack-active.json")),
            "external profile change restores the active marker");
        True(Directory.Exists(externalInstance + ".minepack-transaction"),
            "external profile change retains the receipt and recovery journal");
        using (var launcher = new FabricLauncherService(externalLauncherRoot, ensureLauncherClosed: static () => { }))
        {
            var refusedRetry = await installer.UninstallAsync(externalRoot, externalInstance, packPath, pack.ArchiveSha512, launcher);
            True(!refusedRetry.Success && refusedRetry.Code == "TRANSACTION_RECOVERY_REQUIRED",
                "a later mutation refuses to proceed over the unresolved external profile change");
        }
        Equal(externalProfile!, File.ReadAllText(externalProfilePath),
            "pending recovery keeps the externally changed profile untouched on retry");
        Pass("profile receipt rollback preserves external JSON changes and fails closed");

        static void WriteMarkerlessFixtureProfile(string path, string instance, PackArchive archive)
        {
            var profile = JsonNode.Parse(LauncherProfile.BuildFixtureCandidate("{\"profiles\":{}}", instance,
                archive.MinecraftVersion, archive.FabricLoaderVersion))!.AsObject();
            profile["profiles"]!.AsObject()[LauncherProfile.ProfileKey]!.AsObject().Remove("minepackInstallerId");
            File.WriteAllText(path, profile.ToJsonString());
        }
    }

    private static async Task VerifyArchiveMutationRejectedAsync(string tempRoot)
    {
        var path = Path.Combine(tempRoot, "mutable-pack.mrpack");
        CreatePack(path, "0.1.0", [], [new TestOverride("config/test.txt", Bytes("pinned override"))]);
        var opened = PackArchive.Open(path);
        File.AppendAllText(path, "modified after validation");
        var staging = Path.Combine(tempRoot, "mutation-staging");
        try
        {
            _ = await opened.ExtractOverridesAsync(staging, CancellationToken.None);
            throw new InvalidOperationException("Expected a modified archive to be rejected before extracting overrides.");
        }
        catch (InstallerException ex) when (ex.Code == "PACK_HASH_MISMATCH") { }
        True(!Directory.Exists(staging), "archive mutation is rejected before creating staging output");
        Pass("archive is rehashed before overrides are extracted");
    }

    private static InstallService CachedInstaller(string packPath, string hash, string? cachedRoot)
    {
        if (cachedRoot is null) return new InstallService();
        var sourceFiles = PackArchive.Open(packPath, hash).Files
            .SelectMany(file => file.Downloads.Select(url => (Url: url.AbsoluteUri, file.Path)))
            .ToDictionary(item => item.Url, item => item.Path, StringComparer.Ordinal);
        return new InstallService(new DownloadEngine(new DelegateHandler(request =>
        {
            if (request.RequestUri is null || !sourceFiles.TryGetValue(request.RequestUri.AbsoluteUri, out var relative))
                throw new InvalidOperationException("Unexpected pinned download URL.");
            var path = Path.Combine(cachedRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var content = new StreamContent(File.OpenRead(path));
            content.Headers.ContentLength = new FileInfo(path).Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        })));
    }

    private static async Task VerifyVersionedInstallLifecycleAsync()
    {
        var sourceRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MinePack", "instances", "MinePack-26.2-VanillaPlus-0.18.3");
        if (!Directory.Exists(sourceRoot))
        {
            Console.WriteLine("NOT RUN: the former optimized Vanilla Plus managed-file cache is unavailable for the versioned install lifecycle fixture.");
            return;
        }

        var oldPackPath = Path.Combine(AppContext.BaseDirectory, "releases", "test-pack", TestPackRelease.FormerOptimizedArtifactFileName);
        var oldPack = PackArchive.Open(oldPackPath, TestPackRelease.FormerOptimizedArtifactSha512);
        SafePath.EnsureNoReparsePoints(sourceRoot, sourceRoot);
        var sourceManifest = InstallationManifest.Load(sourceRoot);
        True(sourceManifest.PackVersion == oldPack.VersionId &&
             sourceManifest.PackArchiveSha512.Equals(oldPack.ArchiveSha512, StringComparison.OrdinalIgnoreCase) &&
             sourceManifest.MinecraftVersion == oldPack.MinecraftVersion &&
             sourceManifest.FabricLoaderVersion == oldPack.FabricLoaderVersion,
            "read-only lifecycle download source is the exact former optimized release");
        foreach (var file in oldPack.Files)
        {
            var path = SafePath.Resolve(sourceRoot, file.Path);
            SafePath.EnsureNoReparsePoints(sourceRoot, path);
            if (!File.Exists(path)) throw new InvalidOperationException($"Lifecycle download source is missing managed file {file.Path}.");
            True(file.Sha512.Equals(HashFile(path), StringComparison.OrdinalIgnoreCase),
                $"read-only lifecycle source hash {file.Path}");
        }

        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "options-lifecycle-" + Guid.NewGuid().ToString("N"));
        SafePath.EnsureNoReparsePoints(AppContext.BaseDirectory, fixtureRoot);
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            var installRoot = Path.Combine(fixtureRoot, "install-root");
            using var oldInstaller = CachedInstaller(oldPackPath, oldPack.ArchiveSha512, sourceRoot);
            var oldInstall = await oldInstaller.InstallAsync(oldPackPath, oldPack.ArchiveSha512, installRoot);
            True(oldInstall.Success, $"former optimized release installs in the isolated lifecycle fixture ({oldInstall.Code})");
            var oldInstance = oldInstall.GameDirectory ?? throw new InvalidOperationException("Former optimized install did not return its isolated instance.");
            Equal("MinePack-26.2-VanillaPlus-0.18.3", Path.GetFileName(oldInstance),
                "lifecycle fixture starts with the existing 0.18.3 instance");

            var oldOptionsPath = Path.Combine(oldInstance, "options.txt");
            const string syntheticUserOptions = "version:4903\ngraphicsPreset:\"fancy\"\nrenderDistance:16\nsimulationDistance:12\nentityDistanceScaling:1.0\n";
            File.WriteAllText(oldOptionsPath, syntheticUserOptions);
            var oldOptionsHash = HashFile(oldOptionsPath);
            var oldWorldPath = Path.Combine(oldInstance, "saves", "lifecycle-fixture-world", "level.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(oldWorldPath)!);
            File.WriteAllText(oldWorldPath, "synthetic user world bytes for preservation check");
            var oldWorldHash = HashFile(oldWorldPath);

            var newPackPath = Path.Combine(AppContext.BaseDirectory,
                TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
            using var newInstaller = CachedInstaller(newPackPath, TestPackRelease.ArtifactSha512, sourceRoot);
            var newInstall = await newInstaller.InstallAsync(newPackPath, TestPackRelease.ArtifactSha512, installRoot);
            True(newInstall.Success, $"current release installs beside the former optimized instance ({newInstall.Code})");
            var newInstance = newInstall.GameDirectory ?? throw new InvalidOperationException("Current install did not return its isolated instance.");
            True(!newInstance.Equals(oldInstance, StringComparison.OrdinalIgnoreCase),
                "new release does not route through the existing 0.18.3 instance");
            Equal("MinePack-26.2-VanillaPlus-0.18.4", Path.GetFileName(newInstance),
                "current release gets its own versioned instance name");
            AssertOptimizedGraphicsOptions(File.ReadAllText(Path.Combine(newInstance, "options.txt")),
                "InstallService-created 0.18.4 options");
            CreateOptionsStartupProbeInput(newPackPath, newInstance);
            Equal(oldOptionsHash, HashFile(oldOptionsPath), "existing 0.18.3 options remain byte-identical after current install");
            Equal(oldWorldHash, HashFile(oldWorldPath), "existing 0.18.3 world remains byte-identical after current install");
            Pass("InstallService creates a separate optimized instance and preserves the previous instance settings and world");
        }
        finally
        {
            DeleteBuildFixtureTree(fixtureRoot);
        }
    }

    private static void CreateOptionsStartupProbeInput(string packPath, string installedInstance)
    {
        const string marker = "MinePack plan032 test-only startup probe input v1";
        var root = Path.Combine(AppContext.BaseDirectory, "options-startup-probe-input");
        if (Directory.Exists(root))
        {
            var markerPath = Path.Combine(root, ".minepack-options-startup-probe");
            SafePath.EnsureNoReparsePoints(root, markerPath);
            if (!File.Exists(markerPath) || File.ReadAllText(markerPath) != marker)
                throw new InvalidOperationException("Refusing to replace an unrecognized startup probe input directory.");
            EnsureNoReparseDescendants(root);
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".minepack-options-startup-probe"), marker);
        var gameDirectory = Path.Combine(root, "game");
        Directory.CreateDirectory(gameDirectory);
        var pack = PackArchive.Open(packPath, TestPackRelease.ArtifactSha512);
        foreach (var file in pack.Files)
            CopyProbeInputFile(installedInstance, gameDirectory, file.Path, file.Sha512);
        foreach (var file in pack.Overrides)
            CopyProbeInputFile(installedInstance, gameDirectory, file.Path, file.Sha512);

        var options = Path.Combine(installedInstance, "options.txt");
        if (!File.Exists(options)) throw new InvalidOperationException("The production install omitted options.txt for the startup probe.");
        File.Copy(options, Path.Combine(gameDirectory, "options.txt"));
        File.Copy(options, Path.Combine(root, "production-options.txt"));
        File.WriteAllText(Path.Combine(root, "input.json"), JsonSerializer.Serialize(new
        {
            packVersion = pack.VersionId,
            packSha512 = pack.ArchiveSha512,
            downloadedFiles = pack.Files.Count,
            overrides = pack.Overrides.Count
        }));
    }

    private static void CopyProbeInputFile(string sourceRoot, string targetRoot, string relativePath, string expectedSha512)
    {
        var source = SafePath.Resolve(sourceRoot, relativePath);
        SafePath.EnsureNoReparsePoints(sourceRoot, source);
        if (!File.Exists(source) || !expectedSha512.Equals(HashFile(source), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"A startup probe input is missing or unverified: {relativePath}.");
        var target = SafePath.Resolve(targetRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        SafePath.EnsureNoReparsePoints(targetRoot, target);
        File.Copy(source, target);
        if (!expectedSha512.Equals(HashFile(target), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"A copied startup probe input failed SHA-512 verification: {relativePath}.");
    }

    private static void EnsureNoReparseDescendants(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing startup probe input cleanup across a reparse point.");
            if ((attributes & FileAttributes.Directory) != 0) EnsureNoReparseDescendants(path);
        }
    }

    private static void DeleteBuildFixtureTree(string path)
    {
        var resolved = Path.GetFullPath(path);
        var buildRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var prefix = Path.EndsInDirectorySeparator(buildRoot) ? buildRoot : buildRoot + Path.DirectorySeparatorChar;
        var leaf = Path.GetFileName(resolved);
        const string namePrefix = "options-lifecycle-";
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !leaf.StartsWith(namePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(leaf[namePrefix.Length..], "N", out _))
            throw new InvalidOperationException("Refusing lifecycle fixture cleanup outside the generated build-output directory.");
        if (!Directory.Exists(resolved)) return;
        if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refusing lifecycle fixture cleanup of a reparse-point directory.");
        Directory.Delete(resolved, recursive: true);
    }

    private static async Task<bool> VerifyActualReleaseAsync(string tempRoot, string? cachedRoot = null,
        bool requireVanillaSnapshot = false)
    {
        var packPath = Path.Combine(AppContext.BaseDirectory, TestPackRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var installRoot = Path.Combine(tempRoot, "live-pack-install");
        var vanilla = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        var vanillaBefore = CaptureVanillaData(vanilla);
        if (vanillaBefore is null)
        {
            Console.WriteLine("NOT RUN: vanilla mods/config/saves could not be read for a before/after comparison.");
            if (requireVanillaSnapshot) return false;
        }
        using var installer = CachedInstaller(packPath, TestPackRelease.ArtifactSha512, cachedRoot);
        var install = await installer.InstallAsync(packPath, TestPackRelease.ArtifactSha512, installRoot);
        if (!install.Success)
        {
            Console.WriteLine($"NOT RUN: actual release install did not complete ({install.Code}). {install.Message} Diagnostic log: {install.LogPath ?? "unavailable"}.");
            return false;
        }

        var instance = install.GameDirectory ?? throw new InvalidOperationException("Actual release install did not return an instance directory.");
        var manifest = InstallationManifest.Load(instance);
        var pack = PackArchive.Open(packPath, TestPackRelease.ArtifactSha512);
        var expectedManagedPaths = ExpectedManagedPackPaths(pack);
        True(manifest.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedManagedPaths),
            "actual Vanilla Plus manifest follows the pinned initial-config ownership policy");
        var irisPath = Path.Combine(instance, "config", "iris.properties");
        var hasInitialIris = pack.Overrides.Any(file => file.Path == "config/iris.properties");
        True(File.Exists(irisPath) == hasInitialIris && !manifest.Files.Any(file => file.Path == "config/iris.properties"),
            "initial Iris settings exist outside the managed manifest");
        True(new[]
        {
            "SubtleEffects-fabric-26.2-1.14.3.jar",
            "fzzy_config-0.7.6+26.2.jar", "fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar"
        }.All(name => manifest.Files.Any(file => file.Path == "mods/" + name)) &&
            !manifest.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)),
            "actual Vanilla Plus install excludes Smooth Swapping and manages the remaining effects mod and libraries");
        foreach (var file in manifest.Files)
        {
            var path = Path.Combine(instance, file.Path.Replace('/', Path.DirectorySeparatorChar));
            True(string.Equals(file.Sha512, HashFile(path), StringComparison.OrdinalIgnoreCase),
                $"actual release managed file hash {file.Path}");
        }
        var optionsPath = Path.Combine(instance, "options.txt");
        var expectedPacks = new[] { "vanilla" }.Concat(TestPackRelease.InitialResourcePacks.Select(name => "file/" + name)).Append("punchy:punchy");
        True(File.ReadAllText(optionsPath).Contains("resourcePacks:" + JsonSerializer.Serialize(expectedPacks), StringComparison.Ordinal),
            "all resource packs are selected on first launch in the pinned order");
        var options = File.ReadAllLines(optionsPath);
        True(new[]
        {
            "key_key.sprint:key.keyboard.left.shift", "key_key.sneak:key.keyboard.left.control",
            "fov:0.25", "fullscreen:true", "exclusiveFullscreen:true", "guiScale:4"
        }.All(options.Contains) && !options.Any(line => line.StartsWith("fullscreenResolution:", StringComparison.Ordinal)),
            "new instance starts with requested controls, FOV, fullscreen, and GUI scale without a fixed monitor mode");
        var bbeConfigPath = Path.Combine(instance, "config", "BBEConfig.json");
        using (var document = JsonDocument.Parse(File.ReadAllText(bbeConfigPath)))
        {
            var bbeOptions = document.RootElement.GetProperty("bbe.config.storage.main").EnumerateArray()
                .ToDictionary(entry => entry.GetProperty("option").GetString()!, entry => entry.GetProperty("value").GetBoolean());
            True(!bbeOptions["optimize.chest"] && !bbeOptions["optimize.shulker"],
                "BBE leaves Fresh Animations chest and shulker models visible");
        }
        File.WriteAllText(optionsPath, "resourcePacks:[\"vanilla\"]\n");
        File.WriteAllText(bbeConfigPath, "{}");
        if (hasInitialIris) File.WriteAllText(irisPath, "player Iris settings\n");
        var repair = await installer.RepairAsync(instance, packPath, TestPackRelease.ArtifactSha512);
        if (!repair.Success)
        {
            Console.WriteLine($"NOT RUN: actual release repair did not complete ({repair.Code}). {repair.Message}");
            return false;
        }
        True(File.ReadAllText(optionsPath) == "resourcePacks:[\"vanilla\"]\n" && File.ReadAllText(bbeConfigPath) == "{}" &&
             (!hasInitialIris || File.ReadAllText(irisPath) == "player Iris settings\n"),
            "repair preserves player settings and the unmanaged Iris config");
        var vanillaAfter = CaptureVanillaData(vanilla);
        var vanillaComparisonPassed = vanillaBefore is not null && vanillaAfter is not null && vanillaBefore == vanillaAfter;
        if (vanillaBefore is not null && vanillaAfter is not null)
            Equal(vanillaBefore, vanillaAfter, "vanilla mods/config/saves remain unchanged");
        else if (vanillaBefore is not null || vanillaAfter is not null)
            Console.WriteLine("NOT RUN: vanilla mods/config/saves comparison was incomplete because access changed.");
        else
            Console.WriteLine("NOT RUN: vanilla mods/config/saves before/after comparison unavailable due filesystem access restrictions.");
        Pass("actual pinned release downloads, verifies, and installs in a temporary isolated directory");

        var uninstall = await installer.UninstallAsync(installRoot, instance, packPath, TestPackRelease.ArtifactSha512);
        if (!uninstall.Success)
        {
            Console.WriteLine($"NOT RUN: actual release cleanup did not complete ({uninstall.Code}). {uninstall.Message}");
            return false;
        }
        True(File.ReadAllText(optionsPath) == "resourcePacks:[\"vanilla\"]\n" &&
             File.ReadAllText(bbeConfigPath) == "{}" &&
             (!hasInitialIris || File.ReadAllText(irisPath) == "player Iris settings\n"),
            "uninstall preserves player settings and the unmanaged Iris config");
        Pass("actual pinned release temporary install uninstalls cleanly");
        return !requireVanillaSnapshot || vanillaComparisonPassed;
    }

    private static async Task<bool> VerifyActualVanilla2PlusAsync(string tempRoot, string? cachedRoot = null,
        bool requireVanillaSnapshot = false)
    {
        var packPath = Path.Combine(AppContext.BaseDirectory,
            Vanilla2PlusRelease.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var installRoot = Path.Combine(tempRoot, "live-vanilla-2-plus-install");
        var vanilla = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        var vanillaBefore = CaptureVanillaData(vanilla);
        if (vanillaBefore is null)
        {
            Console.WriteLine("NOT RUN: vanilla mods/config/saves could not be read for a before/after comparison.");
            if (requireVanillaSnapshot) return false;
        }
        using var installer = CachedInstaller(packPath, Vanilla2PlusRelease.ArtifactSha512, cachedRoot);
        var install = await installer.InstallAsync(packPath, Vanilla2PlusRelease.ArtifactSha512, installRoot);
        if (!install.Success)
        {
            Console.WriteLine($"NOT RUN: Vanilla 2 Plus install did not complete ({install.Code}). {install.Message} Diagnostic log: {install.LogPath ?? "unavailable"}.");
            return false;
        }

        var instance = install.GameDirectory ?? throw new InvalidOperationException("Vanilla 2 Plus install did not return an instance directory.");
        var manifest = InstallationManifest.Load(instance);
        var pack = PackArchive.Open(packPath, Vanilla2PlusRelease.ArtifactSha512);
        var expectedManagedPaths = ExpectedManagedPackPaths(pack);
        Equal(Vanilla2PlusRelease.PackVersion, manifest.PackVersion, "installed Vanilla 2 Plus manifest version");
        True(manifest.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedManagedPaths) &&
             !manifest.Files.Any(file => file.Path == "config/iris.properties") &&
             YungsJarNames.Concat(NewForkJarNames).All(name => manifest.Files.Any(file => file.Path == "mods/" + name)),
            "Frontier manifest records pinned downloads and embedded files while leaving Iris unmanaged");
        var irisPath = Path.Combine(instance, "config", "iris.properties");
        var hasInitialIris = pack.Overrides.Any(file => file.Path == "config/iris.properties");
        True(File.Exists(irisPath) == hasInitialIris, "initial Frontier Iris config is present outside managed files");
        foreach (var file in manifest.Files)
        {
            var path = Path.Combine(instance, file.Path.Replace('/', Path.DirectorySeparatorChar));
            True(string.Equals(file.Sha512, HashFile(path), StringComparison.OrdinalIgnoreCase),
                $"Vanilla 2 Plus installed file hash {file.Path}");
        }
        var newModFiles = new[]
        {
            "mcw-windows-2.4.2-mc26.2fabric.jar", "mcw-fences-1.2.1-mc26.2fabric.jar",
            "mcw-bridges-3.1.2-mc26.2fabric.jar", "mcw-doors-1.1.5-mc26.2fabric.jar",
            "mcw-stairs-1.0.2-mc26.2fabric.jar", "bettervillage-fabric-26.2-4.0.0.jar",
            "libraryferret-fabric-26.2-5.0.0.jar", "MoogsNetherStructures-universal-1.21-3.1.1.jar",
            "MoogsStructureLib-fabric-26.2-3.3.0.jar", "MoogsVoyagerStructures-universal-1.21-5.1.3.jar",
            "Structory_26.2_v1.3.7.jar",
            "SubtleEffects-fabric-26.2-1.14.3.jar", "guardvillagers-2.1.3-26.2.jar",
            "fzzy_config-0.7.6+26.2.jar", "fabric-language-kotlin-1.14.1+kotlin.2.4.20.jar",
            "takesapillage-fabric-1.0.12+mc26.2.jar", "ResourcefulLib-5.0.4.jar",
            "Voxy World Gen V2-fabric-26.2-2.4.3.jar"
        };
        True(newModFiles.All(name => manifest.Files.Any(file => file.Path == "mods/" + name)) &&
             !manifest.Files.Any(file => file.Path.Contains("smoothswapping", StringComparison.OrdinalIgnoreCase)),
            "all Frontier additions are managed without Smooth Swapping");
        True(Vanilla2PlusRelease.InitialResourcePacks.Skip(7).All(name =>
                manifest.Files.Any(file => file.Path == "resourcepacks/" + name)),
            "both Guard Villagers animation resource packs are installed and managed");
        var optionsPath = Path.Combine(instance, "options.txt");
        var expectedPacks = new[] { "vanilla" }.Concat(Vanilla2PlusRelease.InitialResourcePacks.Select(name => "file/" + name)).Append("punchy:punchy");
        var options = File.ReadAllLines(optionsPath);
        True(options[0] == "version:4903" &&
             File.ReadAllText(optionsPath).Contains("resourcePacks:" + JsonSerializer.Serialize(expectedPacks), StringComparison.Ordinal),
            "Vanilla 2 Plus selects all twelve resource packs plus Punchy on first launch");
        True(!File.Exists(Path.Combine(instance, "resourcepacks", "LowOnFire v26.2§8.zip")) &&
             !File.ReadAllText(optionsPath).Contains("LowOnFire", StringComparison.Ordinal),
            "Frontier neither installs nor enables Low On Fire");
        var bbeConfigPath = Path.Combine(instance, "config", "BBEConfig.json");
        True(File.Exists(bbeConfigPath), "Vanilla 2 Plus applies the existing Better Block Entities config");
        var guardConfigPath = Path.Combine(instance, "config", "guardvillagers.json");
        using (var guardConfig = JsonDocument.Parse(File.ReadAllText(guardConfigPath)))
            True(guardConfig.RootElement.GetProperty("followHero").GetBoolean() == false &&
                 guardConfig.RootElement.GetProperty("reputationRequirement").GetInt32() == int.MinValue &&
                 !manifest.Files.Any(file => file.Path == "config/guardvillagers.json"),
                "Vanilla 2 Plus preconfigures guards without managing later player changes");
        var voxyConfigPath = Path.Combine(instance, "config", "voxyworldgenv2.json");
        using (var voxyConfig = JsonDocument.Parse(File.ReadAllText(voxyConfigPath)))
            True(voxyConfig.RootElement.GetProperty("generationRadius").GetInt32() == 128 &&
                 voxyConfig.RootElement.GetProperty("maxActiveTasks").GetInt32() == 3 &&
                 !manifest.Files.Any(file => file.Path == "config/voxyworldgenv2.json"),
                "Vanilla 2 Plus preconfigures Voxy WorldGen without managing later player changes");
        var worldPath = Path.Combine(instance, "saves", "plan004-test-world", "level.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(worldPath)!);
        File.WriteAllText(worldPath, "test world stays unmanaged");
        File.WriteAllText(optionsPath, "player options\n");
        File.WriteAllText(bbeConfigPath, "player BBE settings\n");
        if (hasInitialIris) File.WriteAllText(irisPath, "player Iris settings\n");
        File.WriteAllText(guardConfigPath, "player guard settings\n");
        File.WriteAllText(voxyConfigPath, "player Voxy WorldGen settings\n");
        var corruptMacaw = Path.Combine(instance, "mods", "mcw-stairs-1.0.2-mc26.2fabric.jar");
        File.WriteAllText(corruptMacaw, "corrupt managed mod");
        var repair = await installer.RepairAsync(instance, packPath, Vanilla2PlusRelease.ArtifactSha512);
        if (!repair.Success)
        {
            Console.WriteLine($"NOT RUN: Vanilla 2 Plus repair did not complete ({repair.Code}). {repair.Message}");
            return false;
        }
        True(File.ReadAllText(optionsPath) == "player options\n" &&
             File.ReadAllText(bbeConfigPath) == "player BBE settings\n" &&
             File.ReadAllText(guardConfigPath) == "player guard settings\n" &&
             File.ReadAllText(voxyConfigPath) == "player Voxy WorldGen settings\n" &&
             (!hasInitialIris || File.ReadAllText(irisPath) == "player Iris settings\n") && File.Exists(worldPath),
            "Vanilla 2 Plus Repair restores its pinned mod and preserves user settings and world");
        var vanillaAfter = CaptureVanillaData(vanilla);
        var vanillaComparisonPassed = vanillaBefore is not null && vanillaAfter is not null && vanillaBefore == vanillaAfter;
        if (vanillaBefore is not null && vanillaAfter is not null)
            Equal(vanillaBefore, vanillaAfter, "Vanilla 2 Plus leaves vanilla mods/config/saves unchanged");
        else if (vanillaBefore is not null || vanillaAfter is not null)
            Console.WriteLine("NOT RUN: vanilla mods/config/saves comparison was incomplete because access changed.");

        var uninstall = await installer.UninstallAsync(installRoot, instance, packPath, Vanilla2PlusRelease.ArtifactSha512);
        if (!uninstall.Success)
        {
            Console.WriteLine($"NOT RUN: Vanilla 2 Plus uninstall did not complete ({uninstall.Code}). {uninstall.Message}");
            return false;
        }
        True(File.Exists(worldPath) && File.ReadAllText(optionsPath) == "player options\n" &&
             File.ReadAllText(bbeConfigPath) == "player BBE settings\n" &&
             File.ReadAllText(guardConfigPath) == "player guard settings\n" &&
             File.ReadAllText(voxyConfigPath) == "player Voxy WorldGen settings\n" &&
             (!hasInitialIris || File.ReadAllText(irisPath) == "player Iris settings\n"),
            "Vanilla 2 Plus Uninstall removes managed files and preserves user data");
        True(installer.GetActiveInstancePath(installRoot) is null, "Vanilla 2 Plus Uninstall clears the active marker");
        Pass("Vanilla 2 Plus actual downloads, SHA-512 checks, install, Repair, and Uninstall");
        return !requireVanillaSnapshot || vanillaComparisonPassed;
    }

    private static HashSet<string> ExpectedManagedPackPaths(PackArchive pack) => pack.Files.Select(file => file.Path)
        .Concat(pack.Overrides.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path)).Select(file => file.Path))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? CaptureVanillaData(string vanillaRoot)
    {
        var paths = new[] { "mods", "config", "saves" };
        var records = new List<string>();
        try
        {
            foreach (var relative in paths)
            {
                var directory = Path.Combine(vanillaRoot, relative);
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    records.Add($"{relative}/{Path.GetRelativePath(directory, path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
                }
            }
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
        return string.Join("\n", records);
    }

    private static void VerifyLauncherFixture(string tempRoot)
    {
        const string input = "{\"settings\":{\"custom\":true},\"profiles\":{\"vanilla\":{\"name\":\"Existing\",\"customField\":17}}}";
        var candidateGameDir = Path.Combine(Path.GetTempPath(), "minepack-game");
        var candidate = LauncherProfile.BuildFixtureCandidate(input, candidateGameDir, TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using var added = JsonDocument.Parse(candidate);
        var root = added.RootElement;
        True(root.GetProperty("settings").GetProperty("custom").GetBoolean(), "unknown root Launcher fields are preserved");
        True(root.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32() == 17,
            "unowned Launcher profile fields are preserved");
        var own = root.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey);
        Equal(LauncherProfile.ProfileName(TestPackRelease.MinecraftVersion), own.GetProperty("name").GetString(),
            "new Launcher profile has the shared versioned display name");
        Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "minepack-game")), own.GetProperty("gameDir").GetString(), "fixture profile gameDir");
        var removed = LauncherProfile.RemoveOwnedProfile(candidate, candidateGameDir);
        using var removedJson = JsonDocument.Parse(removed);
        True(!removedJson.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _), "only the owned Launcher fixture profile is removed");
        True(removedJson.RootElement.GetProperty("profiles").TryGetProperty("vanilla", out _), "other Launcher profile remains");

        const string conflict = "{\"profiles\":{\"minepack-test-pack\":{\"name\":\"Someone else's profile\"}}}";
        try
        {
            _ = LauncherProfile.BuildFixtureCandidate(conflict, Path.GetTempPath(), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
            throw new InvalidOperationException("Expected Launcher profile conflict rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }

        var oldInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.8.0-" + TestPackRelease.AnimationArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.8.0",
            MinecraftVersion = "26.2",
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.AnimationArtifactSha512
        }.SaveAtomic(oldInstance);
        string ProfileWithoutMarker(string gameDir, string name = "MinePack Test Pack", string lastVersionId = "fabric-loader-0.19.5-26.2") => JsonSerializer.Serialize(new
        {
            profiles = new Dictionary<string, object>
            {
                [LauncherProfile.ProfileKey] = new
                {
                    name, type = "custom",
                    lastVersionId, gameDir
                }
            }
        });
        foreach (var (version, hash) in new[]
                 {
                     ("0.15.0", TestPackRelease.SmoothArtifactSha512),
                     ("0.18.0", TestPackRelease.LowFireArtifactSha512),
                     ("0.19.5", Vanilla2PlusRelease.DoorsArtifactSha512),
                     ("0.17.0", Vanilla2PlusRelease.WorldgenArtifactSha512),
                     ("0.19.0", Vanilla2PlusRelease.UntunedArtifactSha512),
                     ("0.19.1", Vanilla2PlusRelease.TunedArtifactSha512)
                 })
        {
            var instance = Path.Combine(tempRoot, "previous-owned-instance", "instances",
                $"test-pack-{version}-{hash[..12].ToLowerInvariant()}");
            new InstallationManifest
            {
                PackVersion = version,
                MinecraftVersion = TestPackRelease.MinecraftVersion,
                FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
                PackArchiveSha512 = hash
            }.SaveAtomic(instance);
            using var removedPrevious = JsonDocument.Parse(LauncherProfile.RemoveOwnedProfile(ProfileWithoutMarker(instance), instance));
            True(!removedPrevious.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                $"previous {version} profile remains recognized for uninstall");
        }
        var reinstallInstances = Path.Combine(tempRoot, "reinstall-owned-instance", "instances");
        Directory.CreateDirectory(reinstallInstances);
        var reinstallBaseName = $"test-pack-{Vanilla2PlusRelease.PackVersion}-{Vanilla2PlusRelease.ArtifactSha512[..12].ToLowerInvariant()}-reinstall-";
        string CreateReinstallFixture(string suffix, bool saveManifest = true, string? manifestHash = null, string? manifestVersion = null, bool nested = false)
        {
            var parent = nested ? Path.Combine(reinstallInstances, "nested") : reinstallInstances;
            Directory.CreateDirectory(parent);
            var instance = Path.Combine(parent, reinstallBaseName + suffix);
            Directory.CreateDirectory(instance);
            if (saveManifest)
            {
                new InstallationManifest
                {
                    PackVersion = manifestVersion ?? Vanilla2PlusRelease.PackVersion,
                    MinecraftVersion = TestPackRelease.MinecraftVersion,
                    FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
                    PackArchiveSha512 = manifestHash ?? Vanilla2PlusRelease.ArtifactSha512
                }.SaveAtomic(instance);
            }
            return instance;
        }
        void RejectMarkerlessProfile(string profileJson, string label)
        {
            try
            {
                _ = LauncherProfile.BuildFixtureCandidate(profileJson, Path.Combine(tempRoot, "replacement-instance"),
                    TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
                throw new InvalidOperationException($"Expected {label} profile rejection during restore.");
            }
            catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
            try
            {
                using var profile = JsonDocument.Parse(profileJson);
                var gameDirectory = profile.RootElement.GetProperty("profiles")
                    .GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString()!;
                _ = LauncherProfile.RemoveOwnedProfile(profileJson, gameDirectory);
                throw new InvalidOperationException($"Expected {label} profile rejection during uninstall.");
            }
            catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        }
        var markerless = ProfileWithoutMarker(oldInstance);
        var reconfigured = LauncherProfile.BuildFixtureCandidate(markerless, Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(reconfigured))
        {
            Equal(Path.Combine(tempRoot, "new-instance"), parsed.RootElement.GetProperty("profiles")
                .GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString(),
                "Launcher-stripped marker can be recovered from a pinned MinePack manifest");
            Equal(LauncherProfile.ProfileName(TestPackRelease.MinecraftVersion), parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("name").GetString(), "old profile is renamed in place");
            Equal(1, parsed.RootElement.GetProperty("profiles").EnumerateObject().Count(),
                "renaming does not create a second profile");
        }
        var markerlessNewName = ProfileWithoutMarker(oldInstance, LauncherProfile.ProfileName(TestPackRelease.MinecraftVersion));
        var newNamed = LauncherProfile.BuildFixtureCandidate(markerlessNewName,
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(newNamed))
            Equal(LauncherProfile.ProfileName(TestPackRelease.MinecraftVersion), parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("name").GetString(), "new markerless name is recognized with pinned manifest");
        using (var parsed = JsonDocument.Parse(LauncherProfile.RemoveOwnedProfile(markerlessNewName, oldInstance)))
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "markerless versioned profile can be removed while its manifest exists");

        var legacy26_3 = InstalledInstanceCatalog.KnownReleases.Single(release => release.PackVersion == "0.2.0");
        var legacy26_3Instance = Path.Combine(tempRoot, "legacy-26.3-instance", "instances", legacy26_3.InstanceDirectoryPrefix);
        new InstallationManifest
        {
            PackVersion = legacy26_3.PackVersion,
            MinecraftVersion = legacy26_3.MinecraftVersion,
            FabricLoaderVersion = legacy26_3.FabricLoaderVersion,
            PackArchiveSha512 = legacy26_3.ArchiveSha512
        }.SaveAtomic(legacy26_3Instance);
        var legacy26_3Profile = ProfileWithoutMarker(legacy26_3Instance, LauncherProfile.ProfileName("26.3"),
            $"fabric-loader-{legacy26_3.FabricLoaderVersion}-26.3");
        var reconfigured26_3 = LauncherProfile.BuildFixtureCandidate(legacy26_3Profile,
            Path.Combine(tempRoot, "legacy-26.3-reconfigured"), "26.3", legacy26_3.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(reconfigured26_3))
            Equal(LauncherProfile.ProfileName("26.3"), parsed.RootElement.GetProperty("profiles")
                .GetProperty(LauncherProfile.ProfileKey).GetProperty("name").GetString(),
                "markerless legacy release keeps the exact versioned profile name");
        using (var parsed = JsonDocument.Parse(LauncherProfile.RemoveOwnedProfile(legacy26_3Profile, legacy26_3Instance)))
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "markerless versioned legacy profile can be removed using its pinned manifest");

        var reinstallInstance = CreateReinstallFixture(Guid.NewGuid().ToString("N"));
        var reinstallRoot = new JsonObject
        {
            ["settings"] = new JsonObject { ["custom"] = true },
            ["profiles"] = JsonNode.Parse(ProfileWithoutMarker(reinstallInstance, "MinePack"))!["profiles"]!.DeepClone()
        };
        ((JsonObject)reinstallRoot["profiles"]!)["vanilla"] = new JsonObject
        {
            ["name"] = "Existing", ["type"] = "custom", ["customField"] = 17
        };
        var markerlessReinstall = reinstallRoot.ToJsonString();
        var restoredReinstall = LauncherProfile.BuildFixtureCandidate(markerlessReinstall,
            Path.Combine(tempRoot, "reinstall-replacement"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(restoredReinstall))
        {
            var restoredProfile = parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey);
            Equal(LauncherProfile.ProfileName(TestPackRelease.MinecraftVersion), restoredProfile.GetProperty("name").GetString(),
                "markerless reinstall profile is restored in place");
            Equal(Path.GetFullPath(Path.Combine(tempRoot, "reinstall-replacement")), restoredProfile.GetProperty("gameDir").GetString(),
                "reinstall profile restore selects the requested instance");
            Equal(2, parsed.RootElement.GetProperty("profiles").EnumerateObject().Count(),
                "reinstall profile restore does not duplicate MinePack or drop the foreign profile");
            Equal(17, parsed.RootElement.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32(),
                "reinstall profile restore preserves a foreign profile");
            True(parsed.RootElement.GetProperty("settings").GetProperty("custom").GetBoolean(),
                "reinstall profile restore preserves root settings");
        }
        var removedReinstall = LauncherProfile.RemoveOwnedProfile(markerlessReinstall, reinstallInstance);
        using (var parsed = JsonDocument.Parse(removedReinstall))
        {
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "markerless reinstall profile can be removed for its exact instance");
            Equal(17, parsed.RootElement.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32(),
                "reinstall profile uninstall preserves a foreign profile");
            True(parsed.RootElement.GetProperty("settings").GetProperty("custom").GetBoolean(),
                "reinstall profile uninstall preserves root settings");
        }
        Equal(markerlessReinstall,
            LauncherProfile.RemoveOwnedProfile(markerlessReinstall, Path.Combine(tempRoot, "another-instance")),
            "reinstall profile is preserved when expected gameDir differs");

        foreach (var (suffix, label) in new[]
                 {
                     (new string('a', 31), "short reinstall suffix"),
                     (new string('a', 31) + "g", "non-hex reinstall suffix"),
                     (new string('A', 32), "uppercase reinstall suffix")
                 })
            RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(suffix), "MinePack"), label);

        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"), saveManifest: false), "MinePack"),
            "missing-manifest reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"),
            manifestHash: TestPackRelease.ArtifactSha512), "MinePack"), "foreign-manifest reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"),
            manifestVersion: "9.9.9"), "MinePack"), "unknown-version reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(reinstallInstance, "MinePack", "fabric-loader-0.19.5-26.3"),
            "wrong-lastVersionId reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(oldInstance, LauncherProfile.ProfileName("26.3")),
            "wrong-version display name");
        RejectMarkerlessProfile(ProfileWithoutMarker(oldInstance, LauncherProfile.ProfileName("26.2") + " custom"),
            "display name with suffix");
        RejectMarkerlessProfile(ProfileWithoutMarker(reinstallInstance, "Someone else's profile"), "foreign-name reinstall");
        var foreignMarkerReinstall = JsonSerializer.Serialize(new
        {
            profiles = new Dictionary<string, object>
            {
                [LauncherProfile.ProfileKey] = new
                {
                    name = "MinePack", type = "custom", lastVersionId = "fabric-loader-0.19.5-26.2",
                    gameDir = reinstallInstance, minepackInstallerId = "foreign-installer"
                }
            }
        });
        RejectMarkerlessProfile(foreignMarkerReinstall, "foreign-marker reinstall");
        RejectMarkerlessProfile(ProfileWithoutMarker(CreateReinstallFixture(Guid.NewGuid().ToString("N"), nested: true), "MinePack"),
            "nested reinstall directory");
        Pass("markerless reinstall profiles restore and uninstall only with exact pinned manifest and directory ownership");

        var previousInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.9.0-" + TestPackRelease.MapArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.9.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.MapArtifactSha512
        }.SaveAtomic(previousInstance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var priorVanillaPlusInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.10.0-" + TestPackRelease.PriorArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.10.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.PriorArtifactSha512
        }.SaveAtomic(priorVanillaPlusInstance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(priorVanillaPlusInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var previousVanilla2PlusInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.11.0-" + Vanilla2PlusRelease.OriginalArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.11.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.OriginalArtifactSha512
        }.SaveAtomic(previousVanilla2PlusInstance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousVanilla2PlusInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var previousVanilla2Plus12Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.12.0-" + Vanilla2PlusRelease.LegacyArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.12.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.LegacyArtifactSha512
        }.SaveAtomic(previousVanilla2Plus12Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousVanilla2Plus12Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var previousVanilla2Plus13Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.13.0-" + Vanilla2PlusRelease.PreviousArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.13.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.PreviousArtifactSha512
        }.SaveAtomic(previousVanilla2Plus13Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(previousVanilla2Plus13Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var priorVanilla2Plus14Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.14.0-" + Vanilla2PlusRelease.PriorArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.14.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.PriorArtifactSha512
        }.SaveAtomic(priorVanilla2Plus14Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(priorVanilla2Plus14Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        var guardVanilla2Plus16Instance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-0.16.0-" + Vanilla2PlusRelease.GuardArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = "0.16.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.GuardArtifactSha512
        }.SaveAtomic(guardVanilla2Plus16Instance);
        _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(guardVanilla2Plus16Instance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        Equal(markerless, LauncherProfile.RemoveOwnedProfile(markerless, Path.Combine(tempRoot, "new-instance")),
            "uninstalling a different instance preserves the current MinePack profile");
        var vanilla2PlusInstance = Path.Combine(tempRoot, "owned-instance", "instances",
            "test-pack-" + Vanilla2PlusRelease.PackVersion + "-" + Vanilla2PlusRelease.ArtifactSha512[..12].ToLowerInvariant());
        new InstallationManifest
        {
            PackVersion = Vanilla2PlusRelease.PackVersion,
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = Vanilla2PlusRelease.ArtifactSha512
        }.SaveAtomic(vanilla2PlusInstance);
        var vanilla2PlusProfile = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(vanilla2PlusInstance, "MinePack"),
            Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
        using (var parsed = JsonDocument.Parse(vanilla2PlusProfile))
            Equal(Path.GetFullPath(Path.Combine(tempRoot, "new-instance")),
                parsed.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey).GetProperty("gameDir").GetString(),
                "markerless Launcher recognizes the exact Vanilla 2 Plus manifest");
        using (var parsed = JsonDocument.Parse(LauncherProfile.RemoveOwnedProfile(ProfileWithoutMarker(vanilla2PlusInstance, "MinePack"), vanilla2PlusInstance)))
            True(!parsed.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _),
                "Vanilla 2 Plus profile can be removed only while its own manifest exists");
        try
        {
            _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(Path.Combine(tempRoot, "foreign-instance")),
                Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
            throw new InvalidOperationException("Expected unowned markerless profile rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        try
        {
            _ = LauncherProfile.BuildFixtureCandidate(ProfileWithoutMarker(oldInstance, "Someone else's profile"),
                Path.Combine(tempRoot, "new-instance"), TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion);
            throw new InvalidOperationException("Expected foreign-name profile rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        Pass("Launcher fixture changes only the marked profile and preserves other JSON fields");
    }

    private static async Task VerifyAutomaticFabricProfileAsync(string tempRoot)
    {
        var launcherRoot = Path.Combine(tempRoot, "launcher-fixture");
        var installRoot = Path.Combine(tempRoot, "fabric-log-root");
        var gameDirectory = Path.Combine(installRoot, "instances", "fixture-instance");
        Directory.CreateDirectory(gameDirectory);
        new InstallationManifest
        {
            PackVersion = "0.1.0",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = new string('A', 128),
            InstalledAt = DateTimeOffset.UtcNow,
            Files = []
        }.SaveAtomic(gameDirectory);
        Directory.CreateDirectory(launcherRoot);
        var profilesPath = Path.Combine(launcherRoot, "launcher_profiles.json");
        const string input = "{\"profiles\":{\"vanilla\":{\"name\":\"Original\",\"customField\":17}},\"settings\":{\"custom\":true}}";
        File.WriteAllText(profilesPath, input);
        const string versionId = "fabric-loader-0.19.5-26.2";
        var profileHash = HashBytes(Bytes($"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"26.2\"}}"));
        var archive = CreateFabricProfileArchive(versionId,
            $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"26.2\",\"releaseTime\":\"2026-09-24\",\"time\":\"2026-09-24\"}}");

        var hydratedJar = Bytes("official Minecraft client JAR fixture");
        using var service = new FabricLauncherService(launcherRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            profileHash, HashBytes(hydratedJar), hydratedJar.Length, ensureLauncherClosed: static () => { });
        var operationLog = new OperationLog("fabric_configure", "9.9.9", installRoot, "0.1.0",
            TestPackRelease.MinecraftVersion, TestPackRelease.FabricLoaderVersion, new string('A', 128));
        await service.ConfigureAsync(gameDirectory, operationLog: operationLog);
        await service.ConfigureAsync(gameDirectory, operationLog: operationLog);
        var version = Path.Combine(launcherRoot, "versions", versionId);
        True(File.Exists(Path.Combine(version, versionId + ".json")), "Fabric version JSON installed");
        True(File.Exists(Path.Combine(version, versionId + ".jar")), "Fabric version dummy JAR installed");
        var versionJar = Path.Combine(version, versionId + ".jar");
        File.WriteAllBytes(versionJar, hydratedJar);
        await service.ConfigureAsync(gameDirectory, operationLog: operationLog);
        operationLog.Complete("completed");
        var profileLogPath = operationLog.CurrentLogPath
            ?? throw new InvalidOperationException("Fabric operation log was not bound to its validated install root.");
        var profileLog = File.ReadAllText(profileLogPath);
        True(profileLog.Contains("fabric_profile_preflight_passed", StringComparison.Ordinal) &&
             profileLog.Contains("fabric_profile_configured", StringComparison.Ordinal) &&
             profileLog.Contains("\"installerVersion\":\"9.9.9\"", StringComparison.Ordinal),
            "Fabric profile operations share a validated-root log with the supplied installer version");
        Equal(HashBytes(hydratedJar), HashFile(versionJar), "Launcher-filled official client JAR remains unchanged");
        var retimedArchive = CreateFabricProfileArchive(versionId,
            $"{{\"time\":\"2026-09-26\",\"releaseTime\":\"2026-09-26\",\"inheritsFrom\":\"26.2\",\"id\":\"{versionId}\"}}");
        using (var retimedService = new FabricLauncherService(launcherRoot,
                   new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(retimedArchive) }),
                   profileHash, HashBytes(hydratedJar), hydratedJar.Length, ensureLauncherClosed: static () => { }))
            await retimedService.ConfigureAsync(gameDirectory);
        Equal(HashBytes(hydratedJar), HashFile(versionJar), "changed Fabric timestamps leave the official client JAR intact");
        var alteredArchive = CreateFabricProfileArchive(versionId,
            $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"26.2\",\"releaseTime\":\"2026-09-26\",\"time\":\"2026-09-26\",\"libraries\":[{{\"name\":\"foreign:library:1\"}}]}}");
        var alteredRoot = Path.Combine(tempRoot, "launcher-altered-archive");
        Directory.CreateDirectory(alteredRoot);
        var alteredGameDirectory = Path.Combine(tempRoot, "altered-archive-game");
        Directory.CreateDirectory(alteredGameDirectory);
        var alteredProfilesPath = Path.Combine(alteredRoot, "launcher_profiles.json");
        File.WriteAllText(alteredProfilesPath, input);
        var profileBeforeAlteredArchive = File.ReadAllText(alteredProfilesPath);
        using (var alteredService = new FabricLauncherService(alteredRoot,
                   new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(alteredArchive) }),
                   profileHash, HashBytes(hydratedJar), hydratedJar.Length, ensureLauncherClosed: static () => { }))
        {
            try { await alteredService.ConfigureAsync(alteredGameDirectory); throw new InvalidOperationException("Expected changed Fabric libraries to be rejected."); }
            catch (InstallerException ex) when (ex.Code == "FABRIC_HASH") { }
        }
        Equal(profileBeforeAlteredArchive, File.ReadAllText(alteredProfilesPath), "changed Fabric libraries leave Launcher profile untouched");
        var profileBeforeConflict = File.ReadAllText(profilesPath);
        File.WriteAllText(versionJar, "different client JAR");
        try { await service.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected foreign Fabric JAR rejection."); }
        catch (InstallerException ex) when (ex.Code == "FABRIC_VERSION_CONFLICT") { }
        Equal(profileBeforeConflict, File.ReadAllText(profilesPath), "foreign Fabric JAR leaves Launcher profile untouched");
        True(Directory.EnumerateFiles(launcherRoot, "launcher_profiles.json.minepack-*.bak").Any(),
            "Launcher profile backup created");
        using (var document = JsonDocument.Parse(File.ReadAllText(profilesPath)))
        {
            var root = document.RootElement;
            True(root.GetProperty("settings").GetProperty("custom").GetBoolean(), "Launcher settings preserved");
            Equal(17, root.GetProperty("profiles").GetProperty("vanilla").GetProperty("customField").GetInt32(), "vanilla profile preserved");
            Equal(Path.GetFullPath(gameDirectory), root.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("gameDir").GetString(), "automatic profile uses isolated game directory");
        }
        service.RemoveOwnProfile(gameDirectory);
        using (var document = JsonDocument.Parse(File.ReadAllText(profilesPath)))
            True(!document.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _), "only owned profile removed");

        var badRoot = Path.Combine(tempRoot, "launcher-bad-hash");
        Directory.CreateDirectory(badRoot);
        var badProfiles = Path.Combine(badRoot, "launcher_profiles.json");
        File.WriteAllText(badProfiles, input);
        using var badService = new FabricLauncherService(badRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            HashBytes(Bytes("different")), ensureLauncherClosed: static () => { });
        try { await badService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected Fabric hash rejection."); }
        catch (InstallerException ex) when (ex.Code == "FABRIC_HASH") { }
        Equal(input, File.ReadAllText(badProfiles), "bad Fabric archive leaves Launcher profile untouched");

        var conflictRoot = Path.Combine(tempRoot, "launcher-conflict");
        Directory.CreateDirectory(conflictRoot);
        const string conflict = "{\"profiles\":{\"minepack-test-pack\":{\"name\":\"Someone else's profile\"}}}";
        var conflictProfiles = Path.Combine(conflictRoot, "launcher_profiles.json");
        File.WriteAllText(conflictProfiles, conflict);
        using var conflictService = new FabricLauncherService(conflictRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            profileHash, ensureLauncherClosed: static () => { });
        try { await conflictService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected Launcher conflict rejection."); }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_CONFLICT") { }
        Equal(conflict, File.ReadAllText(conflictProfiles), "foreign profile preserved after conflict");
        True(!Directory.Exists(Path.Combine(conflictRoot, "versions", versionId)), "new Fabric version rolled back after profile conflict");
        var previousRoot = Path.Combine(tempRoot, "launcher-previous-release");
        Directory.CreateDirectory(previousRoot);
        File.WriteAllText(Path.Combine(previousRoot, "launcher_profiles.json"), input);
        const string previousId = "fabric-loader-0.19.5-26.3";
        var previousProfileHash = HashBytes(Bytes($"{{\"id\":\"{previousId}\",\"inheritsFrom\":\"26.3\"}}"));
        var previousArchive = CreateFabricProfileArchive(previousId,
            $"{{\"id\":\"{previousId}\",\"inheritsFrom\":\"26.3\",\"releaseTime\":\"2026-09-24\",\"time\":\"2026-09-24\"}}");
        using (var previousService = new FabricLauncherService(previousRoot,
                   new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(previousArchive) }),
                   expectedSha512: previousProfileHash, minecraftVersion: "26.3", ensureLauncherClosed: static () => { }))
            await previousService.ConfigureAsync(gameDirectory);
        using (var previousProfile = JsonDocument.Parse(File.ReadAllText(Path.Combine(previousRoot, "launcher_profiles.json"))))
            Equal(previousId, previousProfile.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("lastVersionId").GetString(), "previous release retains its Fabric version");
        Pass("automatic Fabric profile accepts the official Launcher-filled JAR and rejects foreign files");

        var reopenedRoot = Path.Combine(tempRoot, "launcher-reopened");
        Directory.CreateDirectory(reopenedRoot);
        var reopenedProfile = Path.Combine(reopenedRoot, "launcher_profiles.json");
        File.WriteAllText(reopenedProfile, input);
        var guardCalls = 0;
        using var reopenedService = new FabricLauncherService(reopenedRoot,
            new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) }),
            profileHash, ensureLauncherClosed: () =>
            {
                if (++guardCalls == 2)
                    throw new InstallerException("LAUNCHER_RUNNING", "fixture: Launcher reopened");
            });
        try { await reopenedService.ConfigureAsync(gameDirectory); throw new InvalidOperationException("Expected reopened Launcher rejection."); }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_RUNNING") { }
        Equal(2, guardCalls, "Launcher is rechecked immediately before profile write");
        Equal(input, File.ReadAllText(reopenedProfile), "reopened Launcher leaves profile untouched");
        True(!Directory.Exists(Path.Combine(reopenedRoot, "versions", versionId)), "Fabric staging is rolled back when Launcher reopens");

        var ambiguousRoot = Path.Combine(tempRoot, "launcher-ambiguous-profiles");
        Directory.CreateDirectory(ambiguousRoot);
        File.WriteAllText(Path.Combine(ambiguousRoot, "launcher_profiles.json"), input);
        File.WriteAllText(Path.Combine(ambiguousRoot, "launcher_profiles_microsoft_store.json"), input);
        var guardUnexpected = false;
        using var ambiguousService = new FabricLauncherService(ambiguousRoot,
            ensureLauncherClosed: () => guardUnexpected = true);
        try { ambiguousService.CheckReady(); throw new InvalidOperationException("Expected ambiguous Launcher profiles rejection."); }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_PROFILE_UNKNOWN") { }
        True(!guardUnexpected, "ambiguous profile files stop before process checks or mutation");
        Pass("Launcher lifecycle rechecks before profile writes and rejects ambiguous profile files");
    }

    private static async Task VerifyCurrentPerformanceLauncherDefaultsAsync(string tempRoot)
    {
        var currentGame = Path.Combine(tempRoot, "current-performance-game");
        Directory.CreateDirectory(currentGame);
        new InstallationManifest
        {
            PackVersion = TestPackRelease.PackVersion,
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.ArtifactSha512,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = []
        }.SaveAtomic(currentGame);
        var currentRoot = Path.Combine(tempRoot, "current-performance-launcher");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(currentRoot).FullName, "launcher_profiles.json"), "{\"profiles\":{}}");
        using (var service = CreateOfflineFixtureLauncher(currentRoot))
        {
            await service.ConfigureAsync(currentGame);
            using var first = JsonDocument.Parse(File.ReadAllText(Path.Combine(currentRoot, "launcher_profiles.json")));
            Equal("-Xmx8G", first.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("javaArgs").GetString(), "ConfigureCoreAsync applies the heap default only to the exact current release manifest");
            await service.ConfigureAsync(currentGame);
            var configured = File.ReadAllText(Path.Combine(currentRoot, "launcher_profiles.json"));
            Equal(1, Regex.Matches(configured, "-Xmx8G", RegexOptions.CultureInvariant).Count,
                "repeated profile configuration does not duplicate the current heap argument");
            var custom = JsonNode.Parse(configured)!.AsObject();
            custom["profiles"]![LauncherProfile.ProfileKey]!["javaArgs"] = "-Xmx5G -XX:+UseG1GC";
            File.WriteAllText(Path.Combine(currentRoot, "launcher_profiles.json"), custom.ToJsonString());
            await service.ConfigureAsync(currentGame);
            using var preserved = JsonDocument.Parse(File.ReadAllText(Path.Combine(currentRoot, "launcher_profiles.json")));
            Equal("-Xmx5G -XX:+UseG1GC", preserved.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("javaArgs").GetString(), "ConfigureCoreAsync preserves explicit custom JVM arguments on repair");
        }

        var legacyGame = Path.Combine(tempRoot, "former-current-performance-game");
        Directory.CreateDirectory(legacyGame);
        new InstallationManifest
        {
            PackVersion = "0.18.2",
            MinecraftVersion = TestPackRelease.MinecraftVersion,
            FabricLoaderVersion = TestPackRelease.FabricLoaderVersion,
            PackArchiveSha512 = TestPackRelease.FormerCurrentArtifactSha512,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = []
        }.SaveAtomic(legacyGame);
        var legacyRoot = Path.Combine(tempRoot, "former-current-performance-launcher");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(legacyRoot).FullName, "launcher_profiles.json"), "{\"profiles\":{}}");
        using (var service = CreateOfflineFixtureLauncher(legacyRoot))
            await service.ConfigureAsync(legacyGame);
        using var legacyProfile = JsonDocument.Parse(File.ReadAllText(Path.Combine(legacyRoot, "launcher_profiles.json")));
        True(!legacyProfile.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .TryGetProperty("javaArgs", out _),
            "former-current release Repair does not receive the new JVM default");
        Pass("official Launcher defaults are release-bound and preserve explicit user JVM settings");
    }

    private static FabricLauncherService CreateOfflineFixtureLauncher(string launcherRoot)
    {
        var versionId = $"fabric-loader-{TestPackRelease.FabricLoaderVersion}-{TestPackRelease.MinecraftVersion}";
        var versions = Path.Combine(launcherRoot, "versions", versionId);
        Directory.CreateDirectory(versions);
        File.WriteAllText(Path.Combine(versions, versionId + ".json"),
            $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"{TestPackRelease.MinecraftVersion}\",\"time\":\"fixture\",\"releaseTime\":\"fixture\"}}");
        File.WriteAllBytes(Path.Combine(versions, versionId + ".jar"), []);
        var expectedProfileHash = HashBytes(Bytes($"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"{TestPackRelease.MinecraftVersion}\"}}"));
        return new FabricLauncherService(launcherRoot, expectedSha512: expectedProfileHash,
            expectedClientJarSha512: HashBytes([]), expectedClientJarSize: 0,
            minecraftVersion: TestPackRelease.MinecraftVersion, ensureLauncherClosed: static () => { });
    }

    private static async Task VerifyLauncherLifecycleAsync(string tempRoot)
    {
        var storeTarget = new MinecraftLauncherTarget(MinecraftLauncherKind.Store,
            "Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft");
        var storePlatform = new FakeLauncherPlatform([storeTarget]);
        var storeController = new MinecraftLauncherController(storePlatform, TimeSpan.FromMilliseconds(100));
        var discoveredStore = await storeController.CloseBeforeInstallAsync();
        Equal(storeTarget, discoveredStore, "registered Store AUMID is selected when already closed");
        True(storePlatform.Events.Count == 0, "already closed Launcher receives no close request");
        var profileConfigured = false;
        var storeStart = await storeController.ConfigureAndStartAsync(storeTarget, () =>
        {
            storePlatform.Events.Add("profile");
            profileConfigured = true;
            return Task.CompletedTask;
        });
        Equal(MinecraftLauncherStartStatus.Requested, storeStart.Status, "Store Launcher start request succeeds after profile setup");
        True(profileConfigured && storePlatform.Events.IndexOf("profile") < storePlatform.Events.IndexOf("start"),
            "Store Launcher starts only after profile configuration");

        var win32Target = new MinecraftLauncherTarget(MinecraftLauncherKind.Win32, Path.Combine(tempRoot, "MinecraftLauncher.exe"));
        var win32Platform = new FakeLauncherPlatform([win32Target]);
        win32Platform.SetRunning(win32Target, allowClose: true);
        var win32Controller = new MinecraftLauncherController(win32Platform, TimeSpan.FromMilliseconds(100));
        var selectedWin32 = await win32Controller.CloseBeforeInstallAsync();
        Equal(win32Target, selectedWin32, "registered Win32 path is selected");
        win32Platform.Events.Add("install");
        True(win32Platform.Events.IndexOf("close") < win32Platform.Events.IndexOf("exit") &&
            win32Platform.Events.IndexOf("exit") < win32Platform.Events.IndexOf("install"),
            "installation begins only after graceful exit is confirmed");

        var storeProfileRoot = Path.Combine(tempRoot, "store-with-standard-profile");
        Directory.CreateDirectory(storeProfileRoot);
        File.WriteAllText(Path.Combine(storeProfileRoot, "launcher_profiles.json"), "{}");
        using var storeProfileService = new FabricLauncherService(storeProfileRoot, ensureLauncherClosed: static () => { });
        storeProfileService.CheckProfileReady();
        Equal(storeTarget, await storeController.CloseBeforeInstallAsync(),
            "registered Store Launcher may use launcher_profiles.json");

        var timeoutPlatform = new FakeLauncherPlatform([win32Target]);
        timeoutPlatform.SetRunning(win32Target, allowClose: false);
        var timeoutController = new MinecraftLauncherController(timeoutPlatform, TimeSpan.FromMilliseconds(30));
        var installStarted = false;
        try
        {
            await timeoutController.CloseBeforeInstallAsync();
            installStarted = true;
            throw new InvalidOperationException("Expected Launcher close timeout.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_CLOSE_TIMEOUT") { }
        True(!installStarted, "Launcher timeout or refusal prevents install callback");

        var bothVariantsPlatform = new FakeLauncherPlatform([storeTarget, win32Target]);
        try
        {
            await new MinecraftLauncherController(bothVariantsPlatform).CloseBeforeInstallAsync();
            throw new InvalidOperationException("Expected ambiguous Launcher target rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_TARGET_AMBIGUOUS") { }
        bothVariantsPlatform.SetRunning(storeTarget, allowClose: true);
        Equal(storeTarget, await new MinecraftLauncherController(bothVariantsPlatform).CloseBeforeInstallAsync(),
            "one active official Launcher resolves registered variants");

        var secondWin32Target = new MinecraftLauncherTarget(MinecraftLauncherKind.Win32,
            Path.Combine(tempRoot, "other", "MinecraftLauncher.exe"));
        var ambiguousPlatform = new FakeLauncherPlatform([win32Target, secondWin32Target]);
        try
        {
            await new MinecraftLauncherController(ambiguousPlatform).CloseBeforeInstallAsync();
            throw new InvalidOperationException("Expected ambiguous Launcher target rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_TARGET_AMBIGUOUS") { }

        // Simulates a matching process whose MainModule path cannot be read.
        var unknownPlatform = new FakeLauncherPlatform([win32Target]) { HasUnknownProcess = true };
        try
        {
            await new MinecraftLauncherController(unknownPlatform).CloseBeforeInstallAsync();
            throw new InvalidOperationException("Expected unknown process rejection.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_IDENTITY_UNKNOWN") { }
        True(unknownPlatform.Events.Count == 0, "unidentified process with an unavailable path blocks changes without being closed");
        try
        {
            new MinecraftLauncherController(unknownPlatform).EnsureClosed(win32Target);
            throw new InvalidOperationException("Expected the selected official target guard to reject an unidentified launcher process.");
        }
        catch (InstallerException ex) when (ex.Code == "LAUNCHER_IDENTITY_UNKNOWN") { }
        True(unknownPlatform.Events.Count == 0, "selected official target guard preserves the unidentified-process fail-closed check");

        try
        {
            await storeController.ConfigureAndStartAsync(storeTarget,
                () => Task.FromException(new InvalidOperationException("profile failed")));
            throw new InvalidOperationException("Expected profile setup failure.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "profile failed") { }
        Equal(1, storePlatform.Events.Count(eventName => eventName == "start"),
            "profile setup failure does not issue another Launcher start request");

        var failedStartPlatform = new FakeLauncherPlatform([win32Target]) { FailStart = true };
        var failedStart = await new MinecraftLauncherController(failedStartPlatform).ConfigureAndStartAsync(
            win32Target, static () => Task.CompletedTask);
        Equal(MinecraftLauncherStartStatus.Failed, failedStart.Status, "start failure is returned separately from install success");
        True(failedStart.Diagnostic?.StartsWith("LAUNCHER_START_FAILED", StringComparison.Ordinal) == true,
            "start failure has a diagnostic code");

        var unavailablePlatform = new FakeLauncherPlatform([]);
        var unavailable = await new MinecraftLauncherController(unavailablePlatform).ConfigureAndStartAsync(
            null, static () => Task.CompletedTask);
        Equal(MinecraftLauncherStartStatus.TargetUnavailable, unavailable.Status,
            "an unregistered target keeps installation successful and requests manual opening");
        True(unavailablePlatform.Events.Count == 0, "no unverified target is launched");
        Pass("Store and Win32 Launcher targets close, confirm exit, and report start outcomes using isolated fixtures");
    }

    private static async Task VerifyOfficialFabricDownloadAsync(string tempRoot)
    {
        var launcherRoot = Path.Combine(tempRoot, "official-fabric-fixture");
        Directory.CreateDirectory(launcherRoot);
        var profilesPath = Path.Combine(launcherRoot, "launcher_profiles.json");
        File.WriteAllText(profilesPath, "{\"profiles\":{\"vanilla\":{\"name\":\"Original\"}}}");
        using var service = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { });
        await service.ConfigureAsync(Path.Combine(tempRoot, "official-fabric-game"));
        using var document = JsonDocument.Parse(File.ReadAllText(profilesPath));
        True(document.RootElement.GetProperty("profiles").TryGetProperty("vanilla", out _), "official Fabric download keeps vanilla profile");
        True(document.RootElement.GetProperty("profiles").TryGetProperty(LauncherProfile.ProfileKey, out _), "official Fabric download creates MinePack profile");
        Pass("official Fabric profile ZIP downloads, verifies, and configures an isolated Launcher fixture");
    }

    private static async Task VerifyOfflineFabricProfileAsync(string tempRoot)
    {
        foreach (var minecraftVersion in new[] { "26.2", "26.3" })
        {
            var root = Path.Combine(tempRoot, "fabric-offline-" + minecraftVersion);
            Directory.CreateDirectory(root);
            var gameDirectory = Path.Combine(tempRoot, "fabric-offline-game-" + minecraftVersion);
            Directory.CreateDirectory(gameDirectory);
            var profiles = Path.Combine(root, "launcher_profiles.json");
            File.WriteAllText(profiles, "{\"profiles\":{\"vanilla\":{\"name\":\"Original\"}}}");
            var versionId = $"fabric-loader-{TestPackRelease.FabricLoaderVersion}-{minecraftVersion}";
            var version = Path.Combine(root, "versions", versionId);
            Directory.CreateDirectory(version);
            var profileJson = $"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"{minecraftVersion}\",\"time\":\"2026-09-24\",\"releaseTime\":\"2026-09-24\"}}";
            File.WriteAllText(Path.Combine(version, versionId + ".json"), profileJson);
            File.WriteAllBytes(Path.Combine(version, versionId + ".jar"), []);
            var expectedHash = HashBytes(Bytes($"{{\"id\":\"{versionId}\",\"inheritsFrom\":\"{minecraftVersion}\"}}"));
            var requests = 0;
            using var service = new FabricLauncherService(root, new DelegateHandler(_ =>
                {
                    Interlocked.Increment(ref requests);
                    throw new HttpRequestException("offline fixture must not be requested");
                }), expectedHash, new string('A', 128), 1, minecraftVersion, static () => { },
                TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));
            await service.ConfigureAsync(gameDirectory);
            Equal(0, requests, $"healthy pinned {minecraftVersion} Fabric version configures without HTTP");
            using var configured = JsonDocument.Parse(File.ReadAllText(profiles));
            Equal(versionId, configured.RootElement.GetProperty("profiles").GetProperty(LauncherProfile.ProfileKey)
                .GetProperty("lastVersionId").GetString(), $"offline Fabric profile selects the pinned {minecraftVersion} version");
        }

        foreach (var invalid in new[] { "json", "jar" })
        {
            var root = Path.Combine(tempRoot, "fabric-invalid-local-" + invalid);
            Directory.CreateDirectory(root);
            var gameDirectory = Path.Combine(tempRoot, "fabric-invalid-local-game-" + invalid);
            Directory.CreateDirectory(gameDirectory);
            var profiles = Path.Combine(root, "launcher_profiles.json");
            File.WriteAllText(profiles, "{\"profiles\":{\"vanilla\":{\"name\":\"Original\"}}}");
            const string versionId = "fabric-loader-0.19.5-26.2";
            var version = Path.Combine(root, "versions", versionId);
            Directory.CreateDirectory(version);
            var json = "{\"id\":\"fabric-loader-0.19.5-26.2\",\"inheritsFrom\":\"26.2\",\"time\":\"2026-09-24\",\"releaseTime\":\"2026-09-24\"}";
            var jsonPath = Path.Combine(version, versionId + ".json");
            var jarPath = Path.Combine(version, versionId + ".jar");
            File.WriteAllText(jsonPath, invalid == "json" ? "{broken" : json);
            File.WriteAllBytes(jarPath, invalid == "json" ? [] : Bytes("foreign client jar"));
            var before = HashBytes(File.ReadAllBytes(profiles)) + HashBytes(File.ReadAllBytes(jsonPath)) + HashBytes(File.ReadAllBytes(jarPath));
            var requests = 0;
            var expectedHash = HashBytes(Bytes("{\"id\":\"fabric-loader-0.19.5-26.2\",\"inheritsFrom\":\"26.2\"}"));
            using var service = new FabricLauncherService(root, new DelegateHandler(_ =>
                {
                    Interlocked.Increment(ref requests);
                    throw new HttpRequestException("invalid local version must not be overwritten");
                }), expectedHash, HashBytes(Bytes("expected jar")), Bytes("expected jar").Length, "26.2", static () => { },
                TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));
            try
            {
                await service.ConfigureAsync(gameDirectory);
                throw new InvalidOperationException("Expected invalid local Fabric version conflict.");
            }
            catch (InstallerException ex) when (ex.Code == "FABRIC_VERSION_CONFLICT") { }
            Equal(0, requests, $"invalid local Fabric {invalid} is rejected before network");
            Equal(before, HashBytes(File.ReadAllBytes(profiles)) + HashBytes(File.ReadAllBytes(jsonPath)) + HashBytes(File.ReadAllBytes(jarPath)),
                $"invalid local Fabric {invalid} leaves version and profile bytes unchanged");
        }

        var missingRoot = Path.Combine(tempRoot, "fabric-missing-local");
        Directory.CreateDirectory(missingRoot);
        var missingGame = Path.Combine(tempRoot, "fabric-missing-local-game");
        Directory.CreateDirectory(missingGame);
        File.WriteAllText(Path.Combine(missingRoot, "launcher_profiles.json"), "{\"profiles\":{}}");
        const string missingVersionId = "fabric-loader-0.19.5-26.2";
        var missingProfileJson = "{\"id\":\"fabric-loader-0.19.5-26.2\",\"inheritsFrom\":\"26.2\",\"time\":\"2026-09-24\",\"releaseTime\":\"2026-09-24\"}";
        var missingArchive = CreateFabricProfileArchive(missingVersionId, missingProfileJson);
        var missingExpectedHash = HashBytes(Bytes("{\"id\":\"fabric-loader-0.19.5-26.2\",\"inheritsFrom\":\"26.2\"}"));
        var missingRequests = 0;
        using (var service = new FabricLauncherService(missingRoot, new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref missingRequests);
                   return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(missingArchive) };
               }), missingExpectedHash, new string('A', 128), 1, "26.2", static () => { },
                   TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10)))
            await service.ConfigureAsync(missingGame);
        Equal(1, missingRequests, "missing Fabric version uses exactly one pinned profile request");

        var stalledRoot = Path.Combine(tempRoot, "fabric-body-stall");
        Directory.CreateDirectory(stalledRoot);
        var stalledGame = Path.Combine(tempRoot, "fabric-body-stall-game");
        Directory.CreateDirectory(stalledGame);
        var stalledProfile = Path.Combine(stalledRoot, "launcher_profiles.json");
        File.WriteAllText(stalledProfile, "{\"profiles\":{}}");
        var stalledProfileBefore = HashFile(stalledProfile);
        var stalledRequests = 0;
        using (var service = new FabricLauncherService(stalledRoot, new AsyncDelegateHandler((_, _) =>
               {
                   Interlocked.Increment(ref stalledRequests);
                   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StreamContent(new PendingReadStream())
                   });
               }), missingExpectedHash, new string('A', 128), 1, "26.2", static () => { },
                   TimeSpan.FromMilliseconds(35), TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                await service.ConfigureAsync(stalledGame);
                throw new InvalidOperationException("Expected stalled Fabric profile body timeout.");
            }
            catch (InstallerException ex) when (ex.Code == "FABRIC_TIMEOUT") { }
        }
        Equal(3, stalledRequests, "Fabric body deadline retries are bounded");
        Equal(stalledProfileBefore, HashFile(stalledProfile), "Fabric body timeout leaves Launcher profile unchanged");

        var requestTimeoutRoot = Path.Combine(tempRoot, "fabric-request-timeout");
        Directory.CreateDirectory(requestTimeoutRoot);
        var requestTimeoutGame = Path.Combine(tempRoot, "fabric-request-timeout-game");
        Directory.CreateDirectory(requestTimeoutGame);
        var requestTimeoutProfile = Path.Combine(requestTimeoutRoot, "launcher_profiles.json");
        File.WriteAllText(requestTimeoutProfile, "{\"profiles\":{}}");
        var requestTimeoutProfileBefore = HashFile(requestTimeoutProfile);
        var requestTimeoutRequests = 0;
        using (var service = new FabricLauncherService(requestTimeoutRoot, new AsyncDelegateHandler((_, _) =>
               {
                   Interlocked.Increment(ref requestTimeoutRequests);
                   return Task.FromException<HttpResponseMessage>(new TaskCanceledException("fixture request timeout", new TimeoutException()));
               }), missingExpectedHash, new string('A', 128), 1, "26.2", static () => { },
                   TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                await service.ConfigureAsync(requestTimeoutGame);
                throw new InvalidOperationException("Expected Fabric request timeout after bounded retries.");
            }
            catch (InstallerException ex) when (ex.Code == "FABRIC_TIMEOUT") { }
        }
        Equal(3, requestTimeoutRequests, "non-caller Fabric request cancellation maps to timeout and retries three times");
        Equal(requestTimeoutProfileBefore, HashFile(requestTimeoutProfile), "Fabric request timeout leaves Launcher profile unchanged");

        var callerBodyRoot = Path.Combine(tempRoot, "fabric-caller-body-cancel");
        Directory.CreateDirectory(callerBodyRoot);
        var callerBodyGame = Path.Combine(tempRoot, "fabric-caller-body-cancel-game");
        Directory.CreateDirectory(callerBodyGame);
        var callerBodyProfile = Path.Combine(callerBodyRoot, "launcher_profiles.json");
        File.WriteAllText(callerBodyProfile, "{\"profiles\":{}}");
        var callerBodyProfileBefore = HashFile(callerBodyProfile);
        var callerBodyRequests = 0;
        using (var service = new FabricLauncherService(callerBodyRoot, new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref callerBodyRequests);
                   return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PendingReadStream()) };
               }), missingExpectedHash, new string('A', 128), 1, "26.2", static () => { },
                   TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(35)))
        {
            try
            {
                await service.ConfigureAsync(callerBodyGame, cancel.Token);
                throw new InvalidOperationException("Expected caller cancellation during Fabric body read.");
            }
            catch (OperationCanceledException) { }
        }
        Equal(1, callerBodyRequests, "caller cancellation during Fabric body read is not retried");
        Equal(callerBodyProfileBefore, HashFile(callerBodyProfile), "caller-canceled Fabric body leaves Launcher profile unchanged");

        var retryCancelRoot = Path.Combine(tempRoot, "fabric-retry-cancel");
        Directory.CreateDirectory(retryCancelRoot);
        var retryCancelGame = Path.Combine(tempRoot, "fabric-retry-cancel-game");
        Directory.CreateDirectory(retryCancelGame);
        var retryCancelProfile = Path.Combine(retryCancelRoot, "launcher_profiles.json");
        File.WriteAllText(retryCancelProfile, "{\"profiles\":{}}");
        var retryCancelProfileBefore = HashFile(retryCancelProfile);
        var retryCancelRequests = 0;
        using (var service = new FabricLauncherService(retryCancelRoot, new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref retryCancelRequests);
                   var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                   response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromDays(365));
                   return response;
               }), missingExpectedHash, new string('A', 128), 1, "26.2", static () => { },
                   TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)))
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(35)))
        {
            try
            {
                await service.ConfigureAsync(retryCancelGame, cancel.Token);
                throw new InvalidOperationException("Expected caller cancellation during Fabric retry delay.");
            }
            catch (OperationCanceledException) { }
        }
        Equal(1, retryCancelRequests, "caller cancellation during Fabric retry delay prevents another request");
        Equal(retryCancelProfileBefore, HashFile(retryCancelProfile), "caller-canceled Fabric retry leaves Launcher profile unchanged");

        var declaredOversizedRoot = Path.Combine(tempRoot, "fabric-declared-oversize");
        Directory.CreateDirectory(declaredOversizedRoot);
        var declaredOversizedGame = Path.Combine(tempRoot, "fabric-declared-oversize-game");
        Directory.CreateDirectory(declaredOversizedGame);
        var declaredOversizedProfile = Path.Combine(declaredOversizedRoot, "launcher_profiles.json");
        File.WriteAllText(declaredOversizedProfile, "{\"profiles\":{}}");
        var declaredOversizedProfileBefore = HashFile(declaredOversizedProfile);
        var declaredOversizedRequests = 0;
        var declaredOversizedReads = 0;
        using (var service = new FabricLauncherService(declaredOversizedRoot, new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref declaredOversizedRequests);
                   var content = new StreamContent(new ChunkedMemoryStream(new byte[1_000_001], 16_384,
                       TimeSpan.Zero, () => Interlocked.Increment(ref declaredOversizedReads)));
                   content.Headers.ContentLength = 1_000_001;
                   return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
               }), missingExpectedHash, new string('A', 128), 1, "26.2", static () => { },
                   TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                await service.ConfigureAsync(declaredOversizedGame);
                throw new InvalidOperationException("Expected declared oversized Fabric response rejection.");
            }
            catch (InstallerException ex) when (ex.Code == "FABRIC_DOWNLOAD") { }
        }
        Equal(1, declaredOversizedRequests, "declared oversized Fabric profile rejects before retry or body read");
        Equal(0, declaredOversizedReads, "declared oversized Fabric Content-Length is rejected without reading bytes");
        Equal(declaredOversizedProfileBefore, HashFile(declaredOversizedProfile), "declared oversized Fabric response leaves Launcher profile unchanged");

        var oversizedRoot = Path.Combine(tempRoot, "fabric-chunked-oversize");
        Directory.CreateDirectory(oversizedRoot);
        var oversizedGame = Path.Combine(tempRoot, "fabric-chunked-oversize-game");
        Directory.CreateDirectory(oversizedGame);
        File.WriteAllText(Path.Combine(oversizedRoot, "launcher_profiles.json"), "{\"profiles\":{}}");
        var oversizedRequests = 0;
        using (var service = new FabricLauncherService(oversizedRoot, new DelegateHandler(_ =>
               {
                   Interlocked.Increment(ref oversizedRequests);
                   var response = new HttpResponseMessage(HttpStatusCode.OK)
                   {
                       Content = new StreamContent(new ChunkedMemoryStream(new byte[1_000_001], 16_384, TimeSpan.Zero))
                   };
                   if (response.Content.Headers.ContentLength is not null)
                       throw new InvalidOperationException("Chunked fixture unexpectedly has a Content-Length.");
                   return response;
               }), missingExpectedHash, new string('A', 128), 1, "26.2", static () => { },
                   TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10)))
        {
            try
            {
                await service.ConfigureAsync(oversizedGame);
                throw new InvalidOperationException("Expected chunked Fabric response size rejection.");
            }
            catch (InstallerException ex) when (ex.Code == "FABRIC_DOWNLOAD") { }
        }
        Equal(1, oversizedRequests, "chunked Fabric body stops at the first byte beyond the 1 MB limit");

        Pass("Fabric supports healthy offline versions, rejects local conflicts before HTTP, and bounds profile bodies");
    }

    private static async Task VerifyCurrentLauncherCopyAsync(string tempRoot)
    {
        var realRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        var names = new[] { "launcher_profiles.json", "launcher_profiles_microsoft_store.json" };
        var sourceFiles = names.Select(name => Path.Combine(realRoot, name)).Where(File.Exists).ToArray();
        if (sourceFiles.Length != 1)
        {
            Console.WriteLine("NOT RUN: current Launcher has no unambiguous supported profile file.");
            return;
        }

        var original = File.ReadAllBytes(sourceFiles[0]);
        var launcherRoot = Path.Combine(tempRoot, "current-launcher-copy");
        Directory.CreateDirectory(launcherRoot);
        var copiedFile = Path.Combine(launcherRoot, Path.GetFileName(sourceFiles[0]));
        File.WriteAllBytes(copiedFile, original);
        const string versionId = "fabric-loader-0.19.5-26.2";
        var sourceVersion = Path.Combine(realRoot, "versions", versionId);
        var copiedVersion = Path.Combine(launcherRoot, "versions", versionId);
        var copiedJar = Path.Combine(copiedVersion, versionId + ".jar");
        if (File.Exists(Path.Combine(sourceVersion, versionId + ".json")) &&
            File.Exists(Path.Combine(sourceVersion, versionId + ".jar")))
        {
            Directory.CreateDirectory(copiedVersion);
            File.Copy(Path.Combine(sourceVersion, versionId + ".json"), Path.Combine(copiedVersion, versionId + ".json"));
            File.Copy(Path.Combine(sourceVersion, versionId + ".jar"), copiedJar);
        }
        var copiedJarHash = File.Exists(copiedJar) ? HashFile(copiedJar) : null;
        using var service = new FabricLauncherService(launcherRoot, ensureLauncherClosed: static () => { });
        await service.ConfigureAsync(Path.Combine(tempRoot, "current-launcher-copy-game"));
        if (copiedJarHash is not null)
            Equal(copiedJarHash, HashFile(copiedJar), "Launcher-hydrated Fabric JAR survives reconfiguration in a copy");
        var before = JsonNode.Parse(original)!.AsObject();
        var after = JsonNode.Parse(File.ReadAllBytes(copiedFile))!.AsObject();
        before["profiles"]?.AsObject().Remove(LauncherProfile.ProfileKey);
        after["profiles"]?.AsObject().Remove(LauncherProfile.ProfileKey);
        True(JsonNode.DeepEquals(before, after), "current Launcher settings and other profiles stay unchanged in a copy");
        True(File.ReadAllBytes(sourceFiles[0]).AsSpan().SequenceEqual(original), "actual Launcher profile file stays untouched");
        Pass("current Launcher profile format accepts an isolated MinePack profile in a temporary copy");
    }

    private static byte[] CreateFabricProfileArchive(string versionId, string json)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry($"{versionId}/{versionId}.json").Open()))
                writer.Write(json);
            zip.CreateEntry($"{versionId}/{versionId}.jar");
        }
        return memory.ToArray();
    }

    private static string CreateReleaseFixture(string tempRoot, PackArchive pack, bool legacy, out string instance)
    {
        var root = Path.Combine(tempRoot, "release-fixture-" + Guid.NewGuid().ToString("N"));
        instance = Path.Combine(root, "instances", "fixture");
        var files = pack.Files.Select(file => new ManagedFile(file.Path, file.Sha512,
                file.Downloads.Select(uri => uri.AbsoluteUri).ToArray(), false, Math.Max(0, file.Size)))
            .Concat(pack.Overrides.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path))
                .Select(file => new ManagedFile(file.Path, file.Sha512, [], true, file.Size)))
            .ToList();
        var iris = pack.Overrides.SingleOrDefault(file => file.Path.Equals("config/iris.properties", StringComparison.OrdinalIgnoreCase));
        if (legacy && iris is not null)
            files.Add(new ManagedFile(iris.Path, iris.Sha512, [], true, iris.Size));
        new InstallationManifest
        {
            PackVersion = pack.VersionId,
            MinecraftVersion = pack.MinecraftVersion,
            FabricLoaderVersion = pack.FabricLoaderVersion,
            PackArchiveSha512 = pack.ArchiveSha512,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList()
        }.SaveAtomic(instance);
        File.WriteAllText(Path.Combine(root, ".minepack-active.json"),
            JsonSerializer.Serialize(new { SchemaVersion = 1, InstanceDirectory = "instances/fixture" }));
        return root;
    }

    private static string CreatePinnedReleaseFixture(string tempRoot, PackArchive pack, out string instance)
    {
        var root = Path.Combine(tempRoot, "pinned-release-fixture-" + Guid.NewGuid().ToString("N"));
        instance = InstancePath(root, pack.VersionId, pack.ArchiveSha512);
        var files = pack.Files.Select(file => new ManagedFile(file.Path, file.Sha512,
                file.Downloads.Select(uri => uri.AbsoluteUri).ToArray(), false, Math.Max(0, file.Size)))
            .Concat(pack.Overrides.Where(file => !InitialConfiguration.IsInitialUserConfig(pack, file.Path))
                .Select(file => new ManagedFile(file.Path, file.Sha512, [], true, file.Size)))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        new InstallationManifest
        {
            PackVersion = pack.VersionId,
            MinecraftVersion = pack.MinecraftVersion,
            FabricLoaderVersion = pack.FabricLoaderVersion,
            PackArchiveSha512 = pack.ArchiveSha512,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = files
        }.SaveAtomic(instance);
        File.WriteAllText(Path.Combine(root, ".minepack-active.json"),
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                InstanceDirectory = Path.GetRelativePath(root, instance).Replace('\\', '/')
            }));
        return root;
    }

    private static string FixturePath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void RejectReleaseManifest(InstallService installer, string root, string instance,
        string packPath, string packHash, string scenario)
    {
        try
        {
            _ = installer.ValidateUninstallTarget(root, instance, packPath, packHash);
            throw new InvalidOperationException($"Expected {scenario} to be rejected.");
        }
        catch (InstallerException ex) when (ex.Code == "MANIFEST_INVALID") { }
        Pass(scenario);
    }

    private static string CreatePack(string path, string version, IReadOnlyList<TestFile> files,
        IReadOnlyList<TestOverride>? overrides = null, string minecraftVersion = "26.3", string fabricLoaderVersion = "0.19.5")
    {
        overrides ??= [];
        var indexFiles = files.Select(file =>
        {
            var item = new Dictionary<string, object?>
            {
                ["path"] = file.Path,
                ["hashes"] = new { sha512 = file.InvalidHash ? "bad" : HashBytes(file.Bytes) },
                ["downloads"] = new[] { file.Url },
                ["env"] = new { client = "required", server = "required" }
            };
            if (!file.OmitSize) item["fileSize"] = file.Bytes.LongLength;
            return item;
        });
        var index = new
        {
            formatVersion = 1,
            game = "minecraft",
            versionId = version,
            name = "Smoke Fixture",
            summary = "Temporary smoke-test pack",
            files = indexFiles,
            dependencies = new Dictionary<string, string> { ["minecraft"] = minecraftVersion, ["fabric-loader"] = fabricLoaderVersion }
        };

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write(JsonSerializer.Serialize(index));
        foreach (var item in overrides)
        {
            var entry = zip.CreateEntry("overrides/" + item.Path, CompressionLevel.NoCompression);
            using var output = entry.Open();
            output.Write(item.Bytes);
        }
        return path;
    }

    private static byte[] Bytes(string value) => System.Text.Encoding.UTF8.GetBytes(value);
    private static string HashBytes(byte[] value) => Convert.ToHexString(SHA512.HashData(value));
    private static string HashFile(string path) => Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path)));
    private static string InstancePath(string root, string version, string archiveSha512) =>
        Path.Combine(root, "instances", $"test-pack-{version}-{archiveSha512[..12].ToLowerInvariant()}");

    private static void True(bool value, string scenario)
    {
        if (!value) throw new InvalidOperationException($"Failed: {scenario}");
    }

    private static void Equal<T>(T expected, T? actual, string scenario)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Failed: {scenario}; expected '{expected}', received '{actual}'.");
    }

    private static void Pass(string scenario) => Console.WriteLine($"PASS: {scenario}");

    private sealed record TestFile(string Path, byte[] Bytes, string Url = "https://cdn.modrinth.com/data/test/version/test.jar", bool InvalidHash = false, bool OmitSize = false);
    private sealed record TestOverride(string Path, byte[] Bytes);

    private sealed class FakeLauncherPlatform(IReadOnlyList<MinecraftLauncherTarget> targets) : IMinecraftLauncherPlatform
    {
        private readonly Dictionary<MinecraftLauncherTarget, FakeLauncherProcessState> _running = [];
        public List<string> Events { get; } = [];
        public bool HasUnknownProcess { get; set; }
        public bool FailStart { get; set; }
        public string? StartFailureMessage { get; set; }

        public IReadOnlyList<MinecraftLauncherTarget> FindTargets() => targets;

        public IReadOnlyList<IMinecraftLauncherProcess> FindRunningProcesses(MinecraftLauncherTarget target) =>
            _running.TryGetValue(target, out var state) && !state.HasExited
                ? [new FakeLauncherProcess(state)]
                : [];

        public bool HasUnidentifiedLauncherProcess() => HasUnknownProcess;

        public void Start(MinecraftLauncherTarget target)
        {
            Events.Add("start");
            if (FailStart) throw new InvalidOperationException(StartFailureMessage ?? "fixture launch failure");
        }

        public void SetRunning(MinecraftLauncherTarget target, bool allowClose) =>
            _running[target] = new FakeLauncherProcessState(Events, allowClose);
    }

    private sealed class FakeLauncherProcessState(List<string> events, bool allowClose)
    {
        public bool HasExited { get; set; }
        public bool CloseRequested { get; private set; }

        public bool RequestClose()
        {
            events.Add("close");
            CloseRequested = allowClose;
            return allowClose;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            events.Add("wait");
            if (!CloseRequested) return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            HasExited = true;
            events.Add("exit");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLauncherProcess(FakeLauncherProcessState state) : IMinecraftLauncherProcess
    {
        public bool HasExited => state.HasExited;
        public bool RequestClose() => state.RequestClose();
        public Task WaitForExitAsync(CancellationToken cancellationToken) => state.WaitForExitAsync(cancellationToken);
        public void Dispose() { }
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class AsyncDelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private sealed class PendingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WaitForCancellationAsync(cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(WaitForCancellationAsync(cancellationToken));

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class PendingOpenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new MemoryStream();
        }
    }

    private sealed class LateOpenContent(Task<Stream> streamTask) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => streamTask;
    }

    private sealed class TrackingDisposeStream(Action onDispose) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) onDispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ChunkedMemoryStream(byte[] bytes, int chunkSize, TimeSpan delay, Action? onRead = null) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadChunkAsync(buffer.AsMemory(offset, count), cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(ReadChunkAsync(buffer, cancellationToken));

        private async Task<int> ReadChunkAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            onRead?.Invoke();
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), bytes.Length - _position);
            if (count > 0)
            {
                bytes.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
            }
            return count;
        }
    }

    private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        private readonly AutoResetEvent _callbackReady = new(false);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_callbacks) _callbacks.Enqueue((callback, state));
            _callbackReady.Set();
        }

        public void RunUntilComplete(Task task)
        {
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException("The smoke synchronization context must be pumped by its owner thread.");
            while (!task.IsCompleted)
            {
                (SendOrPostCallback Callback, object? State) item;
                lock (_callbacks)
                    item = _callbacks.Count > 0 ? _callbacks.Dequeue() : default;
                if (item.Callback is null)
                {
                    _callbackReady.WaitOne(100);
                    continue;
                }
                item.Callback(item.State);
            }
            task.GetAwaiter().GetResult();
        }

        public void Dispose() => _callbackReady.Dispose();
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) { }
    }
}
