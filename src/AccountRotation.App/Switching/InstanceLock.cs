using AccountRotation.Core;

namespace AccountRotation.App.Switching;

/// <summary>
/// One running instance per app-data directory: an exclusively opened lock
/// file holding the running instance's URL, so a second launch refuses and
/// prints where the first one is listening.
/// </summary>
internal sealed class InstanceLock : IDisposable
{
    public const string FileName = "instance.lock";

    private readonly FileStream _stream;

    private InstanceLock(FileStream stream, string lockFilePath)
    {
        _stream = stream;
        LockFilePath = lockFilePath;
    }

    public string LockFilePath { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the lock transfers to the caller through the result; disposing it releases the file.")]
    public static Result<InstanceLock, string> TryAcquire(string appDataDirectory, string listenUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(listenUrl);
        Directory.CreateDirectory(appDataDirectory);
        string lockFilePath = Path.Combine(appDataDirectory, FileName);

        FileStreamOptions options = new()
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.Read,
            Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        FileStream? stream = null;
        try
        {
            try
            {
                stream = new FileStream(lockFilePath, options);
            }
            catch (IOException)
            {
                return Result<InstanceLock, string>.Failure("another instance is running at " + ReadRunningUrl(lockFilePath) + " (lock file " + lockFilePath + ")");
            }

            stream.SetLength(0);
            using (StreamWriter writer = new(stream, leaveOpen: true))
            {
                writer.Write(listenUrl);
                writer.Flush();
            }

            stream.Flush(flushToDisk: true);

            InstanceLock acquired = new(stream, lockFilePath);
            stream = null;
            return Result<InstanceLock, string>.Success(acquired);
        }
        finally
        {
            stream?.Dispose();
        }
    }

    public void Dispose() => _stream.Dispose();

    private static string ReadRunningUrl(string lockFilePath)
    {
        try
        {
            // The holder opened the file delete-on-close, so a reader must share Delete as well.
            using FileStream reader = new(lockFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader text = new(reader);
            string url = text.ReadToEnd().Trim();
            return url.Length == 0 ? "an unknown address" : url;
        }
        catch (IOException)
        {
            return "an unknown address";
        }
        catch (UnauthorizedAccessException)
        {
            return "an unknown address";
        }
    }
}
