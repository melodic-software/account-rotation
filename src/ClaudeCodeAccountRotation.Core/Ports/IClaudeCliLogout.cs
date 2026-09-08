namespace ClaudeCodeAccountRotation.Core.Ports;

/// <summary>
/// Revokes a folder's login by running the CLI's own <c>auth logout</c> under
/// that folder. A removed account's refresh token is revoked at the source
/// rather than merely deleted, since deleted bytes are recoverable and a
/// revoked token is not. Out of process, so a port.
/// </summary>
public interface IClaudeCliLogout
{
    /// <summary>
    /// Logs out of <paramref name="configDirectory"/>. A failure carries the
    /// diagnostic text; the caller decides whether to proceed without it.
    /// </summary>
    Task<Result<Unit, string>> LogoutAsync(string configDirectory, CancellationToken cancellationToken);
}
