using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Infrastructure.Auth;

/// <summary>
/// Request payload for the dev login endpoint that mints JWTs.
/// </summary>
public sealed class DevLoginRequest
{
    /// <summary>
    /// Username to embed in the JWT subject and name claim.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Role to embed in the JWT. Must be one of: Admin, Editor, Viewer.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Role { get; set; } = "Viewer";
}

/// <summary>
/// Response payload containing the minted JWT.
/// </summary>
public sealed class DevLoginResponse
{
    /// <summary>
    /// The bearer token to send as: Authorization: Bearer {token}
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Token type (always "Bearer").
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// Token expiry (seconds) from issuance.
    /// </summary>
    public int ExpiresInSeconds { get; set; }

    /// <summary>
    /// Echoed username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Echoed role.
    /// </summary>
    public string Role { get; set; } = string.Empty;
}
