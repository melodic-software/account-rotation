using ClaudeCodeAccountRotation.Core.Accounts;

namespace ClaudeCodeAccountRotation.Core.Ports;

/// <summary>
/// Enumerates the browser profiles this machine already has, so the roster's
/// profile mapping is picked from a list instead of guessed. On disk, so a port.
/// </summary>
public interface IBrowserProfileReader
{
    /// <summary>
    /// Every profile every installed Chromium-family browser publishes, in
    /// browser order. A browser that publishes nothing readable contributes
    /// nothing; there is no failure case, because a browser the operator never
    /// installed must not take the page down for the ones they did.
    /// </summary>
    Task<IReadOnlyList<BrowserProfile>> ReadAsync(CancellationToken cancellationToken);
}
