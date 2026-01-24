using System.Text.Json;
using System.Text.Json.Nodes;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp.Handlers;

public class DuplicatesHandler : IMcpToolHandler
{
    private readonly StorageService _storage;

    public DuplicatesHandler(StorageService storage) => _storage = storage;

    public IReadOnlyList<string> SupportedTools { get; } = new[]
    {
        "cg_get_duplicates", "cg_get_clusters", "cg_export_graph"
    };

    public JsonArray GetToolDefinitions() => new()
    {
        McpProtocolHelpers.CreateToolDef("cg_get_duplicates",
            "Get detected code clones, optionally for a specific method",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: method name to find duplicates for" },
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 10 }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_get_clusters",
            "List intent clusters: groups of methods with similar purpose",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            }),
        McpProtocolHelpers.CreateToolDef("cg_export_graph",
            "Export code graph data (methods, relationships, metrics) as JSON",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["concept"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: filter by cluster label/concept" }
                }
            })
    };

    public Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct)
    {
        return toolName switch
        {
            "cg_get_duplicates" => GetDuplicates(args, ct),
            "cg_get_clusters" => GetClusters(ct),
            "cg_export_graph" => ExportGraph(args, ct),
            _ => Task.FromResult($"Unknown tool: {toolName}")
        };
    }

    private async Task<string> GetDuplicates(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        var top = args?["top"]?.GetValue<int>() ?? 10;

        if (!string.IsNullOrEmpty(method))
        {
            var matches = await _storage.SearchMethodsAsync(method, ct);
            if (matches.Count == 0) return $"Method not found: '{method}'";
            var targetId = matches[0].Id;
            var dupes = await _storage.GetMethodDuplicatesAsync(targetId, ct);
            if (dupes.Count == 0) return "No duplicates found for this method.";

            var lines = new List<string>();
            foreach (var d in dupes.Take(top))
                lines.Add($"{d.Type,-8} {d.HybridScore:F2}  {d.OtherFullName}");
            return string.Join("\n", lines);
        }
        else
        {
            var clones = await _storage.GetClonePairsAsync(cancellationToken: ct);
            if (clones.Count == 0) return "No duplicates detected.";

            var lines = new List<string>();
            foreach (var c in clones.Take(top))
                lines.Add($"{c.Type,-8} {c.HybridScore:F2}  {c.MethodIdA} <-> {c.MethodIdB}");
            return string.Join("\n", lines);
        }
    }

    private async Task<string> GetClusters(CancellationToken ct)
    {
        var clusters = await _storage.GetClustersAsync(ct);
        if (clusters.Count == 0) return "No clusters found.";

        var lines = new List<string>();
        foreach (var cluster in clusters)
        {
            lines.Add($"[{cluster.Id}] {cluster.Label} (cohesion: {cluster.Cohesion:F2}, members: {cluster.MethodIds.Count})");
            foreach (var methodId in cluster.MethodIds.Take(3))
            {
                var info = await _storage.GetMethodInfoAsync(methodId, ct);
                lines.Add($"    {info?.FullName ?? methodId}");
            }
            if (cluster.MethodIds.Count > 3)
                lines.Add($"    ... and {cluster.MethodIds.Count - 3} more");
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> ExportGraph(JsonNode? args, CancellationToken ct)
    {
        var concept = args?["concept"]?.GetValue<string>();

        var methods = await _storage.GetMethodsForExportAsync(concept, ct);
        if (methods.Count == 0) return "No methods found.";

        var methodIds = methods.Select(m => m.Id).ToHashSet();
        var relationships = await _storage.GetCallGraphForMethodsAsync(methodIds, ct);

        var result = JsonSerializer.Serialize(new
        {
            methods = methods.Select(m => new
            {
                id = m.Id,
                fullName = m.FullName,
                returnType = m.ReturnType,
                filePath = m.FilePath,
                line = m.StartLine,
                complexity = m.Complexity,
                loc = m.Loc,
                nesting = m.Nesting,
                cluster = m.ClusterLabel
            }),
            relationships = relationships.Select(r => new { caller = r.CallerId, callee = r.CalleeId }),
            metadata = new { methodCount = methods.Count, relationshipCount = relationships.Count, conceptFilter = concept }
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return result;
    }
}
