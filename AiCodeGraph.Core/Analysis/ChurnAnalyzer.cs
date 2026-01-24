using System.Diagnostics;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Analysis;

public record ChurnResult(
    string MethodId,
    string MethodName,
    string? FilePath,
    int Changes,
    int CognitiveComplexity,
    float ChurnScore);

public class ChurnAnalyzer
{
    public async Task<List<ChurnResult>> AnalyzeAsync(
        IStorageService storage,
        string since,
        int top,
        CancellationToken ct)
    {
        var methods = await storage.GetMethodsForExportAsync(null, ct).ConfigureAwait(false);

        var byFile = methods
            .Where(m => m.FilePath != null && File.Exists(m.FilePath))
            .GroupBy(m => m.FilePath!);

        var results = new List<ChurnResult>();

        foreach (var fileGroup in byFile)
        {
            var commitCount = await GetCommitCount(fileGroup.Key, since, ct).ConfigureAwait(false);
            if (commitCount == 0) continue;

            foreach (var method in fileGroup)
            {
                var cc = method.Complexity;
                if (cc == 0) continue;
                var churnScore = commitCount * cc;
                results.Add(new ChurnResult(
                    method.Id,
                    method.FullName,
                    method.FilePath,
                    commitCount,
                    cc,
                    churnScore));
            }
        }

        return results.OrderByDescending(r => r.ChurnScore).Take(top).ToList();
    }

    private static async Task<int> GetCommitCount(string filePath, string since, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"log --oneline --since=\"{since}\" -- \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return 0;

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0) return 0;
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
