using System.ComponentModel.DataAnnotations;

namespace MOCHI.VERSE.API.Models;

public class UpsertRatingRequest
{
    [Range(1, 10, ErrorMessage = "Score must be between 1 and 10.")]
    public byte Score { get; set; }

    [MaxLength(2000)]
    public string? Review { get; set; }
}

public class RatingRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public byte Score { get; set; }
    public string? Review { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class RatingItem
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public byte Score { get; set; }
    public string? Review { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class TitleRatingsPage
{
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public int TotalVotes { get; set; }
    public decimal AverageScore { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<RatingItem> Items { get; set; } = [];
}
