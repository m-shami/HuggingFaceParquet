namespace hfparquet.Models;

public sealed class RepositoryIndexViewModel
{
    public string RepoId { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool Partial { get; set; }
    public int PendingCount { get; set; }
    public int FailedCount { get; set; }
    public string? BrowseConfig { get; set; }
    public string? BrowseSplit { get; set; }
    public int TotalFileCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalItems { get; set; }
    public int TotalPages { get; set; } = 1;
    public List<FolderEntryViewModel> Folders { get; set; } = [];
    public List<ParquetFileRowViewModel> Files { get; set; } = [];
    public List<RemotePreviewLinkViewModel> PreviewGroups { get; set; } = [];
    public List<RecentRepositoryEntry> RecentRepositories { get; set; } = [];
}

public sealed class RecentRepositoryEntry
{
    public string RepoId { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public DateTime QueriedUtc { get; set; }
}

public sealed class FolderEntryViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public string SizeLabel { get; set; } = "Unknown";
    public string Href { get; set; } = string.Empty;
}

public sealed class ParquetFileRowViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string SizeLabel { get; set; } = "Unknown";
    public bool IsDownloaded { get; set; }
}

public sealed class RemotePreviewLinkViewModel
{
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
}

public sealed class RemotePreviewViewModel
{
    public string RepoId { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
}

public sealed class DownloadedFilesViewModel
{
    public List<DownloadedParquetFileViewModel> Files { get; set; } = [];
}

public sealed class DownloadedParquetFileViewModel
{
    public string Id { get; set; } = string.Empty;
    public string RepoId { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string SizeLabel { get; set; } = "Unknown";
    public string DownloadUrl { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; }
}

public sealed class LocalPreviewViewModel
{
    public string FileId { get; set; } = string.Empty;
    public string RepoId { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string SizeLabel { get; set; } = "Unknown";
    public long TotalRows { get; set; }
    public int PageSize { get; set; } = 50;
    public List<ParquetColumnViewModel> Columns { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class ParquetColumnViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsRepeated { get; set; }
}

public sealed class LocalRowsResponse
{
    public string FileId { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalRows { get; set; }
    public List<ParquetColumnViewModel> Columns { get; set; } = [];
    public List<LocalRowViewModel> Rows { get; set; } = [];
}

public sealed class LocalRowViewModel
{
    public long RowIndex { get; set; }
    public List<CellValueViewModel> Cells { get; set; } = [];
}

public sealed class CellValueViewModel
{
    public string Display { get; set; } = string.Empty;
    public string Full { get; set; } = string.Empty;
}
