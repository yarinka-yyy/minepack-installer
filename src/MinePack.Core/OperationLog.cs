using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;

namespace MinePack.Core;

public sealed class OperationLog
{
    private sealed record PendingEvent(string Stage, string Outcome, string EventName, object? Details);

    private readonly object _sync = new();
    private readonly List<PendingEvent> _pending = [];
    private string? _path;
    private string? _targetPath;
    private string? _packVersion;
    private string? _minecraftVersion;
    private string? _fabricLoaderVersion;
    private string? _packArchiveSha512;
    private bool _unavailable;

    public OperationLog(string operationType, string? installerVersion, string? targetPath = null,
        string? packVersion = null, string? minecraftVersion = null, string? fabricLoaderVersion = null,
        string? packArchiveSha512 = null)
    {
        OperationType = operationType;
        InstallerVersion = installerVersion;
        _targetPath = Canonicalize(targetPath);
        _packVersion = packVersion;
        _minecraftVersion = minecraftVersion;
        _fabricLoaderVersion = fabricLoaderVersion;
        _packArchiveSha512 = packArchiveSha512;
        OperationId = Guid.NewGuid().ToString("N");
    }

    public string OperationId { get; }
    public string OperationType { get; }
    public string? InstallerVersion { get; }

    public string? CurrentLogPath
    {
        get { lock (_sync) return _unavailable ? null : _path; }
    }

    public bool IsAvailable => CurrentLogPath is not null;

    public void SetRelease(string? packVersion, string? minecraftVersion, string? fabricLoaderVersion,
        string? packArchiveSha512)
    {
        lock (_sync)
        {
            _packVersion = packVersion;
            _minecraftVersion = minecraftVersion;
            _fabricLoaderVersion = fabricLoaderVersion;
            _packArchiveSha512 = packArchiveSha512;
        }
    }

    internal void BindValidatedRoot(string root, string? targetPath = null)
    {
        lock (_sync)
        {
            if (_unavailable) return;
            try
            {
                var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                if (_path is not null)
                {
                    var logDirectory = Path.GetDirectoryName(_path);
                    var boundRoot = logDirectory is null ? null : Path.GetDirectoryName(logDirectory);
                    if (boundRoot is null || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundRoot))
                            .Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        _unavailable = true;
                        _path = null;
                        _pending.Clear();
                        return;
                    }

                    _targetPath = Canonicalize(targetPath) ?? _targetPath ?? fullRoot;
                    return;
                }

                var directory = Path.Combine(fullRoot, "logs");
                SafePath.EnsureNoReparsePoints(fullRoot, directory);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"minepack-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{OperationId}.jsonl");
                SafePath.EnsureNoReparsePoints(fullRoot, path);
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                _path = path;
                _targetPath = Canonicalize(targetPath) ?? _targetPath ?? fullRoot;
                AppendUnsafe(new PendingEvent("preflight", "started", "operation_started", null));
                foreach (var item in _pending) AppendUnsafe(item);
                _pending.Clear();
            }
            catch
            {
                _unavailable = true;
                _path = null;
                _pending.Clear();
            }
        }
    }

    public void Write(string stage, string outcome, string eventName, object? details = null)
    {
        lock (_sync)
        {
            if (_unavailable) return;
            var item = new PendingEvent(stage, outcome, eventName, details);
            if (_path is null)
            {
                if (_pending.Count < 64) _pending.Add(item);
                return;
            }
            try { AppendUnsafe(item); }
            catch
            {
                _unavailable = true;
                _path = null;
            }
        }
    }

    public void WriteException(string stage, string eventName, Exception exception, string outcome = "failed")
    {
        var installer = exception as InstallerException;
        var win32 = exception as Win32Exception ?? exception.InnerException as Win32Exception;
        var http = exception as HttpRequestException ?? exception.InnerException as HttpRequestException;
        Write(stage, outcome, eventName, new
        {
            exceptionType = exception.GetType().Name,
            code = installer?.Code,
            innerType = exception.InnerException?.GetType().Name,
            hResult = exception.HResult,
            innerHResult = exception.InnerException?.HResult,
            win32Code = win32?.NativeErrorCode,
            httpStatus = http?.StatusCode is { } status ? (int)status : (int?)null
        });
    }

    public void Complete(string outcome, string? code = null) =>
        Write("end", outcome, "operation_end", code is null ? null : new { code });

    public bool TryExportCurrent(string destination)
    {
        lock (_sync)
        {
            if (_unavailable || _path is null) return false;
            try
            {
                File.Copy(_path, destination, overwrite: true);
                return true;
            }
            catch { return false; }
        }
    }

    internal void Event(string eventName, object? details = null)
    {
        var (stage, outcome) = eventName switch
        {
            "install_started" or "pack_validated" => ("preflight", "passed"),
            "download_attempt" or "download_response" or "download_redirect" or "download_retry" => ("download", "progress"),
            "download_source_failed" or "download_failed" => ("download", "failed"),
            "download_hash_mismatch" => ("verify", "failed"),
            "download_hash_verified" or "download_verified" or "staging_verified" or
                "release_metadata_verified" => ("verify", "passed"),
            "managed_file_check" => ("verify", "checked"),
            "download_complete" or "override_applied" => ("prepare", "progress"),
            "install_committed" or "install_reactivated" or "repair_complete" or "uninstall_complete" => ("commit", "completed"),
            "repair_failed" or "install_failed" or "uninstall_failed" => ("end", "failed"),
            "repair_cancelled" or "install_cancelled" => ("end", "cancelled"),
            "transaction_cleanup_pending" => ("recovery", "pending"),
            "repair_file" or "repair_default" => ("prepare", "completed"),
            _ => ("operation", "info")
        };
        Write(stage, outcome, eventName, details);
    }

    private void AppendUnsafe(PendingEvent item)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            operationId = OperationId,
            operationType = OperationType,
            installerVersion = InstallerVersion,
            packVersion = _packVersion,
            minecraftVersion = _minecraftVersion,
            fabricLoaderVersion = _fabricLoaderVersion,
            packArchiveSha512 = _packArchiveSha512,
            targetPath = _targetPath,
            stage = item.Stage,
            outcome = item.Outcome,
            eventName = item.EventName,
            details = item.Details
        });
        File.AppendAllText(_path!, line + Environment.NewLine);
    }

    private static string? Canonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return null; }
    }
}

public sealed class InstallProgressProjector
{
    private string? _phase;
    private int _totalFiles;
    private int _completedFiles;

    public InstallProgressView Update(string phase, InstallProgress progress)
    {
        var total = Math.Max(0, progress.TotalFiles);
        var reset = !string.Equals(_phase, phase, StringComparison.Ordinal) || total != _totalFiles;
        if (reset)
        {
            _phase = phase;
            _totalFiles = total;
            _completedFiles = 0;
        }
        _completedFiles = Math.Max(_completedFiles, Math.Clamp(progress.CompletedFiles, 0, total));
        return new InstallProgressView(phase, _completedFiles, _totalFiles, _totalFiles == 0,
            progress.BytesReceived, progress.ExpectedBytes, reset);
    }
}

public sealed record InstallProgressView(string Phase, int CompletedFiles, int TotalFiles,
    bool IsIndeterminate, long BytesReceived, long? ExpectedBytes, bool PhaseChanged);
