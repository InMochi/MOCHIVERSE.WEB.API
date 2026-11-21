namespace MOCHI.VERSE.API.Models;

/// <summary>
/// Standard API response wrapper.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
    public IReadOnlyList<string>? Errors { get; init; }

    public static ApiResponse<T> Ok(T data, string message = "Success") =>
        new()
        {
            Success = true,
            Message = message,
            Data = data
        };

    public static ApiResponse<T> Fail(string message, IReadOnlyList<string>? errors = null) =>
        new()
        {
            Success = false,
            Message = message,
            Errors = errors
        };
}
