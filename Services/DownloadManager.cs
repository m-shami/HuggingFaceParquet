using System.Collections.Concurrent;
using System.Threading.Channels;
using hfparquet.Models;

namespace hfparquet.Services;

public sealed class DownloadManager
{
    private readonly ConcurrentDictionary<string, DownloadJobState> _jobs = new(StringComparer.Ordinal);
    private readonly Channel<DownloadJobState> _queue = Channel.CreateUnbounded<DownloadJobState>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public async Task<DownloadJobSnapshot> StartAsync(string repoId, IEnumerable<ParquetFileDto> files, CancellationToken cancellationToken)
    {
        PruneOldJobs();
        var uniqueFiles = files.GroupBy(file => file.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
        if (uniqueFiles.Count == 0) throw new ArgumentException("Choose at least one Parquet file.", nameof(files));

        var state = new DownloadJobState(Guid.NewGuid().ToString("N"), repoId, uniqueFiles);
        if (!_jobs.TryAdd(state.Id, state)) throw new InvalidOperationException("Could not create the download job.");
        try
        {
            await _queue.Writer.WriteAsync(state, cancellationToken);
            return Snapshot(state);
        }
        catch
        {
            _jobs.TryRemove(state.Id, out _);
            state.Cancellation.Dispose();
            throw;
        }
    }

    public DownloadJobSnapshot? GetSnapshot(string id) => _jobs.TryGetValue(id, out var state) ? Snapshot(state) : null;

    public bool Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var state)) return false;
        lock (state.Sync)
        {
            if (IsTerminal(state.Status)) return false;
            state.Cancellation.Cancel();
            return true;
        }
    }

    public async Task<DownloadJobSnapshot?> RetryAsync(string id, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(id, out var state)) return null;
        List<ParquetFileDto> retryFiles;
        lock (state.Sync)
        {
            if (!IsTerminal(state.Status)) return null;
            retryFiles = state.Files.Where(item => item.Status != "Completed").Select(item => item.File).ToList();
        }
        return retryFiles.Count == 0 ? null : await StartAsync(state.RepoId, retryFiles, cancellationToken);
    }

    internal ValueTask<DownloadJobState> DequeueAsync(CancellationToken cancellationToken) => _queue.Reader.ReadAsync(cancellationToken);

    internal IReadOnlyList<DownloadFileState> GetWorkItems(DownloadJobState state) => state.Files;

    internal void SetJobStatus(DownloadJobState state, string status, string? error = null)
    {
        lock (state.Sync)
        {
            state.Status = status;
            state.Error = error;
        }
    }

    internal void SetFileStatus(DownloadJobState state, string fileId, string status, string? error = null, long? totalBytes = null)
    {
        lock (state.Sync)
        {
            var item = state.Files.FirstOrDefault(file => file.File.Id == fileId);
            if (item is null) return;
            item.Status = status;
            item.Error = error;
            if (totalBytes is >= 0) item.TotalBytes = totalBytes;
        }
    }

    internal void SetFileProgress(DownloadJobState state, string fileId, long receivedBytes, long? totalBytes)
    {
        lock (state.Sync)
        {
            var item = state.Files.FirstOrDefault(file => file.File.Id == fileId);
            if (item is null) return;
            item.BytesReceived = Math.Max(0, receivedBytes);
            if (totalBytes is >= 0) item.TotalBytes = totalBytes;
        }
    }

    internal void MarkFileCompleted(DownloadJobState state, string fileId, long receivedBytes)
    {
        lock (state.Sync)
        {
            var item = state.Files.FirstOrDefault(file => file.File.Id == fileId);
            if (item is null) return;
            item.BytesReceived = Math.Max(0, receivedBytes);
            item.TotalBytes ??= receivedBytes;
            item.Status = "Completed";
            item.Error = null;
        }
    }

    internal void MarkUnstartedCancelled(DownloadJobState state)
    {
        lock (state.Sync)
        {
            foreach (var item in state.Files.Where(file => file.Status == "Queued")) item.Status = "Cancelled";
        }
    }

    internal DownloadJobSnapshot Snapshot(DownloadJobState state)
    {
        lock (state.Sync)
        {
            var hasAllTotals = state.Files.All(item => item.TotalBytes.HasValue);
            var total = hasAllTotals ? state.Files.Sum(item => item.TotalBytes!.Value) : (long?)null;
            var received = state.Files.Sum(item => item.BytesReceived);
            var allDone = state.Files.All(item => item.Status == "Completed");
            int? overallPercent = state.Status == "Completed" || allDone ? 100
                : total is > 0 ? (int)Math.Clamp((decimal)received * 100m / total.Value, 0m, 99m)
                : null;

            return new DownloadJobSnapshot
            {
                JobId = state.Id,
                RepoId = state.RepoId,
                Status = state.Status,
                OverallPercent = overallPercent,
                BytesReceived = received,
                TotalBytes = total,
                Error = state.Error,
                Files = state.Files.Select(item => new DownloadItemSnapshot
                {
                    Id = item.File.Id,
                    Config = item.File.Config,
                    Split = item.File.Split,
                    Filename = item.File.Filename,
                    BytesReceived = item.BytesReceived,
                    TotalBytes = item.TotalBytes,
                    Percent = item.Status == "Completed" ? 100
                        : item.TotalBytes is > 0 ? (int)Math.Clamp((decimal)item.BytesReceived * 100m / item.TotalBytes.Value, 0m, 99m)
                        : null,
                    Status = item.Status,
                    Error = item.Error
                }).ToList()
            };
        }
    }

    private void PruneOldJobs()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(-12);
        foreach (var item in _jobs.Values)
        {
            if (item.CreatedAt < expiry && IsTerminal(item.Status) && _jobs.TryRemove(item.Id, out var removed))
                removed.Cancellation.Dispose();
        }
    }

    private static bool IsTerminal(string status) => status is "Completed" or "CompletedWithErrors" or "Cancelled" or "Failed";
}

internal sealed class DownloadJobState(string id, string repoId, IReadOnlyList<ParquetFileDto> files)
{
    public string Id { get; } = id;
    public string RepoId { get; } = repoId;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public object Sync { get; } = new();
    public CancellationTokenSource Cancellation { get; } = new();
    public string Status { get; set; } = "Queued";
    public string? Error { get; set; }
    public List<DownloadFileState> Files { get; } = files.Select(file => new DownloadFileState(file)).ToList();
}

internal sealed class DownloadFileState(ParquetFileDto file)
{
    public ParquetFileDto File { get; } = file;
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; } = file.Size;
    public string Status { get; set; } = "Queued";
    public string? Error { get; set; }
}
