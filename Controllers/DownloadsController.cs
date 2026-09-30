using hfparquet.Models;
using hfparquet.Services;
using hfparquet.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace hfparquet.Controllers;

[Route("Downloads")]
public sealed class DownloadsController(
    HuggingFaceClient huggingFace,
    DownloadFileStore fileStore,
    DownloadManager downloadManager,
    ILogger<DownloadsController> logger) : Controller
{
    [HttpGet("")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index() => View(new DownloadedFilesViewModel { Files = fileStore.ListAll().ToList() });

    [HttpPost("Start")]
    [ValidateAntiForgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Start([FromForm] StartDownloadForm form, CancellationToken cancellationToken)
    {
        if (!RepositoryIdParser.TryParse(form.RepoId, out var repo, out var validationError))
            return BadRequest(new { error = validationError });

        try
        {
            var response = await huggingFace.GetParquetFilesAsync(repo.FullId, cancellationToken);
            var validFiles = response.ParquetFiles
                .Where(file => RepositoryIdParser.TryParse(file.Dataset, out var fileRepo, out _) && string.Equals(fileRepo.FullId, repo.FullId, StringComparison.OrdinalIgnoreCase))
                .Where(file => DownloadFileStore.IsAllowedHubUrl(file.Url) && file.Filename.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var file in validFiles)
                file.Id = RepositoryIdParser.CreateFileId(file.Dataset, file.Config, file.Split, file.Url);

            var requestedIds = form.SelectAll ? validFiles.Select(file => file.Id).ToHashSet(StringComparer.Ordinal)
                : form.FileIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
            if (requestedIds.Count == 0) return BadRequest(new { error = "Select at least one Parquet file." });

            var selected = validFiles.Where(file => requestedIds.Contains(file.Id)).ToList();
            if (selected.Count != requestedIds.Count) return BadRequest(new { error = "One or more selected files are no longer listed by the Hub. Refresh the repository listing and try again." });
            if (selected.Count > 250) return BadRequest(new { error = "A single job is limited to 250 files. Select a smaller batch." });
            var invalidSize = selected.FirstOrDefault(file => file.Size is < 0);
            if (invalidSize is not null) return BadRequest(new { error = "The Hub returned an invalid file size." });

            var job = await downloadManager.StartAsync(repo.FullId, selected, cancellationToken);
            return Json(job);
        }
        catch (HuggingFaceApiException ex)
        {
            return StatusCode((int)ex.StatusCode, new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "The download request was cancelled before it entered the queue." });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start download job for {RepoId}", repo.FullId);
            return StatusCode(503, new { error = "Could not queue this download. Check the application log and try again." });
        }
    }

    [HttpGet("Status")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(string? id)
    {
        var snapshot = string.IsNullOrWhiteSpace(id) ? null : downloadManager.GetSnapshot(id);
        return snapshot is null ? NotFound(new { error = "Download job not found. Job progress is kept in memory and is cleared when the app restarts." }) : Json(snapshot);
    }

    [HttpPost("Cancel")]
    [ValidateAntiForgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Cancel([FromForm] string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "A job ID is required." });
        if (!downloadManager.Cancel(id)) return Conflict(new { error = "This job is not active or no longer exists." });
        return Ok(new { status = "cancelling" });
    }

    [HttpPost("Retry")]
    [ValidateAntiForgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Retry([FromForm] string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "A job ID is required." });
        var snapshot = await downloadManager.RetryAsync(id, cancellationToken);
        return snapshot is null ? Conflict(new { error = "There are no failed files to retry, or the job is still active." }) : Json(snapshot);
    }
}
