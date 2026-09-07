using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.App.Adapters.FileSystem;

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

    private readonly Func<CancellationToken, Task> _beforeReplace;

    public ClaudeStateFile(string path)
        : this(path, beforeReplace: null)
    {
    }

    /// <summary>
    /// <paramref name="beforeReplace"/> runs after the patched bytes are staged and
    /// before the file is re-read and replaced; tests use it to interleave a
    /// session's rewrite at the one moment the detector must see it.
    /// </summary>
    internal ClaudeStateFile(string path, Func<CancellationToken, Task>? beforeReplace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _beforeReplace = beforeReplace ?? (static _ => Task.CompletedTask);
    }

    public string Path { get; }

    public async Task<OAuthAccountBlock?> ReadAccountBlockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        byte[] bytes = await SharedFileReader.ReadAllBytesAsync(Path, cancellationToken);
        AccountSpan? span = LocateAccountValue(bytes);
        if (span is null)
        {
            return null;
        }

        var node = JsonNode.Parse(bytes.AsSpan(span.Value.Start, span.Value.Length));
        return node is JsonObject raw ? OAuthAccountBlock.FromJson(raw) : null;
    }

    /// <summary>
    /// Patches against the bytes on disk at the moment of the replace: the file is
    /// read, the patched bytes are staged in a flushed temp file, then the file is
    /// read again and replaced only if every byte is still what was read, so a
    /// session's own rewrite of an unrelated key during the patch is re-read and
    /// kept rather than overwritten with stale bytes. Bytes, not a length and a
    /// last-write time: the CLI's rewrites often keep the length and can land
    /// inside one timestamp tick. The only unguarded window is the rename itself.
    /// A file that keeps changing across <see cref="MaxPatchAttempts"/> attempts
    /// fails the patch instead of guessing; the journal then completes it at the
    /// next startup.
    /// </summary>
    public async Task PatchAccountBlockAsync(OAuthAccountBlock account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        byte[] value = JsonSerializer.SerializeToUtf8Bytes(account.Raw);

        for (int attempt = 1; attempt <= MaxPatchAttempts; attempt++)
        {
            byte[]? original = await ReadBytesOrNullAsync(cancellationToken);
            byte[] basis = original ?? Encoding.UTF8.GetBytes("{}");

            AccountSpan? span = LocateAccountValue(basis);
            byte[] patched = span is AccountSpan existing
                ? Splice(basis, existing.Start, existing.Length, value)
                : AppendProperty(basis, value);

            string temporaryPath = await AtomicBytesFile.WriteTemporaryAsync(Path, patched, cancellationToken);
            try
            {
                await _beforeReplace(cancellationToken);
                byte[]? current = await ReadBytesOrNullAsync(cancellationToken);
                bool unchanged = original is null
                    ? current is null
                    : current is not null && current.AsSpan().SequenceEqual(original);
                if (unchanged)
                {
                    await AtomicBytesFile.MoveIntoPlaceWithRetryAsync(temporaryPath, Path, cancellationToken);
                    return;
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        throw new IOException("The state file " + Path + " kept changing while the account block was being patched; the switch is journaled and completes at the next startup.");
    }

    private async Task<byte[]?> ReadBytesOrNullAsync(CancellationToken cancellationToken) =>
        File.Exists(Path) ? await SharedFileReader.ReadAllBytesAsync(Path, cancellationToken) : null;

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
}
