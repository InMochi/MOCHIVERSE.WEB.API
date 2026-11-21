namespace MOCHI.VERSE.API.Models;

/// <summary>
/// Standard API response wrapper.
/// </summary>
public class APIResponse
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
}
