using System.Security.Cryptography;
using System.Text;
using JobSuites.Api.Auth;
using JobSuites.Api.Endpoints;
using JobSuites.Api.Services;
using JobSuites.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
jwt.Validate();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<MatchService>();
builder.Services.AddSingleton<UploadStore>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// The rate limiter is keyed on remote IP, and every in-process test client
// shares one address, so a full test class would trip it and fail for the wrong
// reason. Tests opt out here; limiting itself is verified against a live server.
if (app.Configuration.GetValue("RateLimiting:Enabled", defaultValue: true))
{
    app.UseRateLimiter();
}
app.UseAuthentication();
app.UseAuthorization();

// The ingest endpoint is authenticated with a shared key. Failing fast here
// means a deployment can never expose write access to job data by accident.
var ingestKey = builder.Configuration["Ingest:Key"];
if (string.IsNullOrWhiteSpace(ingestKey) || ingestKey.Length < 32)
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Ingest:Key must be configured with at least 32 characters. " +
            "Generate one with: openssl rand -base64 48");
    }

    ingestKey = "dev-insecure-ingest-key-do-not-use-outside-development";
}

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/ingest"))
    {
        var hasKey = ctx.Request.Headers.TryGetValue("X-Ingest-Key", out var provided);
        var fixedKey = Encoding.UTF8.GetBytes(ingestKey!);
        var providedBytes = hasKey
            ? Encoding.UTF8.GetBytes(provided.ToString())
            : Array.Empty<byte>();

        // A missing header is as much a rejection as a wrong one. Constant-time
        // comparison keeps the 401 path free of length-oracle timing leaks.
        if (!hasKey || !CryptographicOperations.FixedTimeEquals(providedBytes, fixedKey))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "ApiKey";
            await ctx.Response.WriteAsJsonAsync(new { title = "Invalid or missing ingest key" });
            return;
        }
    }

    await next();
});

app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapDashboardEndpoints();
app.MapIngestEndpoints();
app.MapRolesEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous()
   .WithTags("Ops");

app.Run();
