using System.Reflection;

namespace AccountRotation.App.Hosting;

/// <summary>The page's entry document, read once from the executable's resources.</summary>
internal static class EmbeddedPage
{
    private const string ResourceName = "AccountRotation.App.wwwroot.index.html";

    private static readonly Lazy<string> _indexHtml = new(Load);

    public static string IndexHtml => _indexHtml.Value;

    private static string Load()
    {
        Assembly assembly = typeof(EmbeddedPage).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded page " + ResourceName + " is missing from the executable.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
