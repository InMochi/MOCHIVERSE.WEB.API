using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MOCHI.VERSE.API.Class;
using MOCHI.VERSE.API.Interfaces;
using MOCHI.VERSE.API.Models;
using MOCHI.VERSE.API.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services
    .AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(options => Encoding.UTF8.GetByteCount(options.Key) >= 32, "Jwt:Key must be at least 32 bytes.")
    .ValidateOnStart();

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwtIssuer = jwtSection["Issuer"];
var jwtAudience = jwtSection["Audience"];
var jwtKey = jwtSection["Key"];

if (string.IsNullOrWhiteSpace(jwtIssuer) ||
    string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtKey) ||
    Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt configuration (Issuer, Audience, and Key >= 32 bytes) must be set.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddSingleton<Hasher>();
builder.Services.AddSingleton<AuthHelper>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
