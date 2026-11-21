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

public class TitleRatingSummary
{
    public int TotalVotes { get; set; }
    public decimal AverageScore { get; set; }
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
    public DateTime CachedAt { get; set; }
    public int TotalVotes { get; set; }
    public decimal AverageScore { get; set; }
}
