using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Generates signed HS256 JWT access tokens from configuration.
/// </summary>
public sealed class AuthHelper
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiresMinutes;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public AuthHelper(IConfiguration configuration)
    {
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];
        var key = configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience) ||
            string.IsNullOrWhiteSpace(key) ||
            Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Jwt configuration (Issuer, Audience, and Key >= 32 bytes) must be set.");
        }

        _issuer = issuer;
        _audience = audience;
        _expiresMinutes = int.TryParse(configuration["Jwt:ExpiresMinutes"], out var mins) && mins > 0 ? mins : 60;

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_expiresMinutes);

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
            Issuer = _issuer,
            Audience = _audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = _signingCredentials
        };

        return (_tokenHandler.CreateToken(descriptor), expiresAt);
    }
}
