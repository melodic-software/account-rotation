using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.Core.Ports;

/// <summary>
/// Renews one parked pair's access token, which also rotates its refresh token
/// and renews the login's own four-week lifetime. The implementation returns
/// the rotated tokens to the caller and writes nothing: only
/// <see cref="ICredentialPairStore"/> touches a credential file.
/// </summary>
public interface ITokenRefreshClient
{
    Task<Result<RefreshedTokens, UsageReadFailure>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
