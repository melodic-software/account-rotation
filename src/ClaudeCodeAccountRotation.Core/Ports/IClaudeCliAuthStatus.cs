namespace ClaudeCodeAccountRotation.Core.Ports;

/// <summary>
/// What <c>claude auth status --json</c> reports for a config directory. The
/// CLI is the authority on which account is live; the tool cross-checks
/// against it after every switch.
/// </summary>
public sealed record ClaudeAuthStatus(
    bool LoggedIn,
    string? Email,
    string? AuthMethod,
    string? OrganizationName,
    string? SubscriptionType,
    string? ProjectsDirectory);

/// <summary>Runs the unmodified CLI's own status command; out of process, so a port.</summary>
public interface IClaudeCliAuthStatus
{
    /// <summary>
    /// Reads the auth status for <paramref name="configDirectory"/> (the live dir
    /// when null). A failure carries the diagnostic text; the result is never null.
    /// </summary>
    Task<Result<ClaudeAuthStatus, string>> ReadAsync(string? configDirectory, CancellationToken cancellationToken);
}
