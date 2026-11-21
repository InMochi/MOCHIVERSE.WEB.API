using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Jikan v4 (MyAnimeList) anime metadata provider.
/// </summary>
public sealed class JikanTitleProvider : ITitleProvider
{
    private const int JikanPageSize = 10;

    private readonly HttpClient _httpClient;

    public string Source => "jikan";
    public string MediaType => "anime";
    public bool IsConfigured => true;

    public JikanTitleProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        var baseUrl = configuration["ExternalApis:Jikan:BaseUrl"] ?? "https://api.jikan.moe/v4";
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ProviderSearchResult> SearchAsync(string query, int page = 1, CancellationToken cancellationToken = default)
    {
        var url = $"anime?q={Uri.EscapeDataString(query)}&page={page}&limit={JikanPageSize}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JikanSearchResponse>(cancellationToken: cancellationToken);
        if (payload?.Data is null)
        {
            return new ProviderSearchResult { PageSize = JikanPageSize };
        }

        var items = payload.Data
            .Where(item => item.MalId > 0 && !string.IsNullOrWhiteSpace(item.Title))
            .Select(item => new TitleSearchItem
            {
                Source = Source,
                ExternalId = item.MalId.ToString(),
                MediaType = MediaType,
                Title = Truncate(item.Title!, 300),
                PosterUrl = TruncateNullable(item.Images?.Jpg?.ImageUrl, 500),
                ReleaseYear = ResolveYear(item),
                Synopsis = string.IsNullOrWhiteSpace(item.Synopsis) ? null : item.Synopsis
            })
            .ToList();

        return new ProviderSearchResult
        {
            Items = items,
            PageSize = JikanPageSize,
            TotalItems = payload.Pagination?.Items?.Total ?? items.Count,
            TotalPages = payload.Pagination?.LastVisiblePage ?? (items.Count > 0 ? 1 : 0)
        };
    }

    public async Task<TitleMetadata?> GetDetailAsync(string externalId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalId, out var malId) || malId <= 0)
        {
            throw new ArgumentException("Invalid Jikan anime id.", nameof(externalId));
        }

        using var response = await _httpClient.GetAsync($"anime/{malId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JikanDetailResponse>(cancellationToken: cancellationToken);
        var item = payload?.Data;
        if (item is null || item.MalId <= 0 || string.IsNullOrWhiteSpace(item.Title))
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
            ExternalId = item.MalId.ToString(),
            MediaType = MediaType,
            Title = Truncate(item.Title, 300),
            PosterUrl = TruncateNullable(item.Images?.Jpg?.ImageUrl, 500),
            ReleaseYear = ResolveYear(item),
            Synopsis = string.IsNullOrWhiteSpace(item.Synopsis) ? null : item.Synopsis,
            Genres = genres,
            CachedAt = DateTime.UtcNow
        };
    }

    private static short? ResolveYear(JikanAnimeDto item)
    {
        var year = item.Year ?? item.Aired?.Prop?.From?.Year;
        return year is >= 1900 and <= 2100 ? (short)year.Value : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    private sealed class JikanSearchResponse
    {
        [JsonPropertyName("pagination")]
        public JikanPaginationDto? Pagination { get; set; }

        [JsonPropertyName("data")]
        public List<JikanAnimeDto>? Data { get; set; }
    }

    private sealed class JikanPaginationDto
    {
        [JsonPropertyName("last_visible_page")]
        public int LastVisiblePage { get; set; }

        [JsonPropertyName("items")]
        public JikanPaginationItemsDto? Items { get; set; }
    }

    private sealed class JikanPaginationItemsDto
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class JikanDetailResponse
    {
        [JsonPropertyName("data")]
        public JikanAnimeDto? Data { get; set; }
    }

    private sealed class JikanAnimeDto
    {
        [JsonPropertyName("mal_id")]
        public int MalId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("synopsis")]
        public string? Synopsis { get; set; }

        [JsonPropertyName("images")]
        public JikanImagesDto? Images { get; set; }

        [JsonPropertyName("aired")]
        public JikanAiredDto? Aired { get; set; }

        [JsonPropertyName("genres")]
        public List<JikanGenreDto>? Genres { get; set; }
    }

    private sealed class JikanImagesDto
    {
        [JsonPropertyName("jpg")]
        public JikanJpgDto? Jpg { get; set; }
    }

    private sealed class JikanJpgDto
    {
        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }
    }

    private sealed class JikanAiredDto
    {
        [JsonPropertyName("prop")]
        public JikanAiredPropDto? Prop { get; set; }
    }

    private sealed class JikanAiredPropDto
    {
        [JsonPropertyName("from")]
        public JikanDatePartDto? From { get; set; }
    }

    private sealed class JikanDatePartDto
    {
        [JsonPropertyName("year")]
        public int? Year { get; set; }
    }

    private sealed class JikanGenreDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
