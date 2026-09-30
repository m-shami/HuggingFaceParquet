using System.Text.Json;
using System.Text.Json.Serialization;

namespace hfparquet.Models;

public sealed class ParquetListResponse
{
    [JsonPropertyName("parquet_files")]
    public List<ParquetFileDto> ParquetFiles { get; set; } = [];

    public List<JsonElement> Pending { get; set; } = [];
    public List<JsonElement> Failed { get; set; } = [];
    public bool Partial { get; set; }
}

public sealed class ParquetFileDto
{
    [JsonIgnore]
    public string Id { get; set; } = string.Empty;

    public string Dataset { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public long? Size { get; set; }
}

public sealed class RemoteRowsResponse
{
    public string Dataset { get; set; } = string.Empty;
    public string Config { get; set; } = string.Empty;
    public string Split { get; set; } = string.Empty;
    public List<RemoteFeatureDto> Features { get; set; } = [];
    public List<RemoteRowDto> Rows { get; set; } = [];

    [JsonPropertyName("num_rows_total")]
    public long? NumRowsTotal { get; set; }

    [JsonPropertyName("num_rows_per_page")]
    public int? NumRowsPerPage { get; set; }

    public bool Partial { get; set; }
}

public sealed class RemoteFeatureDto
{
    public string Name { get; set; } = string.Empty;
    public JsonElement Type { get; set; }
}

public sealed class RemoteRowDto
{
    [JsonPropertyName("row_idx")]
    public long RowIndex { get; set; }

    public Dictionary<string, JsonElement> Row { get; set; } = new(StringComparer.Ordinal);
}
