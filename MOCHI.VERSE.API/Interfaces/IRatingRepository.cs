using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Interfaces;

/// <summary>
/// Data access contract for dbo.Ratings.
/// </summary>
public interface IRatingRepository
{
    Task<RatingRecord> UpsertAsync(
        int userId,
        string source,
        string externalId,
        byte score,
        string? review,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int userId,
        string source,
        string externalId,
        CancellationToken cancellationToken = default);

    Task<List<RatingItem>> GetByTitleAsync(
        string source,
        string externalId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
