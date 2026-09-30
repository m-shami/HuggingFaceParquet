using hfparquet.Models;
using hfparquet.Services;
using Microsoft.AspNetCore.Mvc;

namespace hfparquet.Controllers;

[Route("Parquet")]
public sealed class ParquetController(
    DownloadFileStore fileStore,
    ParquetPreviewService previewService,
    ILogger<ParquetController> logger) : Controller
{
    [HttpGet("Preview")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Preview(string? fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId)) return BadRequest("A downloaded file ID is required.");
        var match = fileStore.FindById(fileId);
        if (match is null) return NotFound("That downloaded Parquet file no longer exists.");

        var file = match.Value.Metadata;
        var model = new LocalPreviewViewModel
        {
            FileId = file.Id,
            RepoId = file.RepoId,
            Config = file.Config,
            Split = file.Split,
            Filename = file.Filename,
            SizeLabel = file.SizeLabel
        };
        try
        {
            var schema = await previewService.ReadSchemaAsync(match.Value.Path, cancellationToken);
            model.TotalRows = schema.TotalRows;
            model.Columns = schema.Columns;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read Parquet schema for file {FileId}", fileId);
            model.Error = "This file could not be parsed as a supported Parquet file. The download may be incomplete or use an unsupported nested type.";
        }
        return View(model);
    }

    [HttpGet("Rows")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Rows(string? fileId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId)) return BadRequest(new { error = "A downloaded file ID is required." });
        var match = fileStore.FindById(fileId);
        if (match is null) return NotFound(new { error = "That downloaded Parquet file no longer exists." });
        try
        {
            var rows = await previewService.ReadPageAsync(match.Value.Path, fileId, page, pageSize, cancellationToken);
            return Json(rows);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499, new { error = "The local preview request was cancelled." });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read Parquet page {Page} from file {FileId}", page, fileId);
            return UnprocessableEntity(new { error = "The requested Parquet page could not be read. Try an earlier page or verify the file is complete." });
        }
    }
}
