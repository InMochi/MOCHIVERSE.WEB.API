using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Shared API response factory.
/// </summary>
public static class Master
{
    public static APIResponse CreateAPIResponse(bool ok, string? message = null, object? data = null) =>
        new()
        {
            Ok = ok,
            Message = message,
            Data = data
        };
}
