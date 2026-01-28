using System.Text.Json.Nodes;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp.Handlers;

public class QueryHandler : IMcpToolHandler
{
    private readonly StorageService _storage;

    public QueryHandler(StorageService storage) => _storage = storage;

    public IReadOnlyList<string> SupportedTools { get; } = new[]
    {
        "cg_get_hotspots", "cg_get_callgraph", "cg_get_tree", "cg_dead_code", "cg_get_impact"
    };

    public JsonArray GetToolDefinitions() => new()
    {
        McpProtocolHelpers.CreateToolDef("cg_get_hotspots",
            "Get top complexity hotspots",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 10 },
                    ["threshold"] = new JsonObject { ["type"] = "integer", ["description"] = "Minimum complexity threshold" }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_get_callgraph",
            "Explore method call graph: callers, callees, or both directions",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Method name or pattern" },
                    ["depth"] = new JsonObject { ["type"] = "integer", ["description"] = "Traversal depth", ["default"] = 2 },
                    ["direction"] = new JsonObject { ["type"] = "string", ["description"] = "callers|callees|both", ["default"] = "both" }
                },
                ["required"] = new JsonArray { "method" }
            }),
        McpProtocolHelpers.CreateToolDef("cg_get_tree",
            "Display code structure: projects, namespaces, types, and methods",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["namespace"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: filter by namespace prefix" },
                    ["type"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: filter by type name" },
                    ["include_private"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include non-public methods", ["default"] = false }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_dead_code",
            "Find methods with no callers (potential dead code)",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["include_overrides"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include override/abstract methods", ["default"] = false }
                }
            }),
        McpProtocolHelpers.CreateToolDef("cg_get_impact",
            "Show transitive impact of changing a method (all callers up the call chain)",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Method name or pattern" },
                    ["depth"] = new JsonObject { ["type"] = "integer", ["description"] = "Max traversal depth (unlimited if omitted)" }
                },
                ["required"] = new JsonArray { "method" }
            })
    };

    public Task<string> HandleAsync(string toolName, JsonNode? args, CancellationToken ct)
    {
        return toolName switch
        {
            "cg_get_hotspots" => GetHotspots(args, ct),
            "cg_get_callgraph" => GetCallgraph(args, ct),
            "cg_get_tree" => GetTree(args, ct),
            "cg_dead_code" => GetDeadCode(args, ct),
            "cg_get_impact" => GetImpact(args, ct),
            _ => Task.FromResult($"Unknown tool: {toolName}")
        };
    }

    private async Task<string> GetHotspots(JsonNode? args, CancellationToken ct)
    {
        var top = args?["top"]?.GetValue<int>() ?? 10;
        var threshold = args?["threshold"]?.GetValue<int>();

        var hotspots = await _storage.GetHotspotsWithThresholdAsync(top, threshold, ct);
        if (hotspots.Count == 0) return "No hotspots found.";

        var lines = new List<string> { $"{"Method",-50} {"CC",4} {"LOC",4} {"Nest",4}" };
        foreach (var h in hotspots)
        {
            var name = h.FullName.Length > 50 ? h.FullName[..47] + "..." : h.FullName;
            lines.Add($"{name,-50} {h.Complexity,4} {h.Loc,4} {h.Nesting,4}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> GetCallgraph(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";
        var depth = args?["depth"]?.GetValue<int>() ?? 2;
        var direction = args?["direction"]?.GetValue<string>() ?? "both";

        var matches = await _storage.SearchMethodsAsync(method, ct);
        if (matches.Count == 0) return $"Method not found: '{method}'";

        var rootId = matches[0].Id;
        var rootInfo = await _storage.GetMethodInfoAsync(rootId, ct);

        var visited = new HashSet<string> { rootId };
        var queue = new Queue<(string Id, int Depth)>();
        queue.Enqueue((rootId, 0));

        var lines = new List<string> { $"{rootInfo?.FullName ?? rootId}" };

        while (queue.Count > 0)
        {
            var (currentId, currentDepth) = queue.Dequeue();
            if (currentDepth >= depth) continue;
            var indent = new string(' ', (currentDepth + 1) * 2);

            if (direction is "callees" or "both")
            {
                foreach (var calleeId in await _storage.GetCalleesAsync(currentId, ct))
                {
                    var info = await _storage.GetMethodInfoAsync(calleeId, ct);
                    var marker = visited.Add(calleeId) ? "" : " (*)";
                    lines.Add($"{indent}-> {info?.FullName ?? calleeId}{marker}");
                    if (marker == "") queue.Enqueue((calleeId, currentDepth + 1));
                }
            }

            if (direction is "callers" or "both")
            {
                foreach (var callerId in await _storage.GetCallersAsync(currentId, ct))
                {
                    var info = await _storage.GetMethodInfoAsync(callerId, ct);
                    var marker = visited.Add(callerId) ? "" : " (*)";
                    lines.Add($"{indent}<- {info?.FullName ?? callerId}{marker}");
                    if (marker == "") queue.Enqueue((callerId, currentDepth + 1));
                }
            }
        }

        return string.Join("\n", lines);
    }

    private async Task<string> GetTree(JsonNode? args, CancellationToken ct)
    {
        var nsFilter = args?["namespace"]?.GetValue<string>();
        var typeFilter = args?["type"]?.GetValue<string>();
        var includePrivate = args?["include_private"]?.GetValue<bool>() ?? false;
        var skipTests = args?["skip_tests"]?.GetValue<bool>() ?? false;
        var skipInterfaces = args?["skip_interfaces"]?.GetValue<bool>() ?? false;
        var skipNs = args?["skip_ns"]?.GetValue<string>();

        var rows = await _storage.GetTreeAsync(nsFilter, typeFilter, includePrivate, includeConstructors: false, skipTests, skipInterfaces, skipNs, ct);
        if (rows.Count == 0) return "No results found.";

        var lines = new List<string>();
        var lastProject = "";
        var lastNs = "";
        var lastType = "";

        foreach (var row in rows)
        {
            if (row.ProjectName != lastProject)
            {
                lines.Add(row.ProjectName);
                lastProject = row.ProjectName;
                lastNs = "";
                lastType = "";
            }
            if (row.NamespaceName != lastNs)
            {
                lines.Add($"  {row.NamespaceName}");
                lastNs = row.NamespaceName;
                lastType = "";
            }
            if (row.TypeName != lastType)
            {
                var kindTag = row.TypeKind switch
                {
                    "Class" => "[C]",
                    "Interface" => "[I]",
                    "Record" => "[R]",
                    "Struct" => "[S]",
                    "Enum" => "[E]",
                    _ => "[?]"
                };
                lines.Add($"    {kindTag} {row.TypeName}");
                lastType = row.TypeName;
            }
            var visibilityTag = row.Accessibility != "Public" ? $" [{row.Accessibility.ToLower()}]" : "";
            lines.Add($"        {row.ReturnType} {row.MethodName}(){visibilityTag}");
        }

        return string.Join("\n", lines);
    }

    private async Task<string> GetDeadCode(JsonNode? args, CancellationToken ct)
    {
        var includeOverrides = args?["include_overrides"]?.GetValue<bool>() ?? false;

        var deadCode = await _storage.GetDeadCodeAsync(includeOverrides, ct);
        if (deadCode.Count == 0) return "No dead code detected.";

        var lines = new List<string> { $"Found {deadCode.Count} potentially unreachable methods:", "" };
        foreach (var m in deadCode.Take(30))
        {
            var file = m.FilePath != null ? $" ({Path.GetFileName(m.FilePath)}:{m.StartLine})" : "";
            lines.Add($"  CC={m.Complexity,2} {m.FullName}{file}");
        }
        if (deadCode.Count > 30)
            lines.Add($"\n  ... +{deadCode.Count - 30} more");

        return string.Join("\n", lines);
    }

    private async Task<string> GetImpact(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";
        var maxDepth = args?["depth"]?.GetValue<int>();

        var matches = await _storage.SearchMethodsAsync(method, ct);
        if (matches.Count == 0) return $"Method not found: '{method}'";

        var targetId = matches[0].Id;
        var targetInfo = await _storage.GetMethodInfoAsync(targetId, ct);

        var visited = new HashSet<string> { targetId };
        var queue = new Queue<(string Id, int Depth)>();
        var depthMap = new Dictionary<string, int> { [targetId] = 0 };
        var entryPoints = new List<string>();

        queue.Enqueue((targetId, 0));

        while (queue.Count > 0)
        {
            var (currentId, currentDepth) = queue.Dequeue();
            if (maxDepth.HasValue && currentDepth >= maxDepth.Value) continue;

            var callers = await _storage.GetCallersAsync(currentId, ct);
            if (callers.Count == 0 && currentId != targetId)
                entryPoints.Add(currentId);

            foreach (var callerId in callers)
            {
                if (visited.Add(callerId))
                {
                    depthMap[callerId] = currentDepth + 1;
                    queue.Enqueue((callerId, currentDepth + 1));
                }
            }
        }

        var lines = new List<string>
        {
            $"Impact: {targetInfo?.FullName ?? targetId}",
            $"Affected: {visited.Count} methods, {entryPoints.Count} entry points",
            ""
        };

        var byDepth = visited.Where(id => id != targetId)
            .GroupBy(id => depthMap.GetValueOrDefault(id))
            .OrderBy(g => g.Key);

        foreach (var group in byDepth)
        {
            lines.Add($"Depth {group.Key}:");
            foreach (var id in group.Take(20))
            {
                var info = await _storage.GetMethodInfoAsync(id, ct);
                var ep = entryPoints.Contains(id) ? " [entry]" : "";
                lines.Add($"  {info?.FullName ?? id}{ep}");
            }
            if (group.Count() > 20)
                lines.Add($"  ... +{group.Count() - 20} more");
        }

        return string.Join("\n", lines);
    }
}
