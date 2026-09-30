using System.Globalization;

namespace hfparquet.Utilities;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long? bytes)
    {
        if (bytes is null || bytes < 0) return "Unknown";
        decimal value = bytes.Value;
        var unit = 0;
        while (value >= 1024m && unit < Units.Length - 1)
        {
            value /= 1024m;
            unit++;
        }
        var digits = unit == 0 ? 0 : value >= 100 ? 0 : value >= 10 ? 1 : 2;
        return $"{Math.Round(value, digits).ToString($"N{digits}", CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
