using System.Diagnostics;
using hfparquet.Models;
using hfparquet.Services;
using hfparquet.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace hfparquet.Controllers;

public sealed class HomeController(
    HuggingFaceClient huggingFace,
    DownloadFileStore fileStore,
    RecentRepositoryStore recentRepos,
    ILogger<HomeController> logger) : Controller
{
    private const int BrowsePageSize = 50;

    [HttpGet("/")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index(string? repoId, string? config, string? split, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = new RepositoryIndexViewModel
        {
            RepoId = repoId?.Trim() ?? string.Empty,
            RecentRepositories = recentRepos.List().ToList()
        };
        if (string.IsNullOrWhiteSpace(model.RepoId)) return View(model);
        if (!RepositoryIdParser.TryParse(model.RepoId, out var repo, out var validationError))
        {
            model.Error = validationError;
            return View(model);
        }

        model.RepoId = repo.FullId;
        try
        {
            var result = await huggingFace.GetParquetFilesAsync(repo.FullId, cancellationToken);
            model.Partial = result.Partial;
            model.PendingCount = result.Pending.Count;
            model.FailedCount = result.Failed.Count;

            var allFiles = new List<ParquetFileRowViewModel>();
            foreach (var file in result.ParquetFiles)
            {
                if (!RepositoryIdParser.TryParse(file.Dataset, out var fileRepo, out _) || !string.Equals(fileRepo.FullId, repo.FullId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!DownloadFileStore.IsAllowedHubUrl(file.Url) || string.IsNullOrWhiteSpace(file.Filename) || !file.Filename.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase)) continue;
                file.Id = RepositoryIdParser.CreateFileId(file.Dataset, file.Config, file.Split, file.Url);
                var fileName = file.Filename.Replace('\\', '/').Split('/').Last();
                allFiles.Add(new ParquetFileRowViewModel
                {
                    Id = file.Id,
                    Config = file.Config,
                    Split = file.Split,
                    Filename = fileName,
                    DisplayPath = $"{file.Config}/{file.Split}/{fileName}",
                    Size = file.Size,
                    SizeLabel = ByteSizeFormatter.Format(file.Size),
                    IsDownloaded = fileStore.IsDownloaded(file)
                });
            }

            model.TotalFileCount = allFiles.Count;
            model.PreviewGroups = allFiles
                .Select(file => new RemotePreviewLinkViewModel { Config = file.Config, Split = file.Split })
                .DistinctBy(group => (group.Config, group.Split))
                .ToList();

            ApplyFolderBrowse(model, allFiles, config, split, page);
            recentRepos.Remember(repo.FullId, allFiles.Count);
            model.RecentRepositories = recentRepos.List().ToList();

            if (allFiles.Count == 0 && model.PendingCount == 0 && model.FailedCount == 0)
                model.Error = "No Parquet shards were listed for this repository. Check the ID, or try again if the Hub is still preparing the dataset.";
        }
        catch (HuggingFaceApiException ex)
        {
            model.Error = ex.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            model.Error = "The repository lookup was cancelled.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not list Parquet files for {RepoId}", repo.FullId);
            model.Error = "Could not reach the Hugging Face Dataset Viewer. Check your connection and try again.";
        }

        return View(model);
    }

    private static void ApplyFolderBrowse(RepositoryIndexViewModel model, List<ParquetFileRowViewModel> allFiles, string? config, string? split, int page)
    {
        if (allFiles.Count == 0) return;

        var configs = allFiles
            .Select(file => file.Config)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var browseConfig = string.IsNullOrWhiteSpace(config) ? null : config.Trim();
        if (browseConfig is not null && !configs.Contains(browseConfig, StringComparer.Ordinal))
            browseConfig = null;

        if (browseConfig is null)
        {
            var folders = configs.Select(name =>
            {
                var files = allFiles.Where(file => string.Equals(file.Config, name, StringComparison.Ordinal)).ToList();
                return new FolderEntryViewModel
                {
                    Name = name,
                    Kind = "config",
                    FileCount = files.Count,
                    SizeLabel = ByteSizeFormatter.Format(SumKnownSizes(files)),
                    Href = $"/?repoId={Uri.EscapeDataString(model.RepoId)}&config={Uri.EscapeDataString(name)}"
                };
            }).ToList();
            model.Folders = Paginate(model, folders, page);
            return;
        }

        model.BrowseConfig = browseConfig;
        var inConfig = allFiles.Where(file => string.Equals(file.Config, browseConfig, StringComparison.Ordinal)).ToList();
        var splits = inConfig
            .Select(file => file.Split)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var browseSplit = string.IsNullOrWhiteSpace(split) ? null : split.Trim();
        if (browseSplit is not null && !splits.Contains(browseSplit, StringComparer.Ordinal))
            browseSplit = null;

        if (browseSplit is null)
        {
            var folders = splits.Select(name =>
            {
                var files = inConfig.Where(file => string.Equals(file.Split, name, StringComparison.Ordinal)).ToList();
                return new FolderEntryViewModel
                {
                    Name = name,
                    Kind = "split",
                    FileCount = files.Count,
                    SizeLabel = ByteSizeFormatter.Format(SumKnownSizes(files)),
                    Href = $"/?repoId={Uri.EscapeDataString(model.RepoId)}&config={Uri.EscapeDataString(browseConfig)}&split={Uri.EscapeDataString(name)}"
                };
            }).ToList();
            model.Folders = Paginate(model, folders, page);
            return;
        }

        model.BrowseSplit = browseSplit;
        var filesInSplit = inConfig
            .Where(file => string.Equals(file.Split, browseSplit, StringComparison.Ordinal))
            .OrderBy(file => file.Filename, StringComparer.OrdinalIgnoreCase)
            .ToList();
        model.Files = Paginate(model, filesInSplit, page);
    }

    private static List<T> Paginate<T>(RepositoryIndexViewModel model, List<T> items, int page)
    {
        model.PageSize = BrowsePageSize;
        model.TotalItems = items.Count;
        model.TotalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)BrowsePageSize));
        model.Page = Math.Clamp(page < 1 ? 1 : page, 1, model.TotalPages);
        return items.Skip((model.Page - 1) * BrowsePageSize).Take(BrowsePageSize).ToList();
    }

    private static long? SumKnownSizes(IReadOnlyList<ParquetFileRowViewModel> files)
    {
        if (files.Count == 0 || files.Any(file => file.Size is null)) return null;
        return files.Sum(file => file.Size!.Value);
    }

    [HttpGet("/Home/Error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
