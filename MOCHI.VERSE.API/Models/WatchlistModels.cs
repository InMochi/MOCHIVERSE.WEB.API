using System.ComponentModel.DataAnnotations;

namespace MOCHI.VERSE.API.Models;

public class UpsertWatchlistRequest
{
    [Required]
    [RegularExpression("^(PlanToWatch|Watching|Completed)$",
        ErrorMessage = "Status must be 'PlanToWatch', 'Watching', or 'Completed'.")]
    public string Status { get; set; } = "PlanToWatch";
}

public class WatchlistItem
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MediaType { get; set; }
    public string? Title { get; set; }
    public string? PosterUrl { get; set; }
    public short? ReleaseYear { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
