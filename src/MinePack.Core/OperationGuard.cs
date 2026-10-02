using System.Security.Principal;

namespace MinePack.Core;

public static class OperationGuard
{
    private static readonly AsyncLocal<Lease?> CurrentLease = new();

    public static async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await RunAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (CurrentLease.Value is { IsActive: true })
            return await operation().ConfigureAwait(false);

        var lease = await AcquireAsync(cancellationToken);
        var previous = CurrentLease.Value;
        CurrentLease.Value = lease;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            CurrentLease.Value = previous;
            await lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static void Run(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Run(() =>
        {
            operation();
            return true;
        });
    }

    public static T Run<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (CurrentLease.Value is { IsActive: true }) return operation();

        var lease = AcquireAsync(CancellationToken.None).GetAwaiter().GetResult();
        var previous = CurrentLease.Value;
        CurrentLease.Value = lease;
        try { return operation(); }
        finally
        {
            CurrentLease.Value = previous;
            lease.Dispose();
        }
    }

    private static Task<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string mutexName;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The operation gate requires Windows.");
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
            mutexName = $"Global\\MinePackInstaller.Operation.{sid}";
        }
        catch (Exception ex)
        {
            return Task.FromException<Lease>(new InstallerException("OPERATION_GUARD_UNAVAILABLE",
                LocalizedText.Get("OperationGuardUnavailable"), ex));
        }
        var acquired = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Thread(() =>
        {
            Mutex? mutex = null;
            var ownsMutex = false;
            ManualResetEventSlim? release = null;
            TaskCompletionSource? released = null;
            try
            {
                mutex = new Mutex(false, mutexName);
                try { ownsMutex = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { ownsMutex = true; }
                if (!ownsMutex)
                {
                    acquired.TrySetException(new InstallerException("OPERATION_BUSY", LocalizedText.Get("OperationBusy")));
                    return;
                }

                release = new ManualResetEventSlim();
                released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                acquired.TrySetResult(new Lease(release, released.Task));
                release.Wait();
                mutex.ReleaseMutex();
                ownsMutex = false;
                released.TrySetResult();
            }
            catch (Exception ex)
            {
                var failure = new InstallerException("OPERATION_GUARD_UNAVAILABLE",
                    LocalizedText.Get("OperationGuardUnavailable"), ex);
                acquired.TrySetException(failure);
                released?.TrySetException(failure);
            }
            finally
            {
                if (ownsMutex)
                {
                    try { mutex?.ReleaseMutex(); }
                    catch (ApplicationException) { }
                }
                release?.Dispose();
                mutex?.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "MinePack operation gate owner"
        };
        try { owner.Start(); }
        catch (Exception ex)
        {
            acquired.TrySetException(new InstallerException("OPERATION_GUARD_UNAVAILABLE",
                LocalizedText.Get("OperationGuardUnavailable"), ex));
        }
        return acquired.Task;
    }

    private sealed class Lease(ManualResetEventSlim release, Task released) : IAsyncDisposable
    {
        private int _active = 1;
        private int _disposed;

        public bool IsActive => Volatile.Read(ref _active) != 0;

        public void Dispose() => BeginDispose().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync() => await BeginDispose().ConfigureAwait(false);

        private Task BeginDispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return Task.CompletedTask;
            Interlocked.Exchange(ref _active, 0);
            release.Set();
            return released;
        }
    }
}
