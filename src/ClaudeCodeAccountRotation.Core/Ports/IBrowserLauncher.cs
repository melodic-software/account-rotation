using ClaudeCodeAccountRotation.Core.Accounts;

namespace ClaudeCodeAccountRotation.Core.Ports;

/// <summary>
/// Opens a sign-in URL in one named browser profile, so ten accounts do not
/// fight over one signed-in profile. Out of process, so a port.
/// </summary>
public interface IBrowserLauncher
{
    /// <summary>
    /// Starts <paramref name="browser"/> on <paramref name="signInUrl"/> in
    /// <paramref name="profileDirectory"/> (the browser's own default profile
    /// when null). A failure carries the reason; nothing is started.
    /// </summary>
    Result<Unit, string> Launch(BrowserFamily browser, string? profileDirectory, Uri signInUrl);
}
