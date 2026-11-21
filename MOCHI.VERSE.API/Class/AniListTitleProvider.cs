using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// AniList GraphQL anime metadata provider.
/// </summary>
public sealed partial class AniListTitleProvider : ITitleProvider
{
    private const int AniListPageSize = 10;

    private const string SearchQuery = """
        query ($search: String, $page: Int, $perPage: Int) {
          Page(page: $page, perPage: $perPage) {
            pageInfo {
              total
              lastPage
            }
            media(search: $search, type: ANIME, isAdult: false, sort: SEARCH_MATCH) {
              id
              title {
                romaji
                english
              }
              coverImage {
                large
              }
              seasonYear
              description(asHtml: false)
            }
          }
        }
        """;

    private const string DetailQuery = """
        query ($id: Int) {
          Media(id: $id, type: ANIME, isAdult: false) {
            id
            title {
              romaji
              english
            }
            coverImage {
              large
            }
            seasonYear
            description(asHtml: false)
            genres
          }
        }
        """;

    private readonly HttpClient _httpClient;

    public string Source => "anilist";
    public string MediaType => "anime";
    public bool IsConfigured => true;

    public AniListTitleProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        var endpoint = configuration["ExternalApis:AniList:Endpoint"] ?? "https://graphql.anilist.co";
        _httpClient.BaseAddress = new Uri(endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ProviderSearchResult> SearchAsync(string query, int page = 1, CancellationToken cancellationToken = default)
    {
        var requestBody = new GraphQlRequest
        {
            Query = SearchQuery,
            Variables = new { search = query, page, perPage = AniListPageSize }
        };

        using var response = await _httpClient.PostAsJsonAsync(string.Empty, requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GraphQlResponse<AniListSearchData>>(cancellationToken: cancellationToken);
        var pageData = payload?.Data?.Page;
        if (pageData?.Media is null)
        {
            return new ProviderSearchResult { PageSize = AniListPageSize };
        }

        var items = pageData.Media
            .Where(item => item.Id > 0 && ResolveTitle(item.Title) is not null)
            .Select(item => new TitleSearchItem
            {
                Source = Source,
                ExternalId = item.Id.ToString(),
                MediaType = MediaType,
                Title = Truncate(ResolveTitle(item.Title)!, 300),
                PosterUrl = TruncateNullable(item.CoverImage?.Large, 500),
                ReleaseYear = ResolveYear(item.SeasonYear),
                Synopsis = CleanDescription(item.Description)
            })
            .ToList();

        return new ProviderSearchResult
        {
            Items = items,
            PageSize = AniListPageSize,
            TotalItems = pageData.PageInfo?.Total ?? items.Count,
            TotalPages = pageData.PageInfo?.LastPage ?? (items.Count > 0 ? 1 : 0)
        };
    }

    public async Task<TitleMetadata?> GetDetailAsync(string externalId, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(externalId, out var anilistId) || anilistId <= 0)
        {
            throw new ArgumentException("Invalid AniList anime id.", nameof(externalId));
        }

        var requestBody = new GraphQlRequest
        {
            Query = DetailQuery,
            Variables = new { id = anilistId }
        };

        using var response = await _httpClient.PostAsJsonAsync(string.Empty, requestBody, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GraphQlResponse<AniListDetailData>>(cancellationToken: cancellationToken);
        var item = payload?.Data?.Media;
        var resolvedTitle = ResolveTitle(item?.Title);
        if (item is null || item.Id <= 0 || resolvedTitle is null)
        {
            return null;
        }

        var genres = item.Genres?
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        return new TitleMetadata
        {
            Source = Source,
            ExternalId = item.Id.ToString(),
            MediaType = MediaType,
            Title = Truncate(resolvedTitle, 300),
            PosterUrl = TruncateNullable(item.CoverImage?.Large, 500),
            ReleaseYear = ResolveYear(item.SeasonYear),
            Synopsis = CleanDescription(item.Description),
            Genres = genres,
            CachedAt = DateTime.UtcNow
        };
    }

    private static string? ResolveTitle(AniListTitleDto? title)
    {
        if (!string.IsNullOrWhiteSpace(title?.English))
        {
            return title.English.Trim();
        }

        return string.IsNullOrWhiteSpace(title?.Romaji) ? null : title.Romaji.Trim();
    }

    private static short? ResolveYear(int? year) =>
        year is >= 1900 and <= 2100 ? (short)year.Value : null;

    private static string? CleanDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var withNewlines = BreakTagRegex().Replace(raw, "\n");
        var withoutTags = HtmlTagRegex().Replace(withNewlines, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags).Trim();

        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    private sealed class GraphQlRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("variables")]
        public object? Variables { get; set; }
    }

    private sealed class GraphQlResponse<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private sealed class AniListSearchData
    {
        [JsonPropertyName("Page")]
        public AniListPageDto? Page { get; set; }
    }

    private sealed class AniListDetailData
    {
        [JsonPropertyName("Media")]
        public AniListMediaDto? Media { get; set; }
    }

    private sealed class AniListPageDto
    {
        [JsonPropertyName("pageInfo")]
        public AniListPageInfoDto? PageInfo { get; set; }

        [JsonPropertyName("media")]
        public List<AniListMediaDto>? Media { get; set; }
    }

    private sealed class AniListPageInfoDto
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("lastPage")]
        public int LastPage { get; set; }
    }

    private sealed class AniListMediaDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public AniListTitleDto? Title { get; set; }

        [JsonPropertyName("coverImage")]
        public AniListCoverImageDto? CoverImage { get; set; }

        [JsonPropertyName("seasonYear")]
        public int? SeasonYear { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("genres")]
        public List<string>? Genres { get; set; }
    }

    private sealed class AniListTitleDto
    {
        [JsonPropertyName("romaji")]
        public string? Romaji { get; set; }

        [JsonPropertyName("english")]
        public string? English { get; set; }
    }

    private sealed class AniListCoverImageDto
    {
        [JsonPropertyName("large")]
        public string? Large { get; set; }
    }
}
