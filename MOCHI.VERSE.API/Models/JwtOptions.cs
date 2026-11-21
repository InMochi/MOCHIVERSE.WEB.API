using System.ComponentModel.DataAnnotations;

namespace MOCHI.VERSE.API.Models;

/// <summary>
/// Configuration options bound from the "Jwt" section.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required]
    [MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiresMinutes { get; set; } = 60;
}
