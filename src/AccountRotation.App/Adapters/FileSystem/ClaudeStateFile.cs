using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccountRotation.Core.Identity;

namespace AccountRotation.App.Adapters.FileSystem;

/// <summary>
/// Claude Code's state file (<c>~/.claude.json</c>, or <c>&lt;CLAUDE_CONFIG_DIR&gt;/.claude.json</c>
/// when that variable is set). Only the <c>oauthAccount</c> value is ever
/// rewritten; every other byte of the file, including per-project trust and
/// MCP state, is preserved by splicing the new value into the original bytes.
/// </summary>
internal sealed class ClaudeStateFile
{
    private const string AccountPropertyName = "oauthAccount";
    private const int MaxPatchAttempts = 5;

    public ClaudeStateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public async Task<OAuthAccountBlock?> ReadAccountBlockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        byte[] bytes = await File.ReadAllBytesAsync(Path, cancellationToken);
        AccountSpan? span = LocateAccountValue(bytes);
        if (span is null)
        {
            return null;
        }

        var node = JsonNode.Parse(bytes.AsSpan(span.Value.Start, span.Value.Length));
        return node is JsonObject raw ? OAuthAccountBlock.FromJson(raw) : null;
    }

    /// <summary>
    /// Patches against the bytes on disk at the moment of writing: the file is
    /// read, patched, and replaced only if its length and last-write time are
    /// still those of the read, so a session's own rewrite of an unrelated key
    /// during the patch is re-read and kept rather than overwritten with stale
    /// bytes. A file that keeps changing across <see cref="MaxPatchAttempts"/>
    /// attempts fails the patch instead of guessing; the journal then completes
    /// it at the next startup.
    /// </summary>
    public async Task PatchAccountBlockAsync(OAuthAccountBlock account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        byte[] value = JsonSerializer.SerializeToUtf8Bytes(account.Raw);

        for (int attempt = 1; attempt <= MaxPatchAttempts; attempt++)
        {
            Stamp before = ReadStamp();
            byte[] original = before.Exists
                ? await File.ReadAllBytesAsync(Path, cancellationToken)
                : Encoding.UTF8.GetBytes("{}");

            AccountSpan? span = LocateAccountValue(original);
            byte[] patched = span is AccountSpan existing
                ? Splice(original, existing.Start, existing.Length, value)
                : AppendProperty(original, value);

            if (ReadStamp() == before)
            {
                await AtomicBytesFile.WriteAsync(Path, patched, cancellationToken);
                return;
            }
        }

        throw new IOException("The state file " + Path + " kept changing while the account block was being patched; the switch is journaled and completes at the next startup.");
    }

    private Stamp ReadStamp()
    {
        FileInfo info = new(Path);
        return info.Exists ? new Stamp(true, info.Length, info.LastWriteTimeUtc) : new Stamp(false, 0, DateTime.MinValue);
    }

    private static AccountSpan? LocateAccountValue(byte[] bytes)
    {
        Utf8JsonReader reader = new(bytes, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return null;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
            {
                continue;
            }

            bool isAccount = reader.ValueTextEquals(AccountPropertyName);
            reader.Read();
            if (!isAccount)
            {
                reader.TrySkip();
                continue;
            }

            int start = checked((int)reader.TokenStartIndex);
            reader.TrySkip();
            int end = checked((int)reader.BytesConsumed);
            return new AccountSpan(start, end - start);
        }

        return null;
    }

    private static byte[] Splice(byte[] original, int start, int length, byte[] value)
    {
        byte[] result = new byte[original.Length - length + value.Length];
        original.AsSpan(0, start).CopyTo(result);
        value.CopyTo(result.AsSpan(start));
        original.AsSpan(start + length).CopyTo(result.AsSpan(start + value.Length));
        return result;
    }

    /// <summary>Inserts <c>"oauthAccount": value</c> before the root object's closing brace.</summary>
    private static byte[] AppendProperty(byte[] original, byte[] value)
    {
        int closingBrace = Array.LastIndexOf(original, (byte)'}');
        if (closingBrace < 0)
        {
            throw new InvalidDataException("The state file is not a JSON object.");
        }

        bool hasProperties = original.AsSpan(0, closingBrace).IndexOf((byte)':') >= 0;
        byte[] prefix = Encoding.UTF8.GetBytes((hasProperties ? "," : string.Empty) + "\n  \"" + AccountPropertyName + "\": ");
        byte[] suffix = Encoding.UTF8.GetBytes("\n");
        byte[] insertion = [.. prefix, .. value, .. suffix];
        return Splice(original, closingBrace, 0, insertion);
    }

    private readonly record struct AccountSpan(int Start, int Length);

    private readonly record struct Stamp(bool Exists, long Length, DateTime LastWriteUtc);
}
