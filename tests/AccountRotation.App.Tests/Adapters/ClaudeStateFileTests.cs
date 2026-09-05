using System.Text;
using System.Text.Json.Nodes;
using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.Core.Identity;

namespace AccountRotation.App.Tests.Adapters;

public sealed class ClaudeStateFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ClaudeStateFileTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, ".claude.json");
    }

    private static OAuthAccountBlock Account(string email) =>
        OAuthAccountBlock.FromJson(new JsonObject
        {
            ["accountUuid"] = Guid.NewGuid().ToString(),
            ["emailAddress"] = email,
            ["organizationRateLimitTier"] = "default_claude_max_20x",
        });

    /// <summary>
    /// A state file shaped like the real one: a large projects map with
    /// non-ASCII strings, the account block in the middle, more keys after it,
    /// and the CLI's two-space indentation. Over 90 KB.
    /// </summary>
    private static string LargeStateFile(string email)
    {
        StringBuilder builder = new();
        builder.Append("{\n  \"numStartups\": 412,\n  \"projects\": {\n");
        for (int index = 0; index < 400; index++)
        {
            builder.Append("    \"/home/dev/repos/projekt-").Append(index).Append("\": {\n");
            builder.Append("      \"allowedTools\": [\"Bash(ls:*)\", \"Read\"],\n");
            builder.Append("      \"lastPrompt\": \"Größe prüfen — 日本語 テスト ✓ ").Append(new string('x', 120)).Append("\",\n");
            builder.Append("      \"hasTrustDialogAccepted\": true\n");
            builder.Append("    },\n");
        }

        builder.Append("    \"/home/dev/last\": { \"hasTrustDialogAccepted\": false }\n  },\n");
        builder.Append("  \"oauthAccount\": {\n    \"accountUuid\": \"old-uuid\",\n    \"emailAddress\": \"").Append(email).Append("\",\n    \"organizationRateLimitTier\": \"default_claude_max_20x\"\n  },\n");
        builder.Append("  \"cachedChangelog\": \"# Changelog\\n\\n## 2.1.261\\n\\n- Fixes\\n\",\n");
        builder.Append("  \"fallbackAvailableWarningThreshold\": 0.5\n}\n");
        return builder.ToString();
    }

    [Fact]
    public async Task ReadsTheAccountBlock()
    {
        await File.WriteAllTextAsync(_path, LargeStateFile("a@example.com"), TestContext.Current.CancellationToken);
        ClaudeStateFile stateFile = new(_path);

        OAuthAccountBlock? block = await stateFile.ReadAccountBlockAsync(TestContext.Current.CancellationToken);

        block!.Email.ShouldBe(AccountEmail.Parse("a@example.com").Value);
        block.OrganizationRateLimitTier.ShouldBe("default_claude_max_20x");
    }

    [Fact]
    public async Task ReadReturnsNullWhenTheFileHasNoAccountBlock()
    {
        await File.WriteAllTextAsync(_path, """{"numStartups": 1}""", TestContext.Current.CancellationToken);

        (await new ClaudeStateFile(_path).ReadAccountBlockAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task PatchChangesOnlyTheAccountSpan()
    {
        string original = LargeStateFile("a@example.com");
        await File.WriteAllTextAsync(_path, original, TestContext.Current.CancellationToken);
        original.Length.ShouldBeGreaterThan(90_000);
        ClaudeStateFile stateFile = new(_path);

        await stateFile.PatchAccountBlockAsync(Account("b@example.com"), TestContext.Current.CancellationToken);

        string patched = await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken);
        int originalStart = original.IndexOf("\"oauthAccount\": ", StringComparison.Ordinal) + "\"oauthAccount\": ".Length;
        int originalEnd = original.IndexOf(",\n  \"cachedChangelog\"", StringComparison.Ordinal);
        int patchedEnd = patched.IndexOf(",\n  \"cachedChangelog\"", StringComparison.Ordinal);
        patched[..originalStart].ShouldBe(original[..originalStart]);
        patched[patchedEnd..].ShouldBe(original[originalEnd..]);
        JsonNode.Parse(patched)!["oauthAccount"]!["emailAddress"]!.GetValue<string>().ShouldBe("b@example.com");
        JsonNode.Parse(patched)!["projects"]!.AsObject().Count.ShouldBe(401);
        Directory.GetFiles(_directory).ShouldBe([_path]);
    }

    [Fact]
    public async Task PatchSurvivesAnExternalRewriteBetweenReadAndPatch()
    {
        await File.WriteAllTextAsync(_path, LargeStateFile("a@example.com"), TestContext.Current.CancellationToken);
        ClaudeStateFile stateFile = new(_path);
        _ = await stateFile.ReadAccountBlockAsync(TestContext.Current.CancellationToken);

        // A session rewrites an unrelated key after the tool's read.
        JsonNode external = JsonNode.Parse(await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken))!;
        external["numStartups"] = 413;
        await File.WriteAllTextAsync(_path, external.ToJsonString(), TestContext.Current.CancellationToken);

        await stateFile.PatchAccountBlockAsync(Account("b@example.com"), TestContext.Current.CancellationToken);

        JsonNode result = JsonNode.Parse(await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken))!;
        result["numStartups"]!.GetValue<int>().ShouldBe(413);
        result["oauthAccount"]!["emailAddress"]!.GetValue<string>().ShouldBe("b@example.com");
    }

    [Fact]
    public async Task PatchAddsTheBlockWhenTheFileHasNone()
    {
        await File.WriteAllTextAsync(_path, "{\n  \"numStartups\": 1\n}\n", TestContext.Current.CancellationToken);

        await new ClaudeStateFile(_path).PatchAccountBlockAsync(Account("b@example.com"), TestContext.Current.CancellationToken);

        JsonNode result = JsonNode.Parse(await File.ReadAllTextAsync(_path, TestContext.Current.CancellationToken))!;
        result["numStartups"]!.GetValue<int>().ShouldBe(1);
        result["oauthAccount"]!["emailAddress"]!.GetValue<string>().ShouldBe("b@example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
