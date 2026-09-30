using System.Security;
using System.Text.RegularExpressions;
using hfparquet.Models;
using hfparquet.Utilities;
using Microsoft.AspNetCore.Hosting;

namespace hfparquet.Services;

public sealed class DownloadFileStore
{
    private readonly string _root;
    private readonly string _rootWithSeparator;

    public DownloadFileStore(IWebHostEnvironment environment)
    {
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot)) webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
        _root = Path.GetFullPath(Path.Combine(webRoot, "download"));
        Directory.CreateDirectory(_root);
        _rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        RemoveStalePartialFiles();
    }

    public string RootPath => _root;

    public string GetFinalPath(ParquetFileDto file)
    {
        if (!RepositoryIdParser.TryParse(file.Dataset, out var repository, out var error))
            throw new SecurityException(error);
        if (!IsAllowedHubUrl(file.Url)) throw new SecurityException("The file URL is not a valid Hugging Face dataset URL.");

        var config = RepositoryIdParser.SafePathSegment(file.Config);
        var split = RepositoryIdParser.SafePathSegment(file.Split);
        var fileName = SafeParquetFileName(file.Filename);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = RepositoryIdParser.CreateFileId(file.Dataset, file.Config, file.Split, file.Url)[..8];
        var uniqueName = $"{stem}-{suffix}{extension}";
        var destination = Path.GetFullPath(Path.Combine(_root, repository.Owner, repository.Repository, config, split, uniqueName));
        EnsureUnderRoot(destination);
        return destination;
    }

    public bool IsDownloaded(ParquetFileDto file)
    {
        var path = GetFinalPath(file);
        return File.Exists(path) && (file.Size is null || new FileInfo(path).Length == file.Size.Value);
    }

    public IReadOnlyList<DownloadedParquetFileViewModel> ListAll()
    {
        if (!Directory.Exists(_root)) return [];
        var result = new List<DownloadedParquetFileViewModel>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.parquet", SearchOption.AllDirectories))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                EnsureUnderRoot(fullPath);
                var relative = Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
                var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5 || !RepositoryIdParser.TryParse($"{parts[0]}/{parts[1]}", out var repo, out _)) continue;
                var id = RepositoryIdParser.CreateLocalFileId(relative);
                var info = new FileInfo(fullPath);
                result.Add(new DownloadedParquetFileViewModel
                {
                    Id = id,
                    RepoId = repo.FullId,
                    Config = parts[2],
                    Split = parts[3],
                    Filename = parts[^1],
                    SizeLabel = ByteSizeFormatter.Format(info.Length),
                    DownloadUrl = "/download/" + string.Join('/', parts.Select(Uri.EscapeDataString)),
                    LastModifiedUtc = info.LastWriteTimeUtc
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Ignore an inaccessible or unsafe file and continue listing other downloads.
            }
        }

        return result.OrderByDescending(file => file.LastModifiedUtc).ToArray();
    }

    public (string Path, DownloadedParquetFileViewModel Metadata)? FindById(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return null;
        foreach (var file in ListAll())
        {
            if (!string.Equals(file.Id, id, StringComparison.Ordinal)) continue;
            var path = Path.GetFullPath(Path.Combine(_root, Uri.UnescapeDataString(file.DownloadUrl["/download/".Length..]).Replace('/', Path.DirectorySeparatorChar)));
            EnsureUnderRoot(path);
            if (File.Exists(path)) return (path, file);
        }
        return null;
    }

    public static bool IsAllowedHubUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        return string.Equals(uri.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowedDownloadHost(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host == "huggingface.co" || host.EndsWith(".huggingface.co", StringComparison.Ordinal)
            || host == "hf.co" || host.EndsWith(".hf.co", StringComparison.Ordinal);
    }

    private static string SafeParquetFileName(string value)
    {
        var basename = (value ?? string.Empty).Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        var safe = Regex.Replace(basename, "[^A-Za-z0-9._-]", "-").Trim('.', '-', '_');
        if (string.IsNullOrWhiteSpace(safe) || !safe.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Hugging Face returned an invalid Parquet filename.");
        return safe;
    }

    private void EnsureUnderRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_rootWithSeparator, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SecurityException("The resolved download path is outside the download directory.");
    }

    private void RemoveStalePartialFiles()
    {
        try
        {
            foreach (var partial in Directory.EnumerateFiles(_root, "*.part", SearchOption.AllDirectories))
            {
                try { File.Delete(partial); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
