using AccountRotation.Core.Identity;
using AccountRotation.Core.Ports;

namespace AccountRotation.Core.Switching;

/// <summary>
/// What a switch left behind. A CLI-reported e-mail that differs from
/// <see cref="Now"/> is surfaced through <see cref="IdentityMismatchWarning"/>,
/// never hidden.
/// </summary>
public sealed record SwitchOutcome(
    AccountEmail Now,
    AccountEmail? ParkedAs,
    Result<ClaudeAuthStatus, string> CliVerification,
    bool IdentityMismatchWarning,
    DateTimeOffset At);
