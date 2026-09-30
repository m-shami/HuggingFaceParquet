using System.Collections;
using System.Reflection;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using hfparquet.Models;
using hfparquet.Utilities;

namespace hfparquet.Services;

public sealed class ParquetPreviewService
{
    private static readonly MethodInfo ExtractValuesMethod = typeof(ParquetPreviewService)
        .GetMethod(nameof(ExtractValuesGeneric), BindingFlags.Static | BindingFlags.NonPublic)!;

    public async Task<(long TotalRows, List<ParquetColumnViewModel> Columns)> ReadSchemaAsync(string path, CancellationToken cancellationToken)
    {
        await using var reader = await ParquetReader.CreateAsync(path, cancellationToken: cancellationToken);
        var columns = reader.Schema.GetDataFields().Select(ToColumnViewModel).ToList();
        long totalRows = 0;
        for (var index = 0; index < reader.RowGroupCount; index++)
        {
            using var group = reader.OpenRowGroupReader(index);
            totalRows = checked(totalRows + group.RowCount);
        }
        return (totalRows, columns);
    }

    public async Task<LocalRowsResponse> ReadPageAsync(string path, string fileId, int requestedPage, int requestedPageSize, CancellationToken cancellationToken)
    {
        var pageWindow = PageWindow.Create(requestedPage, requestedPageSize, 100);
        await using var reader = await ParquetReader.CreateAsync(path, cancellationToken: cancellationToken);
        var fields = reader.Schema.GetDataFields();
        var columns = fields.Select(ToColumnViewModel).ToList();
        var groupCounts = new List<long>(reader.RowGroupCount);
        long totalRows = 0;
        for (var index = 0; index < reader.RowGroupCount; index++)
        {
            using var group = reader.OpenRowGroupReader(index);
            groupCounts.Add(group.RowCount);
            totalRows = checked(totalRows + group.RowCount);
        }

        var lastPage = pageWindow.LastPage(totalRows);
        var page = (int)Math.Min(pageWindow.Page, lastPage);
        pageWindow = PageWindow.Create(page, pageWindow.PageSize, 100);
        var rowCount = (int)Math.Min(pageWindow.PageSize, Math.Max(0, totalRows - pageWindow.Offset));
        var rows = Enumerable.Range(0, rowCount)
            .Select(index => new LocalRowViewModel
            {
                RowIndex = pageWindow.Offset + index,
                Cells = Enumerable.Range(0, fields.Length).Select(_ => new CellValueViewModel()).ToList()
            }).ToList();

        var groupStart = 0L;
        for (var groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
        {
            var groupRows = groupCounts[groupIndex];
            var groupEnd = groupStart + groupRows;
            var pageEnd = pageWindow.Offset + rowCount;
            if (groupEnd > pageWindow.Offset && groupStart < pageEnd)
            {
                using var group = reader.OpenRowGroupReader(groupIndex);
                for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    if (fields[fieldIndex].IsArray)
                    {
                        var marker = new CellValueViewModel
                        {
                            Display = "[nested / repeated]",
                            Full = "This column is repeated/nested and is not expanded in the simple grid."
                        };
                        for (var globalRow = Math.Max(groupStart, pageWindow.Offset); globalRow < Math.Min(groupEnd, pageEnd); globalRow++)
                            rows[(int)(globalRow - pageWindow.Offset)].Cells[fieldIndex] = marker;
                        continue;
                    }

                    var values = await ReadValuesAsync(group, fields[fieldIndex], cancellationToken);
                    var firstGlobal = Math.Max(groupStart, pageWindow.Offset);
                    var lastGlobal = Math.Min(groupEnd, pageEnd);
                    for (var globalRow = firstGlobal; globalRow < lastGlobal; globalRow++)
                    {
                        var localIndex = (int)(globalRow - groupStart);
                        var value = localIndex < values.Count ? values[localIndex] : null;
                        rows[(int)(globalRow - pageWindow.Offset)].Cells[fieldIndex] = new CellValueViewModel
                        {
                            Display = ParquetCellFormatter.Display(value),
                            Full = ParquetCellFormatter.Full(value)
                        };
                    }
                }
            }
            groupStart = groupEnd;
        }

        return new LocalRowsResponse
        {
            FileId = fileId,
            Page = pageWindow.Page,
            PageSize = pageWindow.PageSize,
            TotalRows = totalRows,
            Columns = columns,
            Rows = rows
        };
    }

    private static ParquetColumnViewModel ToColumnViewModel(DataField field)
    {
        var type = field.ClrType == typeof(ReadOnlyMemory<char>) ? "string" : field.ClrType.Name;
        return new ParquetColumnViewModel
        {
            Name = field.Name,
            Type = type,
            IsNullable = field.IsNullable,
            IsRepeated = field.IsArray
        };
    }

    private static async Task<List<object?>> ReadValuesAsync(ParquetRowGroupReader group, DataField field, CancellationToken cancellationToken)
    {
        using var data = await group.ReadRawColumnDataBaseAsync(field, cancellationToken);
        var valueType = data.GetType().GetGenericArguments()[0];
        var method = ExtractValuesMethod.MakeGenericMethod(valueType);
        return (List<object?>?)method.Invoke(null, [data, field.IsNullable]) ?? [];
    }

    private static List<object?> ExtractValuesGeneric<T>(RawColumnData<T> data, bool isNullable) where T : struct
    {
        if (isNullable)
            return data.NullableValues.Cast<object?>().ToList();
        return data.Values.ToArray().Cast<object?>().ToList();
    }
}
