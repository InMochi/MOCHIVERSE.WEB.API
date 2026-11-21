using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// TMDB v3 movie metadata provider.
/// </summary>
public sealed class TmdbTitleProvider : ITitleProvider
{
    private const int TmdbPageSize = 20;

    private readonly HttpClient _httpClient;
    private readonly string _imageBaseUrl;
    private readonly string? _bearerToken;

    public string Source => "tmdb";
    public string MediaType => "movie";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_bearerToken);

    public TmdbTitleProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        var baseUrl = configuration["ExternalApis:Tmdb:BaseUrl"] ?? "https://api.themoviedb.org/3";
        _imageBaseUrl = (configuration["ExternalApis:Tmdb:ImageBaseUrl"] ?? "https://image.tmdb.org/t/p/w500").TrimEnd('/');
        _bearerToken = configuration["ExternalApis:Tmdb:BearerToken"];

        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
    }

    public async Task<ProviderSearchResult> SearchAsync(string query, int page = 1, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"search/movie?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TmdbSearchResponse>(cancellationToken: cancellationToken);
        if (payload?.Results is null)
        {
            return new ProviderSearchResult { PageSize = TmdbPageSize };
        }

        var items = payload.Results
            .Where(item => item.Id > 0 && !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new TitleSearchItem
            {
                Source = Source,
                ExternalId = item.Id.ToString(),
                MediaType = MediaType,
                Title = Truncate(item.Title!, 300),
                PosterUrl = BuildPosterUrl(item.PosterPath),
                ReleaseYear = ParseYear(item.ReleaseDate),
                Synopsis = string.IsNullOrWhiteSpace(item.Overview) ? null : item.Overview
            })
            .ToList();

        return new ProviderSearchResult
        {
            Items = items,
            PageSize = TmdbPageSize,
            TotalItems = payload.TotalResults,
            TotalPages = payload.TotalPages
        };
    }

    public async Task<TitleMetadata?> GetDetailAsync(string externalId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (!int.TryParse(externalId, out var movieId) || movieId <= 0)
        {
            throw new ArgumentException("Invalid TMDB movie id.", nameof(externalId));
        }

        using var response = await _httpClient.GetAsync($"movie/{movieId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<TmdbMovieDto>(cancellationToken: cancellationToken);
        if (item is null || item.Id <= 0 || string.IsNullOrWhiteSpace(item.Title))
        {
            return null;
        }

        var genres = item.Genres?
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => g.Name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        return new TitleMetadata
        {
            Source = Source,
            ExternalId = item.Id.ToString(),
            MediaType = MediaType,
            Title = Truncate(item.Title, 300),
            PosterUrl = BuildPosterUrl(item.PosterPath),
            ReleaseYear = ParseYear(item.ReleaseDate),
            Synopsis = string.IsNullOrWhiteSpace(item.Overview) ? null : item.Overview,
            Genres = genres,
            CachedAt = DateTime.UtcNow
        };
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("TMDB provider is not configured.");
        }
    }

    private string? BuildPosterUrl(string? posterPath)
    {
        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return null;
        }

        var url = $"{_imageBaseUrl}/{posterPath.TrimStart('/')}";
        return Truncate(url, 500);
    }

    private static short? ParseYear(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate) || releaseDate.Length < 4)
        {
            return null;
        }

        return short.TryParse(releaseDate.AsSpan(0, 4), out var year) ? year : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed class TmdbSearchResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbMovieDto>? Results { get; set; }

        [JsonPropertyName("total_pages")]
        public int TotalPages { get; set; }

        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }

    private sealed class TmdbMovieDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("genres")]
        public List<TmdbGenreDto>? Genres { get; set; }
    }

    private sealed class TmdbGenreDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
