using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.Core.Accounts;

/// <summary>
/// One browser profile the machine already has, as its browser publishes it.
/// <para>
/// All three fields earn their place. <see cref="Directory"/> is the only one
/// the launcher can use, because <c>--profile-directory</c> names the folder
/// and nothing else. <see cref="Name"/> is the only one the operator
/// recognizes. They disagree often enough to matter: Edge writes a profile
/// whose directory is <c>Default</c> and whose name is <c>Profile 1</c>, which
/// is also a real directory name in another browser, so either field shown
/// alone picks the wrong profile. <see cref="SignedInAs"/> is what makes the
/// mapping derivable rather than typed: an account whose address matches a
/// profile's is already signed into that profile.
/// </para>
/// </summary>
/// <param name="Browser">The browser that published the profile.</param>
/// <param name="Directory">The profile's own directory name, the launcher's operand.</param>
/// <param name="Name">The name that browser shows for it.</param>
/// <param name="SignedInAs">The address signed into it, when it has one that parses.</param>
public sealed record BrowserProfile(
    BrowserFamily Browser,
    string Directory,
    string Name,
    AccountEmail? SignedInAs);
