namespace hfparquet.Models;

public sealed class StartDownloadForm
{
    public string RepoId { get; set; } = string.Empty;
    public bool SelectAll { get; set; }
    public List<string> FileIds { get; set; } = [];
}

public sealed class DownloadJobSnapshot
{
    public string JobId { get; set; } = string.Empty;
    public string RepoId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? OverallPercent { get; set; }
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public string? Error { get; set; }
    public List<DownloadItemSnapshot> Files { get; set; } = [];
}

public sealed class DownloadItemSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public int? Percent { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}
