using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Repositories;

/// <summary>
/// ADO.NET repository for dbo.TitleCache and dbo.vw_TitleRatingSummary.
/// </summary>
public sealed class TitleCacheRepository : ITitleCacheRepository
{
    private readonly IConfiguration _configuration;

    public TitleCacheRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<TitleMetadata?> GetAsync(string source, string externalId, CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT TOP (1) Source, ExternalId, MediaType, Title, PosterUrl, ReleaseYear, Synopsis, Genres, CachedAt
            FROM dbo.TitleCache
            WHERE Source = @Source AND ExternalId = @ExternalId;
            """;
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);

        return await db.QueryFirstOrDefaultAsync(MapTitle, cancellationToken);
    }

    public async Task UpsertAsync(TitleMetadata title, CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            MERGE dbo.TitleCache WITH (HOLDLOCK) AS target
            USING (SELECT @Source AS Source, @ExternalId AS ExternalId) AS src
               ON target.Source = src.Source AND target.ExternalId = src.ExternalId
            WHEN MATCHED THEN
                UPDATE SET
                    MediaType   = @MediaType,
                    Title       = @Title,
                    PosterUrl   = @PosterUrl,
                    ReleaseYear = @ReleaseYear,
                    Synopsis    = @Synopsis,
                    Genres      = @Genres,
                    CachedAt    = @CachedAt
            WHEN NOT MATCHED THEN
                INSERT (Source, ExternalId, MediaType, Title, PosterUrl, ReleaseYear, Synopsis, Genres, CachedAt)
                VALUES (@Source, @ExternalId, @MediaType, @Title, @PosterUrl, @ReleaseYear, @Synopsis, @Genres, @CachedAt);
            """;
        db.AddParameter("@Source", title.Source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", title.ExternalId, SqlDbType.VarChar, 50);
        db.AddParameter("@MediaType", title.MediaType, SqlDbType.VarChar, 10);
        db.AddParameter("@Title", title.Title, SqlDbType.NVarChar, 300);
        db.AddParameter("@PosterUrl", string.IsNullOrWhiteSpace(title.PosterUrl) ? null : title.PosterUrl, SqlDbType.NVarChar, 500);
        db.AddParameter("@ReleaseYear", title.ReleaseYear, SqlDbType.SmallInt);
        db.AddParameter("@Synopsis", title.Synopsis, SqlDbType.NVarChar, -1);
        db.AddParameter("@Genres", SerializeGenres(title.Genres), SqlDbType.NVarChar, 500);
        db.AddParameter("@CachedAt", title.CachedAt, SqlDbType.DateTime2);

        await db.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TitleRatingSummary> GetRatingSummaryAsync(
        string source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT TOP (1) TotalVotes, AverageScore
            FROM dbo.vw_TitleRatingSummary
            WHERE Source = @Source AND ExternalId = @ExternalId;
            """;
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);

        var summary = await db.QueryFirstOrDefaultAsync(reader => new TitleRatingSummary
        {
            TotalVotes = reader.GetInt32(reader.GetOrdinal("TotalVotes")),
            AverageScore = reader.GetDecimal(reader.GetOrdinal("AverageScore"))
        }, cancellationToken);

        return summary ?? new TitleRatingSummary();
    }

    private static TitleMetadata MapTitle(SqlDataReader reader)
    {
        var posterRaw = reader.IsDBNull(reader.GetOrdinal("PosterUrl"))
            ? null
            : reader.GetString(reader.GetOrdinal("PosterUrl"));

        var genresRaw = reader.IsDBNull(reader.GetOrdinal("Genres"))
            ? null
            : reader.GetString(reader.GetOrdinal("Genres"));

        return new TitleMetadata
        {
            Source = reader.GetString(reader.GetOrdinal("Source")),
            ExternalId = reader.GetString(reader.GetOrdinal("ExternalId")),
            MediaType = reader.GetString(reader.GetOrdinal("MediaType")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            PosterUrl = string.IsNullOrWhiteSpace(posterRaw) ? null : posterRaw,
            ReleaseYear = reader.IsDBNull(reader.GetOrdinal("ReleaseYear")) ? null : reader.GetInt16(reader.GetOrdinal("ReleaseYear")),
            Synopsis = reader.IsDBNull(reader.GetOrdinal("Synopsis")) ? null : reader.GetString(reader.GetOrdinal("Synopsis")),
            Genres = DeserializeGenres(genresRaw),
            CachedAt = reader.GetDateTime(reader.GetOrdinal("CachedAt"))
        };
    }

    private static string? SerializeGenres(List<string> genres)
    {
        var cleaned = genres
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
        {
            return null;
        }

        while (cleaned.Count > 0)
        {
            var json = JsonSerializer.Serialize(cleaned);
            if (json.Length <= 500)
            {
                return json;
            }

            cleaned.RemoveAt(cleaned.Count - 1);
        }

        return null;
    }

    private static List<string> DeserializeGenres(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
