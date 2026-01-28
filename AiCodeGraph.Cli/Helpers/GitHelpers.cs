using System.Diagnostics;

namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Helper methods for git operations.
/// </summary>
public static class GitHelpers
{
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
