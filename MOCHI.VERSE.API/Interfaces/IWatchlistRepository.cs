using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Interfaces;

/// <summary>
/// Data access contract for dbo.Watchlists.
/// </summary>
public interface IWatchlistRepository
{
    Task<WatchlistItem> UpsertAsync(
        int userId,
        string source,
        string externalId,
        string status,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int userId,
        string source,
        string externalId,
        CancellationToken cancellationToken = default);

    Task<List<WatchlistItem>> GetByUserAsync(
        int userId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
