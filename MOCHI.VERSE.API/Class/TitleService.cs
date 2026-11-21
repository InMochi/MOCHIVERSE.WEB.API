using Microsoft.Extensions.Caching.Memory;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Orchestrates title search, detail lookup, IMemoryCache, and SQL TitleCache persistence.
/// </summary>
public sealed class TitleService
{
    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DetailMemoryCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TitleDbCacheTtl = TimeSpan.FromHours(24);

    private readonly IReadOnlyDictionary<string, ITitleProvider> _providers;
    private readonly ITitleCacheRepository _cacheRepository;
    private readonly IRatingRepository _ratingRepository;
    private readonly IWatchlistRepository _watchlistRepository;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<TitleService> _logger;

    public TitleService(
        IEnumerable<ITitleProvider> providers,
        ITitleCacheRepository cacheRepository,
        IRatingRepository ratingRepository,
        IWatchlistRepository watchlistRepository,
        IMemoryCache memoryCache,
        ILogger<TitleService> logger)
    {
        _providers = providers.ToDictionary(p => p.Source, StringComparer.OrdinalIgnoreCase);
        _cacheRepository = cacheRepository;
        _ratingRepository = ratingRepository;
        _watchlistRepository = watchlistRepository;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<PagedResult<TitleSearchItem>> SearchAsync(
        string query,
        string? type,
        int page,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.Trim();
        var normalizedType = string.IsNullOrWhiteSpace(type) ? "all" : type.Trim().ToLowerInvariant();

        var selectedProviders = SelectProvidersForType(normalizedType);
        var cacheKey = $"titles:search:{normalizedType}:{normalizedQuery.ToLowerInvariant()}:{page}";

        if (_memoryCache.TryGetValue(cacheKey, out PagedResult<TitleSearchItem>? cached) && cached is not null)
        {
            return cached;
        }

        var tasks = selectedProviders.Select(async provider =>
        {
            if (!provider.IsConfigured)
            {
                if (normalizedType != "all")
                {
                    throw new InvalidOperationException($"{provider.Source} provider is not configured.");
                }

                _logger.LogWarning("Skipping unconfigured provider {Source} during combined search.", provider.Source);
                return new ProviderSearchResult();
            }

            try
            {
                return await provider.SearchAsync(normalizedQuery, page, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (normalizedType != "all")
                {
                    throw;
                }

                _logger.LogWarning(ex, "Provider {Source} failed during combined search.", provider.Source);
                return new ProviderSearchResult();
            }
        });

        var resultBatches = await Task.WhenAll(tasks);

        var items = resultBatches.SelectMany(batch => batch.Items).ToList();
        var totalItems = resultBatches.Sum(batch => batch.TotalItems);
        var combinedPageSize = Math.Max(1, resultBatches.Sum(batch => batch.PageSize));

        var paged = new PagedResult<TitleSearchItem>
        {
            Items = items,
            Page = page,
            PageSize = combinedPageSize,
            TotalItems = totalItems
        };

        _memoryCache.Set(cacheKey, paged, SearchCacheTtl);
        return paged;
    }

    public async Task<TitleDetailResponse?> GetDetailAsync(
        string source,
        string externalId,
        int? userId = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = await EnsureTitleCachedAsync(source, externalId, cancellationToken);
        if (metadata is null)
        {
            return null;
        }

        var summaryTask = _cacheRepository.GetRatingSummaryAsync(metadata.Source, metadata.ExternalId, cancellationToken);
        Task<MyTitleRating?> myRatingTask = userId is int uid
            ? _ratingRepository.GetUserRatingAsync(uid, metadata.Source, metadata.ExternalId, cancellationToken)
            : Task.FromResult<MyTitleRating?>(null);
        Task<MyTitleWatchlist?> myWatchlistTask = userId is int uid2
            ? _watchlistRepository.GetUserWatchlistItemAsync(uid2, metadata.Source, metadata.ExternalId, cancellationToken)
            : Task.FromResult<MyTitleWatchlist?>(null);

        await Task.WhenAll(summaryTask, myRatingTask, myWatchlistTask);

        var summary = await summaryTask;

        return new TitleDetailResponse
        {
            Source = metadata.Source,
            ExternalId = metadata.ExternalId,
            MediaType = metadata.MediaType,
            Title = metadata.Title,
            PosterUrl = string.IsNullOrWhiteSpace(metadata.PosterUrl) ? null : metadata.PosterUrl,
            ReleaseYear = metadata.ReleaseYear,
            Synopsis = metadata.Synopsis,
            Genres = metadata.Genres,
            CachedAt = metadata.CachedAt,
            TotalVotes = summary.TotalVotes,
            AverageScore = summary.AverageScore,
            MyRating = await myRatingTask,
            MyWatchlist = await myWatchlistTask
        };
    }

    public async Task<TitleMetadata?> EnsureTitleCachedAsync(
        string source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var (normalizedSource, normalizedId) = ValidateTitleKey(source, externalId);
        var provider = _providers[normalizedSource];
        var memoryKey = $"titles:detail:{normalizedSource}:{normalizedId}";

        if (_memoryCache.TryGetValue(memoryKey, out TitleMetadata? inMemory) && inMemory is not null)
        {
            return inMemory;
        }

        var dbCached = await _cacheRepository.GetAsync(normalizedSource, normalizedId, cancellationToken);
        if (dbCached is not null && DateTime.UtcNow - dbCached.CachedAt <= TitleDbCacheTtl)
        {
            _memoryCache.Set(memoryKey, dbCached, DetailMemoryCacheTtl);
            return dbCached;
        }

        try
        {
            var fresh = await provider.GetDetailAsync(normalizedId, cancellationToken);
            if (fresh is null)
            {
                return null;
            }

            await _cacheRepository.UpsertAsync(fresh, cancellationToken);
            _memoryCache.Set(memoryKey, fresh, DetailMemoryCacheTtl);
            return fresh;
        }
        catch (Exception ex) when (dbCached is not null && ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Serve stale DB cache when upstream API is unavailable or rate-limited.
            _logger.LogWarning(ex, "Upstream provider {Source} failed for {ExternalId}; serving stale cache.", normalizedSource, normalizedId);
            _memoryCache.Set(memoryKey, dbCached, DetailMemoryCacheTtl);
            return dbCached;
        }
    }

    public (string Source, string ExternalId) ValidateTitleKey(string source, string externalId)
    {
        var normalizedSource = source.Trim().ToLowerInvariant();
        if (!_providers.ContainsKey(normalizedSource))
        {
            throw new ArgumentException("Unsupported source. Allowed values: 'tmdb', 'jikan'.", nameof(source));
        }

        if (!int.TryParse(externalId, out var numericId) || numericId <= 0)
        {
            throw new ArgumentException("ExternalId must be a positive integer.", nameof(externalId));
        }

        return (normalizedSource, numericId.ToString());
    }

    private List<ITitleProvider> SelectProvidersForType(string normalizedType)
    {
        return normalizedType switch
        {
            "all" => _providers.Values.ToList(),
            "movie" => [_providers["tmdb"]],
            "anime" => [_providers["jikan"]],
            _ => throw new ArgumentException("Invalid type. Allowed values: 'movie', 'anime'.", nameof(normalizedType))
        };
    }
}
