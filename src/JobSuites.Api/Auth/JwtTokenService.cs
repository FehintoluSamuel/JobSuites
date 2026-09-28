using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace JobSuites.Api.Auth;

public sealed record JwtOptions
{
    public const string Section = "Jwt";

    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string Key { get; init; }
    public int ExpiresMinutes { get; init; } = 60 * 24;

    /// <summary>A signing key shorter than 32 bytes is rejected by the JWT
    /// library at runtime. Worth failing fast and loudly at startup instead.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || Encoding.UTF8.GetByteCount(Key) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must be at least 32 bytes. Generate one with: " +
                "openssl rand -base64 48");
        }
    }
}

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) Create(Guid id, string email, string fullName);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) Create(Guid id, string email, string fullName)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_options.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, id.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Name, fullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
