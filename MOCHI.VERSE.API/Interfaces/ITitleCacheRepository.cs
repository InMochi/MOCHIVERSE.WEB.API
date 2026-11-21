using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Interfaces;

/// <summary>
/// Data access contract for dbo.TitleCache and dbo.vw_TitleRatingSummary.
/// </summary>
public interface ITitleCacheRepository
{
    Task<TitleMetadata?> GetAsync(string source, string externalId, CancellationToken cancellationToken = default);
    Task UpsertAsync(TitleMetadata title, CancellationToken cancellationToken = default);
    Task<TitleRatingSummary> GetRatingSummaryAsync(string source, string externalId, CancellationToken cancellationToken = default);
}
