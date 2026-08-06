using System.IO;

namespace SearchBook.Services;

public static class EnvironmentFile
{
    public const string GitHubTokenVariable = "SEARCHBOOK_GITHUB_TOKEN";

    public static string? GetGitHubToken() =>
        GetValue(GitHubTokenVariable, GetDefaultCandidatePaths());

    public static string? GetValue(string variableName, IEnumerable<string> candidatePaths)
    {
        var processValue = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(processValue)) return processValue.Trim();

        foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                    line = line[7..].TrimStart();

                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                if (!line[..separator].Trim().Equals(variableName, StringComparison.OrdinalIgnoreCase)) continue;

                var value = line[(separator + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') ||
                     (value[0] == '\'' && value[^1] == '\'')))
                    value = value[1..^1];

                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> GetDefaultCandidatePaths()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), ".env")
        };

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            if (!Directory.Exists(Path.Combine(directory.FullName, ".git"))) continue;
            candidates.Add(Path.Combine(directory.FullName, ".env"));
            break;
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
