using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace hfparquet.Utilities;

public static class ParquetCellFormatter
{
    public static string Full(object? value)
    {
        if (value is null) return "(null)";
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => "(null)",
                JsonValueKind.Undefined => string.Empty,
                JsonValueKind.String => element.GetString() ?? string.Empty,
                _ => element.GetRawText()
            };
        }

        if (value is ReadOnlyMemory<char> text) return text.ToString();
        if (value is Memory<char> mutableText) return mutableText.ToString();
        if (value is ReadOnlyMemory<byte> binaryMemory) return $"binary ({binaryMemory.Length} bytes)";
        if (value is Memory<byte> mutableBinary) return $"binary ({mutableBinary.Length} bytes)";
        if (value is char[] characters) return new string(characters);
        if (value is byte[] bytes) return $"binary ({bytes.Length} bytes)";
        if (value is DateTime dateTime) return dateTime.ToString("O", CultureInfo.InvariantCulture);
        if (value is DateTimeOffset dateTimeOffset) return dateTimeOffset.ToString("O", CultureInfo.InvariantCulture);
        if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        if (value is IEnumerable sequence)
        {
            try { return JsonSerializer.Serialize(sequence.Cast<object?>().ToArray()); }
            catch { return value.ToString() ?? string.Empty; }
        }

        try { return JsonSerializer.Serialize(value); }
        catch { return value.ToString() ?? string.Empty; }
    }

    public static string Display(object? value, int maximumCharacters = 180)
    {
        var full = Full(value);
        if (full.Length <= maximumCharacters) return full;
        return full[..Math.Max(0, maximumCharacters - 1)] + "…";
    }
}
