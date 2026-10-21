using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Portal.Common.Api;

/// <summary>Gemeinsame Query-Parameter für paginierte Listen.</summary>
public class PaginatedFilterRequest
{
    [RegularExpression("^(asc|desc)$", ErrorMessage = "Erlaubt sind asc oder desc.")]
    public string? SortOrder { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "Muss mindestens 0 sein.")]
    public int Offset { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Muss größer als 0 sein.")]
    public int? Limit { get; init; }

    public bool IsDescending(bool defaultDescending) =>
        SortOrder is null ? defaultDescending : SortOrder == "desc";
}

public static class QueryExtensions
{
    public static IOrderedQueryable<T> OrderByDirection<T, TKey>(
        this IQueryable<T> query, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

    public static IOrderedQueryable<T> ThenByDirection<T, TKey>(
        this IOrderedQueryable<T> query, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? query.ThenByDescending(key) : query.ThenBy(key);

    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query, int offset, int? limit, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var paged = query.Skip(offset);
        if (limit is not null) paged = paged.Take(limit.Value);
        var items = await paged.ToListAsync(ct);
        return new PagedResult<T>(items, new PageMeta(total, offset, limit));
    }
}
