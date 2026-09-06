namespace AccountRotation.App.Adapters.FileSystem;

/// <summary>
/// Reads a whole file through a handle that shares write and delete access.
/// <see cref="File.ReadAllBytesAsync"/> opens with <see cref="FileShare.Read"/>,
/// and on Windows a handle that grants no write or delete sharing makes the
/// CLI's own write to, or rename over, the same path fail with a sharing
/// violation for as long as the read is open. Every read of a file Claude Code
/// also writes goes through here so the tool never gets in the CLI's way.
/// </summary>
internal static class SharedFileReader
{
    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };
        FileStream stream = new(path, options);
        await using (stream)
        {
            using MemoryStream buffer = new(stream.CanSeek && stream.Length <= int.MaxValue ? (int)stream.Length : 0);
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
    }
}
