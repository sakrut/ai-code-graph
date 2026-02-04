using System.Text.Json.Nodes;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp.Handlers;

public class ContextHandler : IMcpToolHandler
{
    private readonly StorageService _storage;

    public ContextHandler(StorageService storage) => _storage = storage;

    public IReadOnlyList<string> SupportedTools { get; } = new[] { "cg_get_context" };

    public JsonArray GetToolDefinitions() => new()
    {
        McpProtocolHelpers.CreateToolDef("cg_get_context",
            "Get compact method context: complexity, callers, callees, cluster, duplicates",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Method name or pattern to search for" }
                },
                ["required"] = new JsonArray { "method" }
            })
    };

    public async Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";

        var matches = await _storage.SearchMethodsAsync(method, ct);
        if (matches.Count == 0) return $"Method not found: '{method}'";

        var targetId = matches.Count == 1
            ? matches[0].Id
            : matches.FirstOrDefault(m => m.FullName.Contains(method, StringComparison.OrdinalIgnoreCase)).Id ?? matches[0].Id;

        var info = await _storage.GetMethodInfoAsync(targetId, ct);
        if (info == null) return "Method info not found";

        var lines = new List<string>();
        AppendMethodHeader(lines, info.Value);
        await AppendMetricsAsync(lines, targetId, ct);
        await AppendCallersAsync(lines, targetId, ct);
        await AppendCalleesAsync(lines, targetId, ct);
        await AppendClusterInfoAsync(lines, targetId, ct);
        await AppendDuplicatesAsync(lines, targetId, ct);
        await AppendTestCoverageAsync(lines, info.Value.Name, ct);

        return string.Join("\n", lines);
    }

    private static void AppendMethodHeader(List<string> lines, (string Id, string Name, string FullName, string? FilePath, int StartLine) info)
    {
        lines.Add($"Method: {info.FullName}");
        lines.Add($"Id: {info.Id}");
        if (info.FilePath != null)
            lines.Add($"File: {info.FilePath}:{info.StartLine}");
    }

    private async Task AppendMetricsAsync(List<string> lines, string targetId, CancellationToken ct)
    {
        var metrics = await _storage.GetMethodMetricsAsync(targetId, ct);
        if (metrics != null)
            lines.Add($"Complexity: CC={metrics.Value.CognitiveComplexity} LOC={metrics.Value.LinesOfCode} Nesting={metrics.Value.NestingDepth}");
    }

    private async Task AppendCallersAsync(List<string> lines, string targetId, CancellationToken ct)
    {
        var callers = await _storage.GetCallersAsync(targetId, ct);
        if (callers.Count > 0)
        {
            var names = new List<string>();
            foreach (var cid in callers.Take(5))
            {
                var ci = await _storage.GetMethodInfoAsync(cid, ct);
                names.Add(ci?.Name ?? cid);
            }
            lines.Add($"Callers ({callers.Count}): {string.Join(", ", names)}");
        }
    }

    private async Task AppendCalleesAsync(List<string> lines, string targetId, CancellationToken ct)
    {
        var callees = await _storage.GetCalleesAsync(targetId, ct);
        if (callees.Count > 0)
        {
            var names = new List<string>();
            foreach (var cid in callees.Take(5))
            {
                var ci = await _storage.GetMethodInfoAsync(cid, ct);
                names.Add(ci?.Name ?? cid);
            }
            lines.Add($"Callees ({callees.Count}): {string.Join(", ", names)}");
        }
    }

    private async Task AppendClusterInfoAsync(List<string> lines, string targetId, CancellationToken ct)
    {
        var cluster = await _storage.GetMethodClusterAsync(targetId, ct);
        if (cluster != null)
            lines.Add($"Cluster: \"{cluster.Value.Label}\" ({cluster.Value.MemberCount} members, cohesion: {cluster.Value.Cohesion:F2})");

        if (cluster != null)
        {
            var clusters = await _storage.GetClustersAsync(ct);
            var myCluster = clusters.FirstOrDefault(c => c.MethodIds.Contains(targetId));
            if (myCluster != null && myCluster.MethodIds.Count > 1)
            {
                var recentChanges = new List<(string MethodName, TimeSpan Age)>();
                foreach (var memberId in myCluster.MethodIds.Where(id => id != targetId).Take(10))
                {
                    var memberInfo = await _storage.GetMethodInfoAsync(memberId, ct);
                    if (memberInfo?.FilePath == null || !File.Exists(memberInfo.Value.FilePath)) continue;
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("git", $"log -1 --format=%ct -- \"{memberInfo.Value.FilePath}\"")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var process = System.Diagnostics.Process.Start(psi);
                        if (process != null)
                        {
                            var output = (await process.StandardOutput.ReadToEndAsync(ct)).Trim();
                            await process.WaitForExitAsync(ct);
                            if (long.TryParse(output, out var ts))
                            {
                                var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts);
                                recentChanges.Add((memberInfo.Value.Name, age));
                            }
                        }
                    }
                    catch { /* skip on git errors */ }
                }
                if (recentChanges.Count > 0)
                {
                    var top3 = recentChanges.OrderBy(r => r.Age).Take(3);
                    var formatted = string.Join(", ", top3.Select(r => $"{r.MethodName} ({McpProtocolHelpers.FormatAge(r.Age)})"));
                    lines.Add($"Recent cluster activity: {formatted}");
                }
            }
        }
    }

    private async Task AppendDuplicatesAsync(List<string> lines, string targetId, CancellationToken ct)
    {
        var dupes = await _storage.GetMethodDuplicatesAsync(targetId, ct);
        if (dupes.Count > 0)
        {
            var dupeStrs = dupes.Take(3).Select(d =>
            {
                var parenIdx = d.OtherFullName.IndexOf('(');
                var nameOnly = parenIdx >= 0 ? d.OtherFullName[..parenIdx] : d.OtherFullName;
                var parts = nameOnly.Split('.');
                var shortName = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
                return $"{shortName} ({d.HybridScore:F2})";
            });
            lines.Add($"Duplicates ({dupes.Count}): {string.Join(", ", dupeStrs)}");
        }
    }

    private async Task AppendTestCoverageAsync(List<string> lines, string methodName, CancellationToken ct)
    {
        var testMatches = await _storage.SearchMethodsAsync($"%{methodName}%Test%", ct);
        var testMatches2 = await _storage.SearchMethodsAsync($"%Test%{methodName}%", ct);
        var allTests = testMatches.Concat(testMatches2)
            .DistinctBy(t => t.Id)
            .Where(t => t.FullName.Contains("Test", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (allTests.Count > 0)
        {
            var testNames = allTests.Take(5).Select(t =>
            {
                var parenIdx = t.FullName.IndexOf('(');
                var nameOnly = parenIdx >= 0 ? t.FullName[..parenIdx] : t.FullName;
                var parts = nameOnly.Split('.');
                return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
            });
            lines.Add($"Tests ({allTests.Count}): {string.Join(", ", testNames)}");
        }
        else
        {
            lines.Add("Tests: none found");
        }
    }
}
