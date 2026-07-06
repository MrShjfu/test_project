namespace Helm.Core.Api;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public record PageRequest(int Page = 1, int PageSize = 20)
{
    public int Skip => (Page - 1) * PageSize;
    public PageRequest Normalized() => new(Math.Max(1, Page), Math.Clamp(PageSize, 1, 200));
}
