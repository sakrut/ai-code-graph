using System.Text.Json;
using System.Text.Json.Nodes;
using AiCodeGraph.Core;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Mcp;

public class McpServer
{
    private readonly string _dbPath;
    private StorageService? _storage;
    private VectorIndex? _vectorIndex;

    public McpServer(string dbPath)
    {
        _dbPath = dbPath;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break; // EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonNode.Parse(line);
                if (request == null) continue;

                var response = await HandleMessage(request, cancellationToken);
                if (response != null)
                {
                    Console.WriteLine(response.ToJsonString());
                    Console.Out.Flush();
                }
            }
            catch (Exception ex)
            {
                var errorResponse = CreateError(null, -32603, $"Internal error: {ex.Message}");
                Console.WriteLine(errorResponse.ToJsonString());
                Console.Out.Flush();
            }
        }
    }

    private async Task<JsonNode?> HandleMessage(JsonNode message, CancellationToken ct)
    {
        var method = message["method"]?.GetValue<string>();
        var id = message["id"];

        // Notifications (no id) - don't respond
        if (id == null && method == "notifications/initialized")
            return null;

        return method switch
        {
            "initialize" => HandleInitialize(id),
            "tools/list" => HandleToolsList(id),
            "tools/call" => await HandleToolCall(message, id, ct),
            _ => id != null ? CreateError(id, -32601, $"Method not found: {method}") : null
        };
    }

    private JsonNode HandleInitialize(JsonNode? id)
    {
        return CreateResult(id, new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "ai-code-graph",
                ["version"] = "0.1.0"
            }
        });
    }

    private JsonNode HandleToolsList(JsonNode? id)
    {
        var tools = new JsonArray
        {
            CreateToolDef("cg_get_context",
                "Get compact method context: complexity, callers, callees, cluster, duplicates",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Method name or pattern to search for" }
                    },
                    ["required"] = new JsonArray { "method" }
                }),
            CreateToolDef("cg_get_hotspots",
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
            CreateToolDef("cg_token_search",
                "Search code by token overlap",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject { ["type"] = "string", ["description"] = "Search query (uses method identifier tokens)" },
                        ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 5 }
                    },
                    ["required"] = new JsonArray { "query" }
                }),
            CreateToolDef("cg_get_duplicates",
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
            CreateToolDef("cg_get_callgraph",
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
            CreateToolDef("cg_get_tree",
                "Display code structure: projects, namespaces, types, and methods",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["namespace"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: filter by namespace prefix" },
                        ["type"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: filter by type name" }
                    }
                }),
            CreateToolDef("cg_get_similar",
                "Find methods with similar semantic intent",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["method"] = new JsonObject { ["type"] = "string", ["description"] = "Method name or pattern" },
                        ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 10 }
                    },
                    ["required"] = new JsonArray { "method" }
                }),
            CreateToolDef("cg_get_clusters",
                "List intent clusters: groups of methods with similar purpose",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }),
            CreateToolDef("cg_export_graph",
                "Export code graph data (methods, relationships, metrics) as JSON",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["concept"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: filter by cluster label/concept" }
                    }
                }),
            CreateToolDef("cg_get_drift",
                "Detect architectural drift from a baseline snapshot",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["baseline"] = new JsonObject { ["type"] = "string", ["description"] = "Path to baseline.db (default: auto-detect next to graph.db)" }
                    }
                }),
            CreateToolDef("cg_dead_code",
                "Find methods with no callers (potential dead code)",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["include_overrides"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include override/abstract methods", ["default"] = false }
                    }
                }),
            CreateToolDef("cg_get_impact",
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
                }),
            CreateToolDef("cg_analyze",
                "Analyze a .NET solution and build/rebuild the code graph database",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["solution"] = new JsonObject { ["type"] = "string", ["description"] = "Path to .sln file (auto-discovers if omitted)" },
                        ["save_baseline"] = new JsonObject { ["type"] = "boolean", ["description"] = "Save result as baseline for drift detection", ["default"] = false }
                    }
                }),
            CreateToolDef("cg_churn",
                "Show methods with high change-frequency × complexity (churn hotspots)",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["since"] = new JsonObject { ["type"] = "string", ["description"] = "Git log time range (e.g. '6 months ago')", ["default"] = "6 months ago" },
                        ["top"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of results", ["default"] = 20 }
                    }
                })
        };

        return CreateResult(id, new JsonObject { ["tools"] = tools });
    }

    private async Task<JsonNode> HandleToolCall(JsonNode message, JsonNode? id, CancellationToken ct)
    {
        var toolName = message["params"]?["name"]?.GetValue<string>();
        var args = message["params"]?["arguments"];

        if (toolName != "cg_analyze" && !File.Exists(_dbPath))
            return CreateToolResult(id, $"Error: Database not found at {_dbPath}. Run 'ai-code-graph analyze' first.", true);

        if (toolName != "cg_analyze" && _storage == null)
        {
            _storage = new StorageService(_dbPath);
            await _storage.OpenAsync(ct);
        }

        try
        {
            var result = toolName switch
            {
                "cg_get_context" => await ToolGetContext(args, ct),
                "cg_get_hotspots" => await ToolGetHotspots(args, ct),
                "cg_token_search" => await ToolSearchCode(args, ct),
                "cg_get_duplicates" => await ToolGetDuplicates(args, ct),
                "cg_get_callgraph" => await ToolGetCallgraph(args, ct),
                "cg_get_tree" => await ToolGetTree(args, ct),
                "cg_get_similar" => await ToolGetSimilar(args, ct),
                "cg_get_clusters" => await ToolGetClusters(ct),
                "cg_export_graph" => await ToolExportGraph(args, ct),
                "cg_get_drift" => await ToolGetDrift(args, ct),
                "cg_dead_code" => await ToolGetDeadCode(args, ct),
                "cg_get_impact" => await ToolGetImpact(args, ct),
                "cg_analyze" => await ToolAnalyze(args, ct),
                "cg_churn" => await ToolGetChurn(args, ct),
                _ => $"Unknown tool: {toolName}"
            };

            return CreateToolResult(id, result, false);
        }
        catch (Exception ex)
        {
            return CreateToolResult(id, $"Error: {ex.Message}", true);
        }
    }

    private async Task<string> ToolGetContext(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";

        var matches = await _storage!.SearchMethodsAsync(method, ct);
        if (matches.Count == 0) return $"Method not found: '{method}'";

        var targetId = matches.Count == 1
            ? matches[0].Id
            : matches.FirstOrDefault(m => m.FullName.Contains(method, StringComparison.OrdinalIgnoreCase)).Id ?? matches[0].Id;

        var info = await _storage.GetMethodInfoAsync(targetId, ct);
        if (info == null) return "Method info not found";

        var lines = new List<string>();
        lines.Add($"Method: {info.Value.FullName}");
        if (info.Value.FilePath != null)
            lines.Add($"File: {info.Value.FilePath}:{info.Value.StartLine}");

        var metrics = await _storage.GetMethodMetricsAsync(targetId, ct);
        if (metrics != null)
            lines.Add($"Complexity: CC={metrics.Value.CognitiveComplexity} LOC={metrics.Value.LinesOfCode} Nesting={metrics.Value.NestingDepth}");

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

        var cluster = await _storage.GetMethodClusterAsync(targetId, ct);
        if (cluster != null)
            lines.Add($"Cluster: \"{cluster.Value.Label}\" ({cluster.Value.MemberCount} members, cohesion: {cluster.Value.Cohesion:F2})");

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

        return string.Join("\n", lines);
    }

    private async Task<string> ToolGetHotspots(JsonNode? args, CancellationToken ct)
    {
        var top = args?["top"]?.GetValue<int>() ?? 10;
        var threshold = args?["threshold"]?.GetValue<int>();

        var hotspots = await _storage!.GetHotspotsWithThresholdAsync(top, threshold, ct);
        if (hotspots.Count == 0) return "No hotspots found.";

        var lines = new List<string> { $"{"Method",-50} {"CC",4} {"LOC",4} {"Nest",4}" };
        foreach (var h in hotspots)
        {
            var name = h.FullName.Length > 50 ? h.FullName[..47] + "..." : h.FullName;
            lines.Add($"{name,-50} {h.Complexity,4} {h.Loc,4} {h.Nesting,4}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> ToolSearchCode(JsonNode? args, CancellationToken ct)
    {
        var query = args?["query"]?.GetValue<string>();
        if (string.IsNullOrEmpty(query)) return "Error: 'query' parameter required";
        var top = args?["top"]?.GetValue<int>() ?? 5;

        var embeddings = await _storage!.GetEmbeddingsAsync(ct);
        if (embeddings.Count == 0) return "No embeddings in database.";

        using var engine = new HashEmbeddingEngine();
        var queryVector = engine.GenerateEmbedding(query);
        if (_vectorIndex == null)
        {
            _vectorIndex = new VectorIndex();
            _vectorIndex.BuildIndex(embeddings);
        }
        var results = _vectorIndex.Search(queryVector, top);

        if (results.Count == 0) return "No results found.";

        var lines = new List<string>();
        foreach (var (methodId, score) in results)
        {
            var info = await _storage.GetMethodInfoAsync(methodId, ct);
            var name = info?.FullName ?? methodId;
            var file = info?.FilePath != null ? $"  {Path.GetFileName(info.Value.FilePath)}:{info.Value.StartLine}" : "";
            lines.Add($"{score:F3}  {name}{file}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> ToolGetDuplicates(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        var top = args?["top"]?.GetValue<int>() ?? 10;

        if (!string.IsNullOrEmpty(method))
        {
            var matches = await _storage!.SearchMethodsAsync(method, ct);
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
            var clones = await _storage!.GetClonePairsAsync(cancellationToken: ct);
            if (clones.Count == 0) return "No duplicates detected.";

            var lines = new List<string>();
            foreach (var c in clones.Take(top))
                lines.Add($"{c.Type,-8} {c.HybridScore:F2}  {c.MethodIdA} <-> {c.MethodIdB}");
            return string.Join("\n", lines);
        }
    }

    private async Task<string> ToolGetCallgraph(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";
        var depth = args?["depth"]?.GetValue<int>() ?? 2;
        var direction = args?["direction"]?.GetValue<string>() ?? "both";

        var matches = await _storage!.SearchMethodsAsync(method, ct);
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

    private async Task<string> ToolGetTree(JsonNode? args, CancellationToken ct)
    {
        var nsFilter = args?["namespace"]?.GetValue<string>();
        var typeFilter = args?["type"]?.GetValue<string>();

        var rows = await _storage!.GetTreeAsync(nsFilter, typeFilter, ct);
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
            lines.Add($"        {row.ReturnType} {row.MethodName}()");
        }

        return string.Join("\n", lines);
    }

    private async Task<string> ToolGetSimilar(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";
        var top = args?["top"]?.GetValue<int>() ?? 10;

        var matches = await _storage!.SearchMethodsAsync(method, ct);
        if (matches.Count == 0) return $"Method not found: '{method}'";

        var targetId = matches[0].Id;
        var allEmbeddings = await _storage.GetEmbeddingsAsync(ct);
        if (allEmbeddings.Count == 0) return "No embeddings in database.";

        var targetEmbedding = allEmbeddings.FirstOrDefault(e => e.MethodId == targetId);
        if (targetEmbedding.Vector == null) return $"No embedding found for '{method}'";

        if (_vectorIndex == null)
        {
            _vectorIndex = new VectorIndex();
            _vectorIndex.BuildIndex(allEmbeddings);
        }
        var results = _vectorIndex.Search(targetEmbedding.Vector, top + 1)
            .Where(r => r.Id != targetId)
            .Take(top)
            .ToList();

        if (results.Count == 0) return "No similar methods found.";

        var lines = new List<string> { $"Similar to: {matches[0].FullName}", "" };
        foreach (var (id, score) in results)
        {
            var info = await _storage.GetMethodInfoAsync(id, ct);
            lines.Add($"  {score:F3}  {info?.FullName ?? id}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> ToolGetClusters(CancellationToken ct)
    {
        var clusters = await _storage!.GetClustersAsync(ct);
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

    private async Task<string> ToolExportGraph(JsonNode? args, CancellationToken ct)
    {
        var concept = args?["concept"]?.GetValue<string>();

        var methods = await _storage!.GetMethodsForExportAsync(concept, ct);
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

    private async Task<string> ToolGetDrift(JsonNode? args, CancellationToken ct)
    {
        var baselinePath = args?["baseline"]?.GetValue<string>();
        if (string.IsNullOrEmpty(baselinePath))
            baselinePath = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "baseline.db");

        if (!File.Exists(baselinePath))
            return $"Baseline not found at {baselinePath}. Run 'ai-code-graph analyze --save-baseline' first.";

        var detector = new AiCodeGraph.Core.Drift.DriftDetector();
        var report = await detector.CompareAsync(_dbPath, baselinePath, ct);

        var hasDrift = report.NewMethods.Count > 0 || report.RemovedMethods.Count > 0
            || report.Regressions.Count > 0 || report.NewDuplicates.Count > 0
            || report.IntentScattering.Count > 0;

        if (!hasDrift) return "No drift detected.";

        var lines = new List<string>();

        if (report.NewMethods.Count > 0)
        {
            lines.Add($"New Methods ({report.NewMethods.Count}):");
            foreach (var m in report.NewMethods.Take(10))
                lines.Add($"  + {m.FullName}");
            if (report.NewMethods.Count > 10)
                lines.Add($"  ... and {report.NewMethods.Count - 10} more");
            lines.Add("");
        }

        if (report.RemovedMethods.Count > 0)
        {
            lines.Add($"Removed Methods ({report.RemovedMethods.Count}):");
            foreach (var m in report.RemovedMethods.Take(10))
                lines.Add($"  - {m.FullName}");
            if (report.RemovedMethods.Count > 10)
                lines.Add($"  ... and {report.RemovedMethods.Count - 10} more");
            lines.Add("");
        }

        if (report.Regressions.Count > 0)
        {
            lines.Add($"Complexity Regressions ({report.Regressions.Count}):");
            foreach (var r in report.Regressions.Take(10))
                lines.Add($"  {r.FullName}: {r.BaselineComplexity} -> {r.CurrentComplexity} (+{(r.PercentageIncrease * 100):F0}%)");
            lines.Add("");
        }

        if (report.NewDuplicates.Count > 0)
        {
            lines.Add($"New Duplicates ({report.NewDuplicates.Count}):");
            foreach (var d in report.NewDuplicates.Take(10))
                lines.Add($"  {d.MethodIdA} <-> {d.MethodIdB} ({d.HybridScore:F2})");
            lines.Add("");
        }

        if (report.IntentScattering.Count > 0)
        {
            lines.Add($"Intent Scattering ({report.IntentScattering.Count}):");
            foreach (var s in report.IntentScattering.Take(5))
                lines.Add($"  '{s.ClusterLabel}' spread to: {string.Join(", ", s.NewNamespaces)}");
        }

        return string.Join("\n", lines);
    }

    private async Task<string> ToolGetDeadCode(JsonNode? args, CancellationToken ct)
    {
        var includeOverrides = args?["include_overrides"]?.GetValue<bool>() ?? false;

        var deadCode = await _storage!.GetDeadCodeAsync(includeOverrides, ct);
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

    private async Task<string> ToolGetImpact(JsonNode? args, CancellationToken ct)
    {
        var method = args?["method"]?.GetValue<string>();
        if (string.IsNullOrEmpty(method)) return "Error: 'method' parameter required";
        var maxDepth = args?["depth"]?.GetValue<int>();

        var matches = await _storage!.SearchMethodsAsync(method, ct);
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

    private async Task<string> ToolAnalyze(JsonNode? args, CancellationToken ct)
    {
        var solutionPath = args?["solution"]?.GetValue<string>();
        var saveBaseline = args?["save_baseline"]?.GetValue<bool>() ?? false;

        try
        {
            var resolvedPath = SolutionDiscovery.FindSolutionFile(solutionPath);
            using var loader = new WorkspaceLoader();
            var workspace = await loader.LoadSolutionAsync(resolvedPath, null, ct);

            var extractor = new CodeModelExtractor();
            var extractionResults = new List<ExtractionResult>();
            foreach (var (projectId, compilation) in workspace.Compilations)
            {
                var project = workspace.Solution.GetProject(projectId);
                var result = extractor.ExtractProject(compilation, project?.Name ?? "", project?.FilePath ?? "");
                extractionResults.Add(result);
            }

            var callGraphBuilder = new CallGraphBuilder();
            var edges = callGraphBuilder.BuildCallGraph(workspace);

            var metricsEngine = new MetricsEngine();
            var metrics = metricsEngine.ComputeMetrics(workspace);

            var normalizer = new IntentNormalizer();
            var normalized = normalizer.NormalizeAll(workspace);

            using var embeddingEngine = new HashEmbeddingEngine();
            var embeddingResults = normalized
                .Select(n => (n.MethodId, Vector: embeddingEngine.GenerateEmbedding(n.SemanticPayload), Model: "hash-v1"))
                .ToList();

            var output = Path.GetDirectoryName(_dbPath) ?? "./ai-code-graph";
            Directory.CreateDirectory(output);

            // Close existing storage if open
            if (_storage != null)
            {
                await _storage.DisposeAsync();
                _storage = null;
            }

            await using var storage = new StorageService(_dbPath);
            await storage.InitializeAsync(ct);
            await storage.SaveCodeModelAsync(extractionResults, ct);
            await storage.SaveCallGraphAsync(edges.Select(e => (e.CallerId, e.CalleeId)).ToList(), ct);
            await storage.SaveMetricsAsync(metrics.Select(m => (m.MethodId, m.CognitiveComplexity, m.LinesOfCode, m.MaxNestingDepth)).ToList(), ct);
            await storage.SaveNormalizedMethodsAsync(normalized.Select(n => (n.MethodId, n.StructuralSignature, n.SemanticPayload)).ToList(), ct);
            await storage.SaveEmbeddingsAsync(embeddingResults, ct);

            var structuralDetector = new StructuralCloneDetector();
            var semanticDetector = new SemanticCloneDetector();
            var hybridScorer = new HybridScorer();
            var structuralClones = structuralDetector.DetectClones(normalized);
            var embeddingPairs = embeddingResults.Select(e => (e.MethodId, e.Vector)).ToList();
            var semanticClones = semanticDetector.DetectClones(embeddingPairs);
            var clonePairs = hybridScorer.Merge(structuralClones, semanticClones);
            await storage.SaveClonePairsAsync(clonePairs, ct);

            var clusterer = new IntentClusterer();
            var clusters = clusterer.ClusterMethods(normalized, embeddingPairs);
            await storage.SaveClustersAsync(clusters, ct);

            if (saveBaseline)
            {
                var baselinePath = Path.Combine(output, "baseline.db");
                File.Copy(_dbPath, baselinePath, overwrite: true);
            }

            var totalMethods = extractionResults.Sum(r => r.Model.Namespaces.Sum(ns => CountMethodsInNamespace(ns)));

            _vectorIndex = null; // invalidate cached index

            return string.Join("\n", new[]
            {
                "Analysis complete:",
                $"  Solution: {resolvedPath}",
                $"  Projects: {extractionResults.Count}",
                $"  Methods: {totalMethods}",
                $"  Call edges: {edges.Count}",
                $"  Clone pairs: {clonePairs.Count}",
                $"  Clusters: {clusters.Count}",
                $"  Output: {Path.GetFullPath(_dbPath)}",
                saveBaseline ? $"  Baseline saved: {Path.GetFullPath(Path.Combine(output, "baseline.db"))}" : ""
            }.Where(l => l.Length > 0));
        }
        catch (FileNotFoundException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> ToolGetChurn(JsonNode? args, CancellationToken ct)
    {
        var since = args?["since"]?.GetValue<string>() ?? "6 months ago";
        var top = args?["top"]?.GetValue<int>() ?? 20;

        var analyzer = new ChurnAnalyzer();
        var results = await analyzer.AnalyzeAsync(_storage!, since, top, ct);

        if (results.Count == 0)
            return "No churn hotspots found.";

        var lines = new List<string> { $"Churn hotspots (since: {since}):", "" };
        foreach (var r in results)
        {
            lines.Add($"  {r.MethodName}");
            lines.Add($"    Changes: {r.Changes}, CC: {r.CognitiveComplexity}, Score: {r.ChurnScore:F0}");
            if (r.FilePath != null) lines.Add($"    File: {r.FilePath}");
        }
        lines.Add($"\nTotal: {results.Count} methods");
        return string.Join("\n", lines);
    }

    private static int CountMethodsInNamespace(NamespaceModel ns)
    {
        return ns.Types.Sum(t => t.Methods.Count + t.NestedTypes.Sum(CountMethodsInType))
            + ns.ChildNamespaces.Sum(CountMethodsInNamespace);
    }

    private static int CountMethodsInType(TypeModel type)
    {
        return type.Methods.Count + type.NestedTypes.Sum(CountMethodsInType);
    }

    private static JsonObject CreateToolDef(string name, string description, JsonObject inputSchema)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }

    private static JsonNode CreateResult(JsonNode? id, JsonNode result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result
        };
    }

    private static JsonNode CreateError(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    private static JsonNode CreateToolResult(JsonNode? id, string text, bool isError)
    {
        return CreateResult(id, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = isError
        });
    }
}
