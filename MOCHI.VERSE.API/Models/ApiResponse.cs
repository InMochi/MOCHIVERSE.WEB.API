using System.Text.Json.Serialization;

namespace MOCHI.VERSE.API.Models;

/// <summary>
/// Standard API response wrapper.
/// </summary>
public class APIResponse
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, string[]>? Errors { get; set; }
}

/// <summary>
/// Standard paginated list wrapper.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => TotalItems == 0 || PageSize <= 0
        ? 0
        : (int)Math.Ceiling(TotalItems / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
}
