using System.Text.Json;
using System.Text.Json.Serialization;
using AccountRotation.App.Adapters.FileSystem;
using AccountRotation.Core.Identity;

namespace AccountRotation.App.Switching;

/// <summary>How far a switch got before the journal was last written.</summary>
internal enum SwitchStep
{
    Planned,
    Parked,
    Unparked,
    Patched,
}

/// <summary>
/// The intent written before the first move: what is leaving, what is arriving,
/// and the step reached. Fingerprints, never tokens.
/// </summary>
internal sealed record SwitchJournalEntry(
    AccountEmail? Outgoing,
    RefreshTokenFingerprint? OutgoingFingerprint,
    string? OutgoingFolderPath,
    AccountEmail Incoming,
    RefreshTokenFingerprint IncomingFingerprint,
    string IncomingFolderPath,
    SwitchStep StepReached,
    DateTimeOffset StartedAt);

/// <summary>
/// <c>&lt;appdata&gt;/state/switch-journal.json</c>: present only while a switch
/// is in flight. A crash between moves leaves it behind, and startup
/// reconciliation reads it to finish or unwind the switch from the
/// fingerprints on disk.
/// </summary>
internal sealed class SwitchJournal
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SwitchJournal(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        _path = Path.Combine(Path.GetFullPath(appDataDirectory), "state", "switch-journal.json");
    }

    public async Task WriteAsync(SwitchJournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var document = JournalDocument.From(entry);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, _serializerOptions);
        await AtomicBytesFile.WriteAsync(_path, bytes, cancellationToken);
    }

    public async Task<SwitchJournalEntry?> ReadOpenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        byte[] bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        JournalDocument? document = JsonSerializer.Deserialize<JournalDocument>(bytes, _serializerOptions);
        return document?.ToEntry();
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_path))
        {
            await AtomicBytesFile.DeleteWithRetryAsync(_path, cancellationToken);
        }
    }

    /// <summary>The on-disk shape: primitives only, so the value types need no converters.</summary>
    private sealed record JournalDocument(
        string? Outgoing,
        string? OutgoingFingerprint,
        string? OutgoingFolderPath,
        string Incoming,
        string IncomingFingerprint,
        string IncomingFolderPath,
        SwitchStep StepReached,
        DateTimeOffset StartedAt)
    {
        public static JournalDocument From(SwitchJournalEntry entry) => new(
            entry.Outgoing?.Value,
            entry.OutgoingFingerprint?.Sha256Hex,
            entry.OutgoingFolderPath,
            entry.Incoming.Value,
            entry.IncomingFingerprint.Sha256Hex,
            entry.IncomingFolderPath,
            entry.StepReached,
            entry.StartedAt);

        public SwitchJournalEntry ToEntry() => new(
            Outgoing is null ? null : new AccountEmail(Outgoing),
            OutgoingFingerprint is null ? null : new RefreshTokenFingerprint(OutgoingFingerprint),
            OutgoingFolderPath,
            new AccountEmail(Incoming),
            new RefreshTokenFingerprint(IncomingFingerprint),
            IncomingFolderPath,
            StepReached,
            StartedAt);
    }
}
