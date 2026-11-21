using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
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
    public const string AccessCookieName = "__Host-mochi_access";
    public const string CsrfCookieName = "XSRF-TOKEN";
    public const string CsrfHeaderName = "X-XSRF-TOKEN";

    private readonly IUserRepository _userRepository;
    private readonly Hasher _hasher;
    private readonly AuthHelper _authHelper;
    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepository,
        Hasher hasher,
        AuthHelper authHelper,
        IAntiforgery antiforgery,
        ILogger<AuthController> logger)
    {
        _userRepository = userRepository;
        _hasher = hasher;
        _authHelper = authHelper;
        _antiforgery = antiforgery;
        _logger = logger;
    }

    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<APIResponse> GetCsrfToken()
    {
        Response.Headers.CacheControl = "no-store";

        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "Failed to generate CSRF token."));
        }

        Response.Cookies.Append(CsrfCookieName, tokens.RequestToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true
        });

        var payload = new CsrfTokenData
        {
            HeaderName = CsrfHeaderName,
            RequestToken = tokens.RequestToken
        };

        return Ok(Master.CreateAPIResponse(true, data: payload));
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<APIResponse>> Register(
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
                return Conflict(Master.CreateAPIResponse(false, "Username or email is already registered."));
            }

            var passwordHash = await _hasher.HashPasswordAsync(request.Password);
            var user = await _userRepository.CreateAsync(username, email, passwordHash, "User", cancellationToken);

            var (token, expiresAt) = _authHelper.GenerateToken(user);
            AppendAuthCookie(token, expiresAt);

            var data = new AuthData
            {
                ExpiresAt = expiresAt,
                User = ToProfile(user)
            };

            return StatusCode(StatusCodes.Status201Created, Master.CreateAPIResponse(true, "User registered.", data));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return Conflict(Master.CreateAPIResponse(false, "Username or email is already registered."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error during user registration.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<APIResponse>> Login(
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
                return Unauthorized(Master.CreateAPIResponse(false, "Invalid credentials."));
            }

            var isValidPassword = await _hasher.VerifyPasswordAsync(request.Password, user.PasswordHash);
            if (!isValidPassword)
            {
                return Unauthorized(Master.CreateAPIResponse(false, "Invalid credentials."));
            }

            var (token, expiresAt) = _authHelper.GenerateToken(user);
            AppendAuthCookie(token, expiresAt);

            var data = new AuthData
            {
                ExpiresAt = expiresAt,
                User = ToProfile(user)
            };

            return Ok(Master.CreateAPIResponse(true, "Login successful.", data));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error during login.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<APIResponse>> Logout(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = Master.GetUserId(User);
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

        if (userId is null || string.IsNullOrWhiteSpace(jti) || !long.TryParse(expClaim, out var expSeconds))
        {
            return Unauthorized(Master.CreateAPIResponse(false, "Invalid token."));
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;

        try
        {
            await _userRepository.RevokeTokenAsync(jti, userId.Value, expiresAt, cancellationToken);
            DeleteAuthCookie();
            return Ok(Master.CreateAPIResponse(true, "Logged out successfully."));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error during logout.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<APIResponse>> Me(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var userId = Master.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized(Master.CreateAPIResponse(false, "Invalid token."));
        }

        try
        {
            var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
            if (user is null || !user.IsActive)
            {
                return Unauthorized(Master.CreateAPIResponse(false, "Unauthorized."));
            }

            return Ok(Master.CreateAPIResponse(true, data: ToProfile(user)));
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error while loading current user profile.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                Master.CreateAPIResponse(false, "An unexpected error occurred."));
        }
    }

    private void AppendAuthCookie(string token, DateTime expiresAt)
    {
        Response.Cookies.Append(AccessCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)),
            IsEssential = true
        });
    }

    private void DeleteAuthCookie()
    {
        Response.Cookies.Delete(AccessCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
    }

    private static UserProfile ToProfile(User user) =>
        new()
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        };
}
