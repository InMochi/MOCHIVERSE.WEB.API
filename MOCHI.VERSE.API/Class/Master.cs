using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Shared API response and claims helpers.
/// </summary>
public static class Master
{
    public static APIResponse CreateAPIResponse(
        bool ok,
        string? message = null,
        object? data = null,
        IDictionary<string, string[]>? errors = null) =>
        new()
        {
            Ok = ok,
            Message = message,
            Data = data,
            Errors = errors
        };

    public static int? GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(sub, out var userId) && userId > 0 ? userId : null;
    }
}
