using System.ComponentModel.DataAnnotations;

namespace MOCHI.VERSE.API.Models;

/// <summary>
/// Database entity representing a row in dbo.Users.
/// </summary>
public sealed class User
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string Role { get; init; } = "User";
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record RegisterRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(50)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Username may only contain letters, numbers, '.', '_', or '-'.")]
    public string Username { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    [Required]
    [MaxLength(255)]
    public string Identifier { get; init; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed record UserProfileResponse(
    int Id,
    string Username,
    string Email,
    string Role,
    DateTime CreatedAt);

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    UserProfileResponse User);
