using System.Text.Json.Nodes;
using AiCodeGraph.Core.Query;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp.Handlers;

public class QueryHandler : IMcpToolHandler
{
    private readonly StorageService _storage;

    public QueryHandler(StorageService storage) => _storage = storage;

    public IReadOnlyList<string> SupportedTools { get; } = new[]
    {
        "cg_query", "cg_get_hotspots", "cg_get_callgraph", "cg_get_tree", "cg_dead_code", "cg_get_impact"
    };

    public JsonArray GetToolDefinitions() => new()
    {
        McpProtocolHelpers.CreateToolDef("cg_query",
            "Execute a graph query for method retrieval (recommended over search)",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["seed"] = new JsonObject { ["type"] = "string", ["description"] = "Method pattern, exact ID, namespace, or cluster name (supports wildcards: *Service*, MyApp.* )" },
                    ["expand"] = new JsonObject { ["type"] = "string", ["description"] = "Expansion direction: none|callers|callees|both", ["default"] = "both" },
                    ["depth"] = new JsonObject { ["type"] = "integer", ["description"] = "Max traversal depth (1-10)", ["default"] = 3 },
                    ["rank"] = new JsonObject { ["type"] = "string", ["description"] = "Ranking strategy: blast-radius|complexity|coupling|combined", ["default"] = "blast-radius" },
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Max results to return", ["default"] = 20 },
                    ["exclude_tests"] = new JsonObject { ["type"] = "boolean", ["description"] = "Exclude test methods", ["default"] = true }
                },
                ["required"] = new JsonArray { "seed" }
            }),
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
                    ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Maximum results to return", ["default"] = 20 },
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
            "cg_query" => ExecuteGraphQuery(args, ct),
            "cg_get_hotspots" => GetHotspots(args, ct),
            "cg_get_callgraph" => GetCallgraph(args, ct),
            "cg_get_tree" => GetTree(args, ct),
            "cg_dead_code" => GetDeadCode(args, ct),
            "cg_get_impact" => GetImpact(args, ct),
            _ => Task.FromResult($"Unknown tool: {toolName}")
        };
    }

    private async Task<string> ExecuteGraphQuery(JsonNode? args, CancellationToken ct)
    {
        var seed = args?["seed"]?.GetValue<string>();
        if (string.IsNullOrEmpty(seed))
            return "Error: 'seed' parameter required";

        var expand = args?["expand"]?.GetValue<string>() ?? "both";
        var depth = args?["depth"]?.GetValue<int>() ?? 3;
        var rank = args?["rank"]?.GetValue<string>() ?? "blast-radius";
        var top = args?["top"]?.GetValue<int>() ?? 20;
        var excludeTests = args?["exclude_tests"]?.GetValue<bool>() ?? true;

        // Determine if seed is a pattern (contains wildcards) or exact ID
        var isPattern = seed.Contains('*') || seed.Contains('?');

        var querySeed = isPattern
            ? new QuerySeed { MethodPattern = seed }
            : new QuerySeed { MethodId = seed };

        var expandDirection = expand.ToLower() switch
        {
            "none" => ExpandDirection.None,
            "callers" => ExpandDirection.Callers,
            "callees" => ExpandDirection.Callees,
            "both" => ExpandDirection.Both,
            _ => ExpandDirection.Both
        };

        var rankStrategy = rank.ToLower().Replace("-", "") switch
        {
            "blastradius" => RankStrategy.BlastRadius,
            "complexity" => RankStrategy.Complexity,
            "coupling" => RankStrategy.Coupling,
            "combined" => RankStrategy.Combined,
            _ => RankStrategy.BlastRadius
        };

        var query = new GraphQuery
        {
            Seed = querySeed,
            Expand = new QueryExpand
            {
                Direction = expandDirection,
                MaxDepth = Math.Max(1, Math.Min(10, depth)),
                IncludeTransitive = expandDirection != ExpandDirection.None
            },
            Filter = excludeTests ? new QueryFilter { ExcludeNamespaces = new List<string> { "*.Tests.*", "*.Test.*", "*Tests", "*Test" } } : null,
            Rank = new QueryRank
            {
                Strategy = rankStrategy,
                Descending = true
            },
            Output = new QueryOutput
            {
                MaxResults = top,
                IncludeMetrics = true,
                IncludeLocation = true
            }
        };

        var traversalEngine = new GraphTraversalEngine(_storage);
        var executor = new GraphQueryExecutor(_storage, traversalEngine);
        var result = await executor.ExecuteAsync(query, useCache: true, ct: ct);

        if (!result.Success)
            return $"Error: {result.Error}";

        return FormatQueryResult(result, seed, expand, depth, rank);
    }

    private static string FormatQueryResult(QueryResult result, string seed, string direction, int depth, string rank)
    {
        var lines = new List<string>();

        // Summary line
        lines.Add($"Query: seed={seed}, direction={direction}, depth={depth}, rank={rank}");
        lines.Add($"{result.Nodes.Count} results (of {result.TotalMatches} total), ranked by {rank}:");
        lines.Add("");

        // Compact results: [rank] metrics method location
        var index = 1;
        foreach (var node in result.Nodes)
        {
            var br = node.RankScore.HasValue ? $"BR={node.RankScore:F0}" : "";
            var cc = node.Complexity.HasValue ? $"CC={node.Complexity}" : "";
            var metrics = string.Join(" ", new[] { br, cc }.Where(s => !string.IsNullOrEmpty(s)));

            var location = node.FilePath != null
                ? $" {Path.GetFileName(node.FilePath)}:{node.Line}"
                : "";

            lines.Add($"[{index}] {metrics} {node.FullName}{location}");
            index++;
        }

        return string.Join("\n", lines);
    }

    private async Task<string> GetHotspots(JsonNode? args, CancellationToken ct)
    {
        var top = args?["top"]?.GetValue<int>() ?? 10;
        var threshold = args?["threshold"]?.GetValue<int>();
        var sortBy = args?["sort"]?.GetValue<string>() ?? "complexity";

        var hotspots = await _storage.GetHotspotsWithThresholdAsync(top, threshold, sortBy, ct);
        if (hotspots.Count == 0) return "No hotspots found.";

        // Compact output: one line per item with MethodId
        var lines = new List<string>();
        var showBlast = sortBy is "blast-radius" or "blast" or "risk";
        foreach (var h in hotspots)
        {
            var location = h.FilePath != null ? $" {Path.GetFileName(h.FilePath)}:{h.StartLine}" : "";
            var blastInfo = showBlast && h.BlastRadius > 0 ? $" Blast:{h.BlastRadius}" : "";
            lines.Add($"{h.FullName} CC:{h.Complexity} LOC:{h.Loc} Nest:{h.Nesting}{blastInfo}{location}");
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
        var top = args?["top"]?.GetValue<int>() ?? 20;
        var includeOverrides = args?["include_overrides"]?.GetValue<bool>() ?? false;

        var deadCode = await _storage.GetDeadCodeAsync(includeOverrides, ct);
        if (deadCode.Count == 0) return "No dead code detected.";

        var total = deadCode.Count;
        // Compact output: one line per item
        var lines = new List<string>();
        foreach (var m in deadCode.Take(top))
        {
            var location = m.FilePath != null ? $" {Path.GetFileName(m.FilePath)}:{m.StartLine}" : "";
            lines.Add($"{m.FullName} — 0 callers{location}");
        }
        if (total > top)
            lines.Add($"(+{total - top} more)");

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
