using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace hfparquet.Utilities;

public sealed record RepositoryIdentity(string Owner, string Repository)
{
    public string FullId => $"{Owner}/{Repository}";
}

public static partial class RepositoryIdParser
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,95}/[A-Za-z0-9][A-Za-z0-9._-]{0,95}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();

    public static bool TryParse(string? value, out RepositoryIdentity identity, out string error)
    {
        identity = new RepositoryIdentity(string.Empty, string.Empty);
        error = string.Empty;
        var candidate = value?.Trim() ?? string.Empty;

        if (!RepositoryPattern().IsMatch(candidate))
        {
            error = "Enter a public dataset ID in the form owner/repository (letters, numbers, dots, underscores, and hyphens).";
            return false;
        }

        var parts = candidate.Split('/', StringSplitOptions.None);
        if (parts.Length != 2 || parts.Any(part => part is "." or ".."))
        {
            error = "The repository ID must contain exactly one owner and one repository name.";
            return false;
        }

        identity = new RepositoryIdentity(parts[0], parts[1]);
        return true;
    }

    public static string SafePathSegment(string value, string fallback = "unknown")
    {
        var safe = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9._-]", "-");
        safe = safe.Trim('.', '-', '_');
        return string.IsNullOrWhiteSpace(safe) || safe is "." or ".." ? fallback : safe;
    }

    public static string CreateFileId(string dataset, string config, string split, string url)
    {
        var value = $"{dataset}\n{config}\n{split}\n{url}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }

    public static string CreateLocalFileId(string normalizedRelativePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("local\n" + normalizedRelativePath.Replace('\\', '/')));
        return Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }
}
