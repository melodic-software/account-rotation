using System.Globalization;
using System.Text.Json;
using ClaudeCodeAccountRotation.App.Adapters.Process;
using ClaudeCodeAccountRotation.Core.Accounts;
using ClaudeCodeAccountRotation.Core.Identity;
using ClaudeCodeAccountRotation.Core.Ports;

namespace ClaudeCodeAccountRotation.App.Adapters.FileSystem;

/// <summary>
/// Reads each Chromium-family browser's <c>Local State</c>, the JSON file where
/// the browser publishes its own profiles under <c>profile.info_cache</c>: the
/// directory name as the key, the display name and usually the signed-in
/// address as the value. That is where "which browser profile is this account
/// already signed into" is answered without anyone guessing.
/// <para>
/// Read-only and forgiving, like every other read of a file another program
/// owns. A running browser is writing this file, so the read goes through
/// <see cref="SharedFileReader"/>; an absent file, an unreadable one, torn or
/// malformed JSON, and a file with no <c>info_cache</c> are all one answer, no
/// profiles for that browser, never a failed request. Nothing here writes into
/// a browser's user data directory.
/// </para>
/// </summary>
internal sealed class ChromiumLocalStateProfileReader : IBrowserProfileReader
{
    private readonly Func<BrowserFamily, string?> _localStatePath;

    public ChromiumLocalStateProfileReader(Func<BrowserFamily, string?>? localStatePath = null) =>
        _localStatePath = localStatePath ?? LocalStatePath;

    public async Task<IReadOnlyList<BrowserProfile>> ReadAsync(CancellationToken cancellationToken)
    {
        List<BrowserProfile> profiles = [];
        // In declaration order, so two browsers signed into one address always
        // resolve the same way for the page's auto-match.
        foreach (BrowserFamily browser in Enum.GetValues<BrowserFamily>())
        {
            profiles.AddRange(await ReadAsync(browser, cancellationToken));
        }

        return profiles;
    }

    /// <summary>
    /// Where a browser keeps <c>Local State</c> on this platform, or null when
    /// this platform has no known location for it. The Windows vendor segment
    /// is the launcher's, so the two adapters cannot drift apart.
    /// </summary>
    internal static string? LocalStatePath(BrowserFamily browser)
    {
        if (OperatingSystem.IsWindows())
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrWhiteSpace(root)
                ? null
                : Path.Combine(root, ChromiumFamilyBrowserLauncher.WindowsVendorPath(browser), "User Data", "Local State");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            string bundle = browser switch
            {
                BrowserFamily.Chrome => Path.Combine("Google", "Chrome"),
                BrowserFamily.Edge => "Microsoft Edge",
                _ => Path.Combine("BraveSoftware", "Brave-Browser"),
            };
            return Path.Combine(home, "Library", "Application Support", bundle, "Local State");
        }

        string configured = browser switch
        {
            BrowserFamily.Chrome => "google-chrome",
            BrowserFamily.Edge => "microsoft-edge",
            _ => Path.Combine("BraveSoftware", "Brave-Browser"),
        };
        return Path.Combine(home, ".config", configured, "Local State");
    }

    private async Task<IReadOnlyList<BrowserProfile>> ReadAsync(BrowserFamily browser, CancellationToken cancellationToken)
    {
        if (_localStatePath(browser) is not string path || string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        byte[] bytes;
        try
        {
            // No exists-then-open check: the browser rewrites this file through a
            // temp and a rename, so the open is the only honest test. A browser
            // that is not installed, a path this user cannot read, and a read
            // landing in that rename window are one thing here.
            bytes = await SharedFileReader.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            return Profiles(browser, document.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<BrowserProfile> Profiles(BrowserFamily browser, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("profile", out JsonElement profile)
            || profile.ValueKind != JsonValueKind.Object
            || !profile.TryGetProperty("info_cache", out JsonElement cache)
            || cache.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        List<BrowserProfile> profiles = [];
        foreach (JsonProperty entry in cache.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // A profile the browser never named still has a directory, and the
            // directory is what the launcher needs; showing it twice beats
            // dropping the row.
            profiles.Add(new BrowserProfile(browser, entry.Name, Text(entry.Value, "name") ?? entry.Name, SignedInAs(entry.Value)));
        }

        // The file's order is the browser's own bookkeeping, not a list for a
        // person: directories come from a counter the browser never decrements,
        // so deletions leave gaps, and nothing promises the keys are written in
        // any order at all. Sort here so the picker reads the way the operator
        // does: Default, then the numbered profiles as numbers, then the rest.
        profiles.Sort(static (left, right) => CompareDirectories(left.Directory, right.Directory));
        return profiles;
    }

    /// <summary>
    /// <c>Default</c> first, then <c>Profile N</c> by <c>N</c> so that
    /// <c>Profile 10</c> follows <c>Profile 2</c>, then any other directory name
    /// in ordinal order after all of those. Case-sensitive on purpose: these are
    /// directory names the browser wrote, compared as the launcher passes them.
    /// </summary>
    private static int CompareDirectories(string left, string right)
    {
        (int leftRank, int leftNumber) = Rank(left);
        (int rightRank, int rightNumber) = Rank(right);
        int byRank = leftRank.CompareTo(rightRank);
        if (byRank != 0)
        {
            return byRank;
        }

        int byNumber = leftNumber.CompareTo(rightNumber);
        return byNumber != 0 ? byNumber : string.CompareOrdinal(left, right);
    }

    private static (int Rank, int Number) Rank(string directory)
    {
        const string prefix = "Profile ";
        if (directory == "Default")
        {
            return (0, 0);
        }

        if (directory.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(directory.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return (1, number);
        }

        return (2, 0);
    }

    /// <summary>
    /// The address the browser records for a profile. Written by another
    /// program, so it goes through <see cref="AccountEmail.Parse"/>: the
    /// allowlist refuses a display name, an empty string, and a directory-style
    /// account, and each of those leaves the profile with no address rather
    /// than throwing.
    /// </summary>
    private static AccountEmail? SignedInAs(JsonElement profile) =>
        Text(profile, "user_name") is string address
            ? AccountEmail.Parse(address).Match(static parsed => (AccountEmail?)parsed, static _ => null)
            : null;

    /// <summary>
    /// A string this file carries, or null when it carries nothing usable.
    /// Blank counts as nothing: an empty name would render an option the
    /// operator cannot read, and an empty address is not an address.
    /// </summary>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is string text
        && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}
