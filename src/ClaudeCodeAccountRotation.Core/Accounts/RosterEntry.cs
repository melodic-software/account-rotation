using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.Core.Accounts;

/// <summary>The Chromium-family browsers the launcher knows how to open a profile in.</summary>
public enum BrowserFamily
{
    Chrome,
    Edge,
    Brave,
}

/// <summary>
/// One account the operator put on this machine: its e-mail, what to call it,
/// which browser profile signs it in, whether it is out of the rotation, and
/// whatever the operator wrote about it. No token field exists here; the
/// credential pair lives in the profile folder, never in the roster.
/// </summary>
public sealed record RosterEntry(
    AccountEmail Email,
    string? Alias = null,
    BrowserFamily? Browser = null,
    string? BrowserProfileDirectory = null,
    bool Paused = false,
    string? Notes = null);
