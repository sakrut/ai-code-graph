using System.Diagnostics;

namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Helper methods for git operations.
/// </summary>
public static class GitHelpers
{
    /// <summary>
    /// Gets the current git commit hash, or null if not in a git repo.
    /// </summary>
    public static string? GetCurrentCommitHash()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return process.ExitCode == 0 && output.Length >= 7 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the timestamp of the last commit that modified a file matching the pattern.
    /// </summary>
    public static DateTimeOffset? GetLastModifiedTime(string pattern = "*.cs")
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"log -1 --format=%ct -- \"{pattern}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && long.TryParse(output, out var timestamp))
                return DateTimeOffset.FromUnixTimeSeconds(timestamp);

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<List<string>> GetChangedCsFiles(string fromRef, string toRef, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", $"diff --name-only {fromRef} {toRef} -- \"*.cs\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return new List<string>();

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0) return new List<string>();

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
