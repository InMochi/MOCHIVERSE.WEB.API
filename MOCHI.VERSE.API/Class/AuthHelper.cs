using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Generates signed HS256 JWT access tokens.
/// </summary>
public sealed class AuthHelper
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public AuthHelper(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        var keyBytes = Encoding.UTF8.GetBytes(_options.Key);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
        }

        var securityKey = new SymmetricSecurityKey(keyBytes);
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.ExpiresMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.UniqueName] = user.Username,
            [JwtRegisteredClaimNames.Email] = user.Email,
            [ClaimTypes.Role] = user.Role,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString()
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = _signingCredentials
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return (token, expiresAt);
    }
}
