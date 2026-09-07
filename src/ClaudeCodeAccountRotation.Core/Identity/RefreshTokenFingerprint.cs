using System.Security.Cryptography;
using System.Text;

namespace ClaudeCodeAccountRotation.Core.Identity;

/// <summary>
/// The SHA-256 of a refresh token, in lowercase hex. What tests, logs, and the
/// single-holder check compare; it reveals nothing about the token itself.
/// </summary>
public readonly record struct RefreshTokenFingerprint(string Sha256Hex)
{
    public static RefreshTokenFingerprint FromRefreshToken(string refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return new RefreshTokenFingerprint(Convert.ToHexStringLower(digest));
    }

    public override string ToString() => Sha256Hex;
}
