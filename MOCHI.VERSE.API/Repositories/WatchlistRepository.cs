using System.Data;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Repositories;

/// <summary>
/// ADO.NET repository for dbo.Watchlists joined with dbo.TitleCache.
/// </summary>
public sealed class WatchlistRepository : IWatchlistRepository
{
    private readonly IConfiguration _configuration;

    public WatchlistRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<WatchlistItem> UpsertAsync(
        int userId,
        string source,
        string externalId,
        string status,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            MERGE dbo.Watchlists WITH (HOLDLOCK) AS target
            USING (SELECT @UserId AS UserId, @Source AS Source, @ExternalId AS ExternalId) AS src
               ON target.UserId = src.UserId
              AND target.Source = src.Source
              AND target.ExternalId = src.ExternalId
            WHEN MATCHED THEN
                UPDATE SET
                    Status    = @Status,
                    UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (UserId, Source, ExternalId, Status)
                VALUES (@UserId, @Source, @ExternalId, @Status);

            SELECT TOP (1)
                w.Id, w.Source, w.ExternalId, w.Status, w.CreatedAt, w.UpdatedAt,
                tc.MediaType, tc.Title, tc.PosterUrl, tc.ReleaseYear
            FROM dbo.Watchlists AS w
            LEFT JOIN dbo.TitleCache AS tc
                ON tc.Source = w.Source AND tc.ExternalId = w.ExternalId
            WHERE w.UserId = @UserId AND w.Source = @Source AND w.ExternalId = @ExternalId;
            """;
        db.AddParameter("@UserId", userId, SqlDbType.Int);
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);
        db.AddParameter("@Status", status, SqlDbType.VarChar, 20);

        return await db.QueryFirstOrDefaultAsync(MapWatchlistItem, cancellationToken)
            ?? throw new InvalidOperationException("Failed to save watchlist item.");
    }

    public async Task<bool> DeleteAsync(
        int userId,
        string source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            DELETE FROM dbo.Watchlists
            WHERE UserId = @UserId AND Source = @Source AND ExternalId = @ExternalId;
            """;
        db.AddParameter("@UserId", userId, SqlDbType.Int);
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);

        var affected = await db.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<List<WatchlistItem>> GetByUserAsync(
        int userId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT
                w.Id, w.Source, w.ExternalId, w.Status, w.CreatedAt, w.UpdatedAt,
                tc.MediaType, tc.Title, tc.PosterUrl, tc.ReleaseYear
            FROM dbo.Watchlists AS w
            LEFT JOIN dbo.TitleCache AS tc
                ON tc.Source = w.Source AND tc.ExternalId = w.ExternalId
            WHERE w.UserId = @UserId
              AND (@Status IS NULL OR w.Status = @Status)
            ORDER BY COALESCE(w.UpdatedAt, w.CreatedAt) DESC, w.Id DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        db.AddParameter("@UserId", userId, SqlDbType.Int);
        db.AddParameter("@Status", status, SqlDbType.VarChar, 20);
        db.AddParameter("@Offset", (page - 1) * pageSize, SqlDbType.Int);
        db.AddParameter("@PageSize", pageSize, SqlDbType.Int);

        return await db.QueryListAsync(MapWatchlistItem, cancellationToken);
    }

    private static WatchlistItem MapWatchlistItem(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Source = reader.GetString(reader.GetOrdinal("Source")),
            ExternalId = reader.GetString(reader.GetOrdinal("ExternalId")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            MediaType = reader.IsDBNull(reader.GetOrdinal("MediaType")) ? null : reader.GetString(reader.GetOrdinal("MediaType")),
            Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? null : reader.GetString(reader.GetOrdinal("Title")),
            PosterUrl = reader.IsDBNull(reader.GetOrdinal("PosterUrl")) ? null : reader.GetString(reader.GetOrdinal("PosterUrl")),
            ReleaseYear = reader.IsDBNull(reader.GetOrdinal("ReleaseYear")) ? null : reader.GetInt16(reader.GetOrdinal("ReleaseYear")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        };
}
