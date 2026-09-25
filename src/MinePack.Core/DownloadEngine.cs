using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace MinePack.Core;

public sealed class DownloadEngine : IDisposable
{
    private const int MaxAttempts = 3;
    private const int MaxRedirects = 5;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _slots = new(5, 5);

    public DownloadEngine(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler { AllowAutoRedirect = false };
        _client = new HttpClient(handler, disposeHandler: true);
    }

    public event Action<string, object?>? Diagnostic;

    public async Task<string> DownloadVerifiedAsync(PackFile file, string stagingRoot,
        Action<long, long?>? progress, CancellationToken cancellationToken)
    {
        if (!PackArchive.IsSha512(file.Sha512)) throw new InstallerException("PACK_INVALID_HASH", LocalizedText.Get("IndexSha512Invalid"));
        await _slots.WaitAsync(cancellationToken);
        try
        {
            var destination = SafePath.Resolve(stagingRoot, file.Path);
            SafePath.EnsureNoReparsePoints(stagingRoot, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            InstallerException? lastError = null;
            foreach (var source in file.Downloads)
            {
                if (!PackArchive.IsAllowedDownloadUri(source))
                    throw new InstallerException("DOWNLOAD_URL_BLOCKED", LocalizedText.Get("DownloadSourceBlocked"));
                for (var attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Report("download_attempt", new { path = file.Path, source = Origin(source), attempt = attempt + 1 });
                    var partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
                    try
                    {
                        await DownloadToFileAsync(source, partial, file, progress, cancellationToken);
                        SafePath.EnsureNoReparsePoints(stagingRoot, destination);
                        File.Move(partial, destination);
                        Report("download_complete", new { path = file.Path, source = Origin(source), size = new FileInfo(destination).Length });
                        return destination;
                    }
                    catch (DownloadFailure ex)
                    {
                        lastError = new InstallerException(ex.Code, ex.Message, ex);
                        TryDelete(partial);
                        Report(ex.Retryable && attempt + 1 < MaxAttempts ? "download_retry" : "download_source_failed",
                            new { path = file.Path, source = Origin(source), attempt = attempt + 1, ex.Code, retryAfterMs = (long?)ex.RetryAfter?.TotalMilliseconds });
                        if (!ex.Retryable || attempt + 1 == MaxAttempts) break;
                        await Task.Delay(ex.RetryAfter ?? TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken);
                    }
                    catch (HttpRequestException ex)
                    {
                        lastError = new InstallerException("DOWNLOAD_NETWORK", LocalizedText.Get("DownloadNetworkFailed"), ex);
                        TryDelete(partial);
                        Report(attempt + 1 < MaxAttempts ? "download_retry" : "download_source_failed",
                            new { path = file.Path, source = Origin(source), attempt = attempt + 1, code = "DOWNLOAD_NETWORK", errorType = ex.GetType().Name });
                        if (attempt + 1 == MaxAttempts) break;
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken);
                    }
                    catch (InstallerException ex) when (ex.Code is "DOWNLOAD_HASH_MISMATCH" or "DOWNLOAD_SIZE_MISMATCH")
                    {
                        lastError = ex;
                        TryDelete(partial);
                        Report(attempt + 1 < MaxAttempts ? "download_retry" : "download_source_failed",
                            new { path = file.Path, source = Origin(source), attempt = attempt + 1, ex.Code });
                        if (attempt + 1 == MaxAttempts) break;
                        await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken);
                    }
                    catch (InstallerException ex) when (ex.Code == "DOWNLOAD_HTTP")
                    {
                        lastError = ex;
                        TryDelete(partial);
                        Report("download_source_failed", new { path = file.Path, source = Origin(source), code = ex.Code });
                        break;
                    }
                    catch
                    {
                        TryDelete(partial);
                        throw;
                    }
                    TryDelete(partial);
                }
            }
            var failure = lastError ?? new InstallerException("DOWNLOAD_FAILED", LocalizedText.Get("DownloadFailed"));
            Report("download_failed", new { path = file.Path, code = failure.Code });
            throw failure;
        }
        finally { _slots.Release(); }
    }

    private async Task DownloadToFileAsync(Uri source, string partial, PackFile file,
        Action<long, long?>? progress, CancellationToken cancellationToken)
    {
        var uri = source;
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            if (!PackArchive.IsAllowedDownloadUri(uri))
                throw new InstallerException("DOWNLOAD_REDIRECT_BLOCKED", LocalizedText.Get("DownloadRedirectBlocked"));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            Report("download_response", new { source = Origin(uri), statusCode = (int)response.StatusCode });
            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects || response.Headers.Location is null)
                    throw new InstallerException("DOWNLOAD_REDIRECT_BLOCKED", LocalizedText.Get("DownloadRedirectInvalid"));
                var previousOrigin = Origin(uri);
                uri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                if (!PackArchive.IsAllowedDownloadUri(uri))
                    throw new InstallerException("DOWNLOAD_REDIRECT_BLOCKED", LocalizedText.Get("DownloadRedirectBlocked"));
                Report("download_redirect", new { source = previousOrigin, destination = Origin(uri) });
                continue;
            }
            if (IsRetryableStatus(response.StatusCode))
                throw new DownloadFailure("DOWNLOAD_HTTP", LocalizedText.Get("HttpTemporaryFailure", (int)response.StatusCode), true, RetryAfter(response));
            if (!response.IsSuccessStatusCode)
                throw new InstallerException("DOWNLOAD_HTTP", LocalizedText.Get("HttpFailure", (int)response.StatusCode));

            var expectedSize = response.Content.Headers.ContentLength;
            if (file.Size >= 0 && expectedSize.HasValue && expectedSize.Value != file.Size)
                throw new InstallerException("DOWNLOAD_SIZE_MISMATCH", LocalizedText.Get("DownloadSizeMismatch"));
            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var count = await sourceStream.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                hash.AppendData(buffer, 0, count);
                total += count;
                progress?.Invoke(total, expectedSize);
                if (file.Size >= 0 && total > file.Size)
                    throw new InstallerException("DOWNLOAD_SIZE_MISMATCH", LocalizedText.Get("DownloadTooLarge"));
            }
            await output.FlushAsync(cancellationToken);
            if (file.Size >= 0 && total != file.Size)
                throw new InstallerException("DOWNLOAD_SIZE_MISMATCH", LocalizedText.Get("DownloadTooShort"));
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!PackArchive.FixedTimeHashEquals(actual, file.Sha512))
            {
                Report("download_hash_mismatch", new { path = file.Path, expected = file.Sha512, actual });
                throw new InstallerException("DOWNLOAD_HASH_MISMATCH", LocalizedText.Get("DownloadHashMismatch"));
            }
            Report("download_hash_verified", new { path = file.Path, sha512 = actual, size = total });
            return;
        }
        throw new InstallerException("DOWNLOAD_REDIRECT_BLOCKED", LocalizedText.Get("DownloadRedirectLimit"));
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool IsRetryableStatus(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retry?.Date is { } date) return date <= DateTimeOffset.UtcNow ? TimeSpan.Zero : date - DateTimeOffset.UtcNow;
        return null;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    private void Report(string eventName, object detail)
    {
        try { Diagnostic?.Invoke(eventName, detail); }
        catch { }
    }

    public void Dispose()
    {
        _client.Dispose();
        _slots.Dispose();
    }

    private sealed class DownloadFailure(string code, string message, bool retryable, TimeSpan? retryAfter)
        : Exception(message)
    {
        public string Code { get; } = code;
        public bool Retryable { get; } = retryable;
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }
}
