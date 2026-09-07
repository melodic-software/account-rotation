using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.Core.Switching;

/// <summary>The pure output of planning: what will move where. Nothing has moved yet.</summary>
public sealed record SwitchPlan(
    AccountEmail? Outgoing,
    string? OutgoingFolderPath,
    AccountEmail Incoming,
    string IncomingFolderPath,
    OAuthAccountBlock IncomingAccount);
