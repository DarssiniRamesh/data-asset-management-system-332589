using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DataAssetBackend.Infrastructure.Auth;

/// <summary>
/// JWT token creation helpers used by dev login and future authentication flows.
/// </summary>
public sealed class JwtTokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _signingKey;

    /// <summary>
    /// Creates a new JWT token service.
    /// </summary>
    /// <param name="issuer">JWT issuer.</param>
    /// <param name="audience">JWT audience.</param>
    /// <param name="signingKey">Signing key (HMAC-SHA256). Must be non-empty.</param>
    public JwtTokenService(string issuer, string audience, string signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new ArgumentException("JWT signing key must be provided.", nameof(signingKey));
        }

        _issuer = issuer;
        _audience = audience;
        _signingKey = signingKey;
    }

    /// <summary>
    /// Mints a signed JWT for the given user identity and role.
    /// </summary>
    /// <param name="username">Username (subject/name).</param>
    /// <param name="role">Role (Admin/Editor/Viewer).</param>
    /// <param name="expiresIn">Lifetime duration.</param>
    /// <returns>Serialized JWT.</returns>
    public string CreateToken(string username, string role, TimeSpan expiresIn)
    {
        var now = DateTimeOffset.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new(ClaimTypes.Name, username),
            // ASP.NET uses ClaimTypes.Role by default for role authorization.
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(expiresIn).UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates TokenValidationParameters matching this service's signing configuration.
    /// </summary>
    /// <returns>Validation parameters for JWT bearer authentication.</returns>
    public TokenValidationParameters CreateValidationParameters()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,

            ValidateAudience = true,
            ValidAudience = _audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
    }
}
