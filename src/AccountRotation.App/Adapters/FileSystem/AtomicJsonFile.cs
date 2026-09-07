using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccountRotation.App.Adapters.FileSystem;

/// <summary>
/// Every JSON write in the App: the node is serialized compactly and written
/// through <see cref="AtomicBytesFile"/>, so a reader sees the old bytes or the
/// new bytes and never a torn file.
/// </summary>
internal static class AtomicJsonFile
{
    public static Task WriteAsync(string path, JsonNode content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(content);
        return AtomicBytesFile.WriteAsync(path, bytes, cancellationToken);
    }
}
