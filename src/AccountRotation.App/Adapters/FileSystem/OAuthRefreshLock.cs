using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AccountRotation.Core;

namespace AccountRotation.App.Adapters.FileSystem;

/// <summary>
/// Claude Code's own token-refresh mutex, joined rather than sampled. The CLI
/// takes <c>&lt;live dir&gt;/.oauth_refresh.lock</c> through an exclusive
/// directory create (proper-lockfile: stale after 60 s, mtime refreshed every
/// 5 s), and a contending process gets a retryable error. The tool acquires
/// the same directory the same way, so a session's own refresh can never run
/// between the tool's park and unpark. A hold lasts milliseconds, so the mtime
/// is not refreshed while held.
/// </summary>
internal sealed partial class OAuthRefreshLock
{
    public const string DirectoryName = ".oauth_refresh.lock";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    private const int WindowsErrorAlreadyExists = 183;
    private const int UnixErrorExists = 17;
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _lockDirectory;
    private readonly TimeProvider _timeProvider;

    public OAuthRefreshLock(string liveConfigDirectory, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveConfigDirectory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _lockDirectory = Path.Combine(Path.GetFullPath(liveConfigDirectory), DirectoryName);
        _timeProvider = timeProvider;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the held lock transfers to the caller through the result; disposing it releases the lock.")]
    public async Task<Result<IAsyncDisposable, string>> AcquireAsync(TimeSpan waitBound, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _timeProvider.GetUtcNow() + waitBound;
        while (true)
        {
            if (TryCreateExclusively(_lockDirectory))
            {
                return Result<IAsyncDisposable, string>.Success(new Held(_lockDirectory));
            }

            if (IsStale())
            {
                TryRemove(_lockDirectory);
                continue;
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                return Result<IAsyncDisposable, string>.Failure(
                    "another process holds " + DirectoryName + " (a session is refreshing its token); waited " + waitBound.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s");
            }

            await Task.Delay(_pollInterval, _timeProvider, cancellationToken);
        }
    }

    private bool IsStale()
    {
        if (!Directory.Exists(_lockDirectory))
        {
            return false;
        }

        DateTime lastWrite = Directory.GetLastWriteTimeUtc(_lockDirectory);
        return _timeProvider.GetUtcNow() - lastWrite > StaleAfter;
    }

    private static void TryRemove(string lockDirectory)
    {
        try
        {
            Directory.Delete(lockDirectory);
        }
        catch (IOException)
        {
            // Another process removed or re-created it first; the loop re-evaluates.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: leave it to the next iteration, which times out if it persists.
        }
    }

    private static bool TryCreateExclusively(string lockDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            if (CreateDirectoryWindows(lockDirectory, nint.Zero))
            {
                return true;
            }

            int error = Marshal.GetLastPInvokeError();
            return error == WindowsErrorAlreadyExists
                ? false
                : throw new IOException("CreateDirectoryW failed for " + lockDirectory + " (Win32 error " + error.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        if (MakeDirectoryUnix(lockDirectory, 0x1C0) == 0)
        {
            return true;
        }

        int errno = Marshal.GetLastPInvokeError();
        return errno == UnixErrorExists
            ? false
            : throw new IOException("mkdir failed for " + lockDirectory + " (errno " + errno.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryWindows(string path, nint securityAttributes);

    [LibraryImport("libc", EntryPoint = "mkdir", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int MakeDirectoryUnix(string path, uint mode);

    private sealed class Held(string lockDirectory) : IAsyncDisposable
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                TryRemove(lockDirectory);
            }

            return ValueTask.CompletedTask;
        }
    }
}
