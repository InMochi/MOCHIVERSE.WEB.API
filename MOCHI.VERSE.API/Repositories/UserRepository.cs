using System.Data;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Repositories;

/// <summary>
/// ADO.NET repository for dbo.Users.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly IConfiguration _configuration;

    public UserRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<User?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT TOP (1) Id, Username, Email, PasswordHash, Role, IsActive, CreatedAt, UpdatedAt
            FROM dbo.Users
            WHERE Username = @Identifier OR Email = @Identifier;
            """;
        db.AddParameter("@Identifier", identifier, SqlDbType.NVarChar, 255);

        return await db.QueryFirstOrDefaultAsync(MapUser, cancellationToken);
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT TOP (1) Id, Username, Email, PasswordHash, Role, IsActive, CreatedAt, UpdatedAt
            FROM dbo.Users
            WHERE Id = @Id;
            """;
        db.AddParameter("@Id", id, SqlDbType.Int);

        return await db.QueryFirstOrDefaultAsync(MapUser, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Users WHERE Username = @Username OR Email = @Email
            ) THEN 1 ELSE 0 END;
            """;
        db.AddParameter("@Username", username, SqlDbType.NVarChar, 50);
        db.AddParameter("@Email", email, SqlDbType.NVarChar, 255);

        var result = await db.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task<User> CreateAsync(string username, string email, string passwordHash, string role,    
        CancellationToken cancellationToken = default)
    {
        await using var db = new SqlHelper(_configuration);
        db.CommandText = """
            INSERT INTO dbo.Users (Username, Email, PasswordHash, Role)
            OUTPUT INSERTED.Id, INSERTED.Username, INSERTED.Email, INSERTED.PasswordHash,
                   INSERTED.Role, INSERTED.IsActive, INSERTED.CreatedAt, INSERTED.UpdatedAt
            VALUES (@Username, @Email, @PasswordHash, @Role);
            """;
        db.AddParameter("@Username", username, SqlDbType.NVarChar, 50);
        db.AddParameter("@Email", email, SqlDbType.NVarChar, 255);
        db.AddParameter("@PasswordHash", passwordHash, SqlDbType.NVarChar, 255);
        db.AddParameter("@Role", role, SqlDbType.NVarChar, 30);

        return await db.QueryFirstOrDefaultAsync(MapUser, cancellationToken)
            ?? throw new InvalidOperationException("Failed to insert user record.");
    }

    private static User MapUser(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Username = reader.GetString(reader.GetOrdinal("Username")),
            Email = reader.GetString(reader.GetOrdinal("Email")),
            PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
            Role = reader.GetString(reader.GetOrdinal("Role")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("UpdatedAt"))
        };
}
