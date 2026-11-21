using System.Data;
using Microsoft.Data.SqlClient;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Repositories;

/// <summary>
/// ADO.NET repository for dbo.Users.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly string _connectionString;

    public UserRepository(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        _connectionString = connectionString;
    }

    public async Task<User?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1) Id, Username, Email, PasswordHash, Role, IsActive, CreatedAt, UpdatedAt
            FROM dbo.Users
            WHERE Username = @Identifier OR Email = @Identifier;
            """;

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Identifier", SqlDbType.NVarChar, 255).Value = identifier;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1) Id, Username, Email, PasswordHash, Role, IsActive, CreatedAt, UpdatedAt
            FROM dbo.Users
            WHERE Id = @Id;
            """;

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapUser(reader) : null;
    }

    public async Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Users WHERE Username = @Username OR Email = @Email
            ) THEN 1 ELSE 0 END;
            """;

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Username", SqlDbType.NVarChar, 50).Value = username;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 255).Value = email;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    public async Task<User> CreateAsync(
        string username,
        string email,
        string passwordHash,
        string role,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.Users (Username, Email, PasswordHash, Role)
            OUTPUT INSERTED.Id, INSERTED.Username, INSERTED.Email, INSERTED.PasswordHash,
                   INSERTED.Role, INSERTED.IsActive, INSERTED.CreatedAt, INSERTED.UpdatedAt
            VALUES (@Username, @Email, @PasswordHash, @Role);
            """;

        await using var connection = await CreateOpenConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Username", SqlDbType.NVarChar, 50).Value = username;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 255).Value = email;
        command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 255).Value = passwordHash;
        command.Parameters.Add("@Role", SqlDbType.NVarChar, 30).Value = role;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Failed to insert user record.");
        }

        return MapUser(reader);
    }

    private async Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
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
