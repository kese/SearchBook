using System.IO;

namespace SearchBook.Services;

public sealed class AppDataPaths
{
    public static AppDataPaths Current { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SearchBook"));

    public AppDataPaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        ResultsDirectory = Path.Combine(RootDirectory, "results");
        PreviewsDirectory = Path.Combine(ResultsDirectory, "previews");
        UpdatesDirectory = Path.Combine(RootDirectory, "updates");
        DiagnosticsDirectory = Path.Combine(RootDirectory, "diagnostics");
    }

    public string RootDirectory { get; }
    public string ResultsDirectory { get; }
    public string PreviewsDirectory { get; }
    public string UpdatesDirectory { get; }
    public string DiagnosticsDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ResultsDirectory);
        Directory.CreateDirectory(PreviewsDirectory);
        Directory.CreateDirectory(UpdatesDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }

    public int CleanupPreviews(DateTime utcNow, TimeSpan maximumAge)
    {
        if (!Directory.Exists(PreviewsDirectory)) return 0;

        var threshold = utcNow - maximumAge;
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(PreviewsDirectory, "*.xlsx"))
        {
            if (File.GetLastWriteTimeUtc(path) >= threshold) continue;
            File.Delete(path);
            removed++;
        }
        return removed;
    }
}
