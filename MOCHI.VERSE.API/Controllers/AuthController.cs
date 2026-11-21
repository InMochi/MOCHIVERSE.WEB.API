using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.JsonWebTokens;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;

namespace MOCHI.VERSE.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly Hasher _hasher;
    private readonly AuthHelper _authHelper;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepository,
        Hasher hasher,
        AuthHelper authHelper,
        ILogger<AuthController> logger)
    {
        _userRepository = userRepository;
        _hasher = hasher;
        _authHelper = authHelper;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        try
        {
            if (await _userRepository.ExistsAsync(username, email, cancellationToken))
            {
                return Conflict(ApiResponse<AuthResponse>.Fail("Username or email is already registered."));
            }

            var passwordHash = await _hasher.HashPasswordAsync(request.Password);
            var user = await _userRepository.CreateAsync(username, email, passwordHash, "User", cancellationToken);

            var (token, expiresAt) = _authHelper.GenerateToken(user);
            var payload = new AuthResponse(token, expiresAt, ToProfile(user));

            return StatusCode(StatusCodes.Status201Created, ApiResponse<AuthResponse>.Ok(payload, "User registered."));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(ApiResponse<AuthResponse>.Fail("Username or email is already registered."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error during user registration.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<AuthResponse>.Fail("An unexpected error occurred."));
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var identifier = request.Identifier.Trim();
        if (identifier.Contains('@'))
        {
            identifier = identifier.ToLowerInvariant();
        }

        try
        {
            var user = await _userRepository.GetByIdentifierAsync(identifier, cancellationToken);
            if (user is null || !user.IsActive)
            {
                return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid credentials."));
            }

            var isValidPassword = await _hasher.VerifyPasswordAsync(request.Password, user.PasswordHash);
            if (!isValidPassword)
            {
                return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid credentials."));
            }

            var (token, expiresAt) = _authHelper.GenerateToken(user);
            var payload = new AuthResponse(token, expiresAt, ToProfile(user));

            return Ok(ApiResponse<AuthResponse>.Ok(payload, "Login successful."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error during login.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<AuthResponse>.Fail("An unexpected error occurred."));
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserProfileResponse>>> Me(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(sub, out var userId))
        {
            return Unauthorized(ApiResponse<UserProfileResponse>.Fail("Invalid token."));
        }

        try
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user is null || !user.IsActive)
            {
                return Unauthorized(ApiResponse<UserProfileResponse>.Fail("Unauthorized."));
            }

            return Ok(ApiResponse<UserProfileResponse>.Ok(ToProfile(user)));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while loading current user profile.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<UserProfileResponse>.Fail("An unexpected error occurred."));
        }
    }

    private static UserProfileResponse ToProfile(User user) =>
        new(user.Id, user.Username, user.Email, user.Role, user.CreatedAt);
}
