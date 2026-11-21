using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Interfaces;

/// <summary>
/// External media metadata provider contract (e.g. TMDB, Jikan).
/// </summary>
public interface ITitleProvider
{
    string Source { get; }
    string MediaType { get; }
    bool IsConfigured { get; }

    Task<List<TitleSearchItem>> SearchAsync(string query, int page = 1, CancellationToken cancellationToken = default);
    Task<TitleMetadata?> GetDetailAsync(string externalId, CancellationToken cancellationToken = default);
}
