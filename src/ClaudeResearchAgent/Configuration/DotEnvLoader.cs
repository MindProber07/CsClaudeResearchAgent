namespace ClaudeResearchAgent.Configuration;

/// <summary>
/// Optional local-development convenience: loads <c>KEY=VALUE</c> lines from a <c>.env</c> file in
/// the current directory into the process environment. This is never the primary mechanism for
/// providing secrets — <see cref="EnvironmentValidator"/> only ever reads real environment
/// variables — and it never overwrites a variable the environment (or shell) already set, so CI
/// and production configuration always wins.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>Loads <paramref name="path"/> into the process environment if it exists; a no-op
    /// otherwise. Safe to call unconditionally at startup.</summary>
    public static void LoadIfPresent(string path = ".env")
    {
        string currentFolder = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
