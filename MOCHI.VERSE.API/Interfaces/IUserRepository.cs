using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Interfaces;

/// <summary>
/// Data access contract for dbo.Users.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default);
    Task<User> CreateAsync(string username, string email, string passwordHash, string role, CancellationToken cancellationToken = default);
    Task RevokeTokenAsync(string jti, int userId, DateTime expiresAt, CancellationToken cancellationToken = default);
    Task<bool> IsTokenRevokedAsync(string jti, CancellationToken cancellationToken = default);
}
