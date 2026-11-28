namespace SwfCrypt.Api.Services;

public static class Paging
{
    public const int MaxPageSize = 50;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
