namespace hfparquet.Utilities;

public readonly record struct PageWindow(int Page, int PageSize, long Offset)
{
    public static PageWindow Create(int requestedPage, int requestedPageSize = 50, int maximumPageSize = 100)
    {
        var page = Math.Max(1, requestedPage);
        var pageSize = Math.Clamp(requestedPageSize, 1, maximumPageSize);
        var offset = checked((long)(page - 1) * pageSize);
        return new PageWindow(page, pageSize, offset);
    }

    public long LastPage(long totalRows)
    {
        if (totalRows <= 0) return 1;
        var pages = totalRows / PageSize + (totalRows % PageSize == 0 ? 0 : 1);
        return Math.Max(1, pages);
    }
}
