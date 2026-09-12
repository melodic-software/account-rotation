using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Quota;

namespace ClaudeCodeAccountRotation.App.Quota;

/// <summary>
/// Where a successful read's numbers go so they survive a restart and the card
/// can say "via cached" with the original capture time instead of falling back
/// to "unknown".
/// <para>
/// The file itself is the next phase's work; this is the seam it fills, placed
/// now so the engine's call site is real code with a real signature rather than
/// a comment somebody has to find. Saving nothing is correct behaviour in the
/// meantime: an in-memory snapshot is what the page renders either way, and a
/// restart simply forgets, which is what it does today.
/// </para>
/// </summary>
internal sealed class UsageSnapshotCache
{
    /// <summary>Records one account's newest numbers. A no-op until the cache file lands.</summary>
#pragma warning disable CA1822 // Mark members as static
    public Task SaveAsync(AccountEmail account, UsageSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.CompletedTask;
#pragma warning restore CA1822
    // Deliberately an instance method: the phase that adds the file gives this
    // type a path and a write lock, and a call site that had to change from a
    // static call would be a needless edit to the engine's gated section.
}
