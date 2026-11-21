using System.Data;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Repositories;

/// <summary>
/// ADO.NET repository for dbo.Ratings.
/// </summary>
public sealed class RatingRepository : IRatingRepository
{
    private readonly IConfiguration _configuration;

    public RatingRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<RatingRecord> UpsertAsync(
        int userId,
        string source,
        string externalId,
        byte score,
        string? review,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            MERGE dbo.Ratings WITH (HOLDLOCK) AS target
            USING (SELECT @UserId AS UserId, @Source AS Source, @ExternalId AS ExternalId) AS src
               ON target.UserId = src.UserId
              AND target.Source = src.Source
              AND target.ExternalId = src.ExternalId
            WHEN MATCHED THEN
                UPDATE SET
                    Score     = @Score,
                    Review    = @Review,
                    UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (UserId, Source, ExternalId, Score, Review)
                VALUES (@UserId, @Source, @ExternalId, @Score, @Review);

            SELECT TOP (1)
                r.Id, r.UserId, u.Username, r.Source, r.ExternalId,
                r.Score, r.Review, r.CreatedAt, r.UpdatedAt
            FROM dbo.Ratings AS r
            INNER JOIN dbo.Users AS u ON u.Id = r.UserId
            WHERE r.UserId = @UserId AND r.Source = @Source AND r.ExternalId = @ExternalId;
            """;
        db.AddParameter("@UserId", userId, SqlDbType.Int);
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);
        db.AddParameter("@Score", score, SqlDbType.TinyInt);
        db.AddParameter("@Review", review, SqlDbType.NVarChar, 2000);

        return await db.QueryFirstOrDefaultAsync(MapRatingRecord, cancellationToken)
            ?? throw new InvalidOperationException("Failed to save rating.");
    }

    public async Task<bool> DeleteAsync(
        int userId,
        string source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            DELETE FROM dbo.Ratings
            WHERE UserId = @UserId AND Source = @Source AND ExternalId = @ExternalId;
            """;
        db.AddParameter("@UserId", userId, SqlDbType.Int);
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);

        var affected = await db.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<MyTitleRating?> GetUserRatingAsync(
        int userId,
        string source,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT TOP (1) Id, Score, Review, CreatedAt, UpdatedAt
            FROM dbo.Ratings
            WHERE UserId = @UserId AND Source = @Source AND ExternalId = @ExternalId;
            """;
        db.AddParameter("@UserId", userId, SqlDbType.Int);
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);

        return await db.QueryFirstOrDefaultAsync(reader => new MyTitleRating
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Score = reader.GetByte(reader.GetOrdinal("Score")),
            Review = reader.IsDBNull(reader.GetOrdinal("Review")) ? null : reader.GetString(reader.GetOrdinal("Review")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        }, cancellationToken);
    }

    public async Task<List<RatingItem>> GetByTitleAsync(
        string source,
        string externalId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT
                r.Id, u.Username, r.Score, r.Review, r.CreatedAt, r.UpdatedAt
            FROM dbo.Ratings AS r
            INNER JOIN dbo.Users AS u ON u.Id = r.UserId
            WHERE r.Source = @Source AND r.ExternalId = @ExternalId AND u.IsActive = 1
            ORDER BY COALESCE(r.UpdatedAt, r.CreatedAt) DESC, r.Id DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        db.AddParameter("@Source", source, SqlDbType.VarChar, 20);
        db.AddParameter("@ExternalId", externalId, SqlDbType.VarChar, 50);
        db.AddParameter("@Offset", (page - 1) * pageSize, SqlDbType.Int);
        db.AddParameter("@PageSize", pageSize, SqlDbType.Int);

        return await db.QueryListAsync(MapRatingItem, cancellationToken);
    }

    private static RatingRecord MapRatingRecord(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
            Username = reader.GetString(reader.GetOrdinal("Username")),
            Source = reader.GetString(reader.GetOrdinal("Source")),
            ExternalId = reader.GetString(reader.GetOrdinal("ExternalId")),
            Score = reader.GetByte(reader.GetOrdinal("Score")),
            Review = reader.IsDBNull(reader.GetOrdinal("Review")) ? null : reader.GetString(reader.GetOrdinal("Review")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        };

    private static RatingItem MapRatingItem(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Username = reader.GetString(reader.GetOrdinal("Username")),
            Score = reader.GetByte(reader.GetOrdinal("Score")),
            Review = reader.IsDBNull(reader.GetOrdinal("Review")) ? null : reader.GetString(reader.GetOrdinal("Review")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        };
}
