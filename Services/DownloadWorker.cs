using System.Net;
using hfparquet.Models;

namespace hfparquet.Services;

public sealed class DownloadWorker(
    DownloadManager manager,
    DownloadFileStore fileStore,
    IHttpClientFactory httpClientFactory,
    ILogger<DownloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DownloadJobState job;
            try { job = await manager.DequeueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
            var token = linked.Token;
            manager.SetJobStatus(job, "Downloading");

            try
            {
                foreach (var item in manager.GetWorkItems(job))
                {
                    if (token.IsCancellationRequested) break;
                    manager.SetFileStatus(job, item.File.Id, "Downloading");
                    try
                    {
                        var bytes = await DownloadOneAsync(job, item.File, token);
                        manager.MarkFileCompleted(job, item.File.Id, bytes);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        manager.SetFileStatus(job, item.File.Id, "Cancelled", "Download cancelled.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Download failed for {RepoId}/{Filename}", job.RepoId, item.File.Filename);
                        manager.SetFileStatus(job, item.File.Id, "Failed", SafeError(ex));
                    }
                }

                if (token.IsCancellationRequested) manager.MarkUnstartedCancelled(job);
                var snapshot = manager.Snapshot(job);
                var completed = snapshot.Files.Count(file => file.Status == "Completed");
                var failed = snapshot.Files.Count(file => file.Status == "Failed");
                var cancelled = snapshot.Files.Count(file => file.Status == "Cancelled");
                manager.SetJobStatus(job, completed == snapshot.Files.Count ? "Completed"
                    : cancelled > 0 && failed == 0 ? "Cancelled"
                    : completed > 0 || failed > 0 ? "CompletedWithErrors" : "Failed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Download job {JobId} failed unexpectedly", job.Id);
                manager.SetJobStatus(job, "Failed", SafeError(ex));
            }
        }
    }

    private async Task<long> DownloadOneAsync(DownloadJobState job, ParquetFileDto file, CancellationToken cancellationToken)
    {
        if (!DownloadFileStore.IsAllowedHubUrl(file.Url)) throw new InvalidDataException("The Hub returned a non-HTTPS or non-Hugging Face file URL.");
        var destination = fileStore.GetFinalPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (fileStore.IsDownloaded(file)) return new FileInfo(destination).Length;

        var partial = destination + ".part";
        var client = httpClientFactory.CreateClient("HuggingFaceFiles");
        var currentUri = new Uri(file.Url, UriKind.Absolute);
        HttpResponseMessage? response = null;

        try
        {
            for (var redirect = 0; redirect <= 8; redirect++)
            {
                if (!DownloadFileStore.IsAllowedDownloadHost(currentUri)) throw new InvalidDataException("The Hugging Face download redirected to an untrusted host.");
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                request.Headers.UserAgent.ParseAdd("HuggingFaceParquetBrowser/1.0");
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    response.Dispose();
                    response = null;
                    if (location is null || redirect == 8) throw new HttpRequestException("The Hugging Face download returned too many or invalid redirects.");
                    currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                    continue;
                }
                break;
            }

            if (response is null) throw new HttpRequestException("The Hugging Face file server did not return a response.");
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"The file server returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            var totalBytes = response.Content.Headers.ContentLength ?? file.Size;
            manager.SetFileStatus(job, file.Id, "Downloading", totalBytes: totalBytes);

            long received;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                received = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    manager.SetFileProgress(job, file.Id, received, totalBytes);
                }
                await output.FlushAsync(cancellationToken);
            }

            if (file.Size is >= 0 && received != file.Size.Value)
                throw new InvalidDataException($"The downloaded length ({received:N0} bytes) did not match the Hub's expected size ({file.Size.Value:N0} bytes). The incomplete file was discarded.");

            await ReplacePartialAsync(partial, destination, cancellationToken);
            return received;
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch (IOException) { }
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static async Task ReplacePartialAsync(string partial, string destination, CancellationToken cancellationToken)
    {
        const int attempts = 8;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Move(partial, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }
    }

    private static bool IsRedirect(HttpStatusCode code) => code is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
        or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static string SafeError(Exception exception)
    {
        var message = exception.Message;
        return message.Length <= 300 ? message : message[..300] + "…";
    }
}
