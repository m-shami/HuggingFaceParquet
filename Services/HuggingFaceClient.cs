using System.Net;
using System.Text.Json;
using hfparquet.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace hfparquet.Services;

public sealed class HuggingFaceClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ParquetListResponse> GetParquetFilesAsync(string dataset, CancellationToken cancellationToken)
    {
        var url = QueryHelpers.AddQueryString("parquet", "dataset", dataset);
        return await GetAsync<ParquetListResponse>(url, cancellationToken);
    }

    public async Task<RemoteRowsResponse> GetRowsAsync(string dataset, string config, string split, long offset, int length, CancellationToken cancellationToken)
    {
        var safeLength = Math.Clamp(length, 1, 100);
        var query = new Dictionary<string, string?>
        {
            ["dataset"] = dataset,
            ["config"] = config,
            ["split"] = split,
            ["offset"] = Math.Max(0, offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["length"] = safeLength.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var url = QueryHelpers.AddQueryString("rows", query);
        return await GetAsync<RemoteRowsResponse>(url, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : new()
    {
        using var response = await httpClient.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 500) body = body[..500] + "…";
            var explanation = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
            throw new HuggingFaceApiException(response.StatusCode, $"Hugging Face returned {(int)response.StatusCode} {response.ReasonPhrase}: {explanation}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ?? new T();
    }
}

public sealed class HuggingFaceApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
