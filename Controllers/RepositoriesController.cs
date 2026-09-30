using hfparquet.Models;
using hfparquet.Services;
using hfparquet.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace hfparquet.Controllers;

[Route("Repositories")]
public sealed class RepositoriesController(HuggingFaceClient huggingFace, ILogger<RepositoriesController> logger) : Controller
{
    [HttpGet("Preview")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Preview(string? repoId, string? config, string? split)
    {
        if (!RepositoryIdParser.TryParse(repoId, out var repo, out var error)) return BadRequest(error);
        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(split)) return BadRequest("Config and split are required.");
        return View(new RemotePreviewViewModel { RepoId = repo.FullId, Config = config.Trim(), Split = split.Trim() });
    }

    [HttpGet("Rows")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Rows(string? repoId, string? config, string? split, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (!RepositoryIdParser.TryParse(repoId, out var repo, out var error)) return BadRequest(new { error });
        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(split)) return BadRequest(new { error = "Config and split are required." });
        var window = PageWindow.Create(page, pageSize, 100);
        try
        {
            var result = await huggingFace.GetRowsAsync(repo.FullId, config.Trim(), split.Trim(), window.Offset, window.PageSize, cancellationToken);
            var totalRows = result.NumRowsTotal;
            var normalizedPage = totalRows is null ? window.Page : (int)Math.Min(window.Page, window.LastPage(totalRows.Value));
            if (normalizedPage != window.Page)
            {
                window = PageWindow.Create(normalizedPage, window.PageSize, 100);
                result = await huggingFace.GetRowsAsync(repo.FullId, config.Trim(), split.Trim(), window.Offset, window.PageSize, cancellationToken);
                totalRows = result.NumRowsTotal;
                normalizedPage = totalRows is null ? window.Page : (int)Math.Min(window.Page, window.LastPage(totalRows.Value));
            }
            return Json(new
            {
                dataset = result.Dataset,
                config = result.Config,
                split = result.Split,
                page = normalizedPage,
                pageSize = window.PageSize,
                totalRows,
                partial = result.Partial,
                features = result.Features,
                rows = result.Rows
            });
        }
        catch (HuggingFaceApiException ex)
        {
            return StatusCode((int)ex.StatusCode, new { error = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "The preview request was cancelled." });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load remote rows for {RepoId}/{Config}/{Split}", repo.FullId, config, split);
            return StatusCode(503, new { error = "The Hugging Face Dataset Viewer is currently unavailable. Download a shard to preview it locally." });
        }
    }
}
