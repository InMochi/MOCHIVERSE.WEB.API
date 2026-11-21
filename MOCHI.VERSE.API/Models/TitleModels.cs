namespace MOCHI.VERSE.API.Models;

/// <summary>
/// Cached title metadata matching dbo.TitleCache.
/// </summary>
public class TitleMetadata
{
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public short? ReleaseYear { get; set; }
    public string? Synopsis { get; set; }
    public List<string> Genres { get; set; } = [];
    public DateTime CachedAt { get; set; }
}

public class TitleSearchItem
{
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public short? ReleaseYear { get; set; }
    public string? Synopsis { get; set; }
}

public class ProviderSearchResult
{
    public List<TitleSearchItem> Items { get; set; } = [];
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public class TitleRatingSummary
{
    public int TotalVotes { get; set; }
    public decimal AverageScore { get; set; }
}

public class MyTitleRating
{
    public int Id { get; set; }
    public byte Score { get; set; }
    public string? Review { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class MyTitleWatchlist
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class TitleDetailResponse
{
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public short? ReleaseYear { get; set; }
    public string? Synopsis { get; set; }
    public List<string> Genres { get; set; } = [];
    public DateTime CachedAt { get; set; }
    public int TotalVotes { get; set; }
    public decimal AverageScore { get; set; }
    public MyTitleRating? MyRating { get; set; }
    public MyTitleWatchlist? MyWatchlist { get; set; }
}
