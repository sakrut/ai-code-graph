using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using AiCodeGraph.Core;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Storage;

var rootCommand = new RootCommand("AI Code Graph - Semantic code analysis for .NET");

var solutionOption = new Option<string?>("--solution", "-s")
{
    Description = "Path to .sln file (auto-discovered if omitted)"
};

var outputOption = new Option<string>("--output", "-o")
{
    Description = "Output directory for the database",
    DefaultValueFactory = _ => "./ai-code-graph"
};

var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Enable verbose output"
};

var analyzeCommand = new Command("analyze", "Analyze a .NET solution and build the code graph")
{
    solutionOption,
    outputOption,
    verboseOption
};

analyzeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var solutionPath = parseResult.GetValue(solutionOption);
    var output = parseResult.GetValue(outputOption) ?? "./ai-code-graph";
    var verbose = parseResult.GetValue(verboseOption);
    var totalTimer = Stopwatch.StartNew();

    try
    {
        // 1. Discover/validate solution
        var resolvedPath = SolutionDiscovery.FindSolutionFile(solutionPath);
        Console.WriteLine($"Solution: {resolvedPath}");

        // 2. Load workspace
        Console.Write("Loading workspace...");
        var stageTimer = Stopwatch.StartNew();
        using var loader = new WorkspaceLoader();

        var progress = verbose
            ? new Progress<AiCodeGraph.Core.Models.WorkspaceLoadProgress>(p =>
            {
                if (p.ProjectName != null)
                    Console.WriteLine($"  [{p.CurrentProject}/{p.TotalProjects}] {p.Phase}: {p.ProjectName}");
            })
            : null;

        var workspace = await loader.LoadSolutionAsync(resolvedPath, progress, cancellationToken);
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        if (workspace.HasErrors)
        {
            foreach (var diag in workspace.Diagnostics)
                Console.Error.WriteLine($"  Warning: [{diag.Kind}] {diag.Message}");
        }

        // 3. Extract code model
        Console.Write("Extracting code model...");
        stageTimer.Restart();
        var extractor = new CodeModelExtractor();
        var extractionResults = new List<ExtractionResult>();

        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            var project = workspace.Solution.GetProject(projectId);
            var name = project?.Name ?? projectId.Id.ToString();
            var filePath = project?.FilePath ?? "";
            var result = extractor.ExtractProject(compilation, name, filePath);
            extractionResults.Add(result);

            if (verbose)
                Console.WriteLine($"  {name}: {CountMethods(result.Model)} methods");
        }
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // 4. Build call graph
        Console.Write("Building call graph...");
        stageTimer.Restart();
        var callGraphBuilder = new CallGraphBuilder();
        var edges = callGraphBuilder.BuildCallGraph(workspace);
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // 5. Compute metrics
        Console.Write("Computing metrics...");
        stageTimer.Restart();
        var metricsEngine = new MetricsEngine();
        var metrics = metricsEngine.ComputeMetrics(workspace);
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // 6. Normalize methods
        Console.Write("Normalizing methods...");
        stageTimer.Restart();
        var normalizer = new IntentNormalizer();
        var normalized = normalizer.NormalizeAll(workspace);
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // 7. Generate embeddings
        Console.Write("Generating embeddings...");
        stageTimer.Restart();
        using var embeddingEngine = new HashEmbeddingEngine();
        var embeddingResults = normalized
            .Select(n => (n.MethodId, Vector: embeddingEngine.GenerateEmbedding(n.SemanticPayload), Model: "hash-v1"))
            .ToList();
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // 8. Store results
        Console.Write("Storing results...");
        stageTimer.Restart();
        Directory.CreateDirectory(output);
        var dbPath = Path.Combine(output, "graph.db");

        await using var storage = new StorageService(dbPath);
        await storage.InitializeAsync(cancellationToken);
        await storage.SaveCodeModelAsync(extractionResults, cancellationToken);
        await storage.SaveCallGraphAsync(
            edges.Select(e => (e.CallerId, e.CalleeId)).ToList(),
            cancellationToken);
        await storage.SaveMetricsAsync(
            metrics.Select(m => (m.MethodId, m.CognitiveComplexity, m.LinesOfCode, m.MaxNestingDepth)).ToList(),
            cancellationToken);
        await storage.SaveNormalizedMethodsAsync(
            normalized.Select(n => (n.MethodId, n.StructuralSignature, n.SemanticPayload)).ToList(),
            cancellationToken);
        await storage.SaveEmbeddingsAsync(embeddingResults, cancellationToken);
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // 9. Detect duplicates and cluster methods
        Console.Write("Detecting duplicates...");
        stageTimer.Restart();
        var structuralDetector = new StructuralCloneDetector();
        var semanticDetector = new SemanticCloneDetector();
        var hybridScorer = new HybridScorer();

        var structuralClones = structuralDetector.DetectClones(normalized);
        var embeddingPairs = embeddingResults.Select(e => (e.MethodId, e.Vector)).ToList();
        var semanticClones = semanticDetector.DetectClones(embeddingPairs);
        var clonePairs = hybridScorer.Merge(structuralClones, semanticClones);
        await storage.SaveClonePairsAsync(clonePairs, cancellationToken);

        var clusterer = new IntentClusterer();
        var clusters = clusterer.ClusterMethods(normalized, embeddingPairs);
        await storage.SaveClustersAsync(clusters, cancellationToken);
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // Summary
        totalTimer.Stop();
        var totalProjects = extractionResults.Count;
        var totalTypes = extractionResults.Sum(r => CountTypes(r.Model));
        var totalMethods = extractionResults.Sum(r => CountMethods(r.Model));
        var avgComplexity = metrics.Count > 0 ? metrics.Average(m => m.CognitiveComplexity) : 0;

        Console.WriteLine();
        Console.WriteLine("Analysis complete:");
        Console.WriteLine($"  Projects:       {totalProjects:N0}");
        Console.WriteLine($"  Types:          {totalTypes:N0}");
        Console.WriteLine($"  Methods:        {totalMethods:N0}");
        Console.WriteLine($"  Call edges:     {edges.Count:N0}");
        Console.WriteLine($"  Clone pairs:    {clonePairs.Count:N0}");
        Console.WriteLine($"  Clusters:       {clusters.Count:N0}");
        Console.WriteLine($"  Avg complexity: {avgComplexity:F1}");
        Console.WriteLine($"  Duration:       {totalTimer.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  Output:         {Path.GetFullPath(dbPath)}");
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (verbose) Console.Error.WriteLine(ex.StackTrace);
        Environment.ExitCode = 1;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (verbose) Console.Error.WriteLine(ex.StackTrace);
        Environment.ExitCode = 1;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Analysis cancelled.");
        Environment.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Unexpected error: {ex.Message}");
        if (verbose) Console.Error.WriteLine(ex.ToString());
        Environment.ExitCode = 2;
    }
});

// --- callgraph command ---
var methodArgument = new Argument<string>("method") { Description = "Method name or pattern to search for" };
var depthOption = new Option<int>("--depth", "-d") { Description = "Traversal depth", DefaultValueFactory = _ => 2 };
var directionOption = new Option<string>("--direction") { Description = "callers|callees|both", DefaultValueFactory = _ => "both" };
var cgFormatOption = new Option<string>("--format", "-f") { Description = "tree|json", DefaultValueFactory = _ => "tree" };
var cgDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var callgraphCommand = new Command("callgraph", "Explore method call graph")
{
    methodArgument, depthOption, directionOption, cgFormatOption, cgDbOption
};

callgraphCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var method = parseResult.GetValue(methodArgument)!;
    var depth = parseResult.GetValue(depthOption);
    var direction = parseResult.GetValue(directionOption) ?? "both";
    var format = parseResult.GetValue(cgFormatOption) ?? "tree";
    var dbPath = parseResult.GetValue(cgDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var matches = await storage.SearchMethodsAsync(method, cancellationToken);
    if (matches.Count == 0)
    {
        Console.Error.WriteLine($"No methods found matching '{method}'.");
        Environment.ExitCode = 1;
        return;
    }

    if (matches.Count > 1 && !matches.Any(m => m.FullName == method))
    {
        Console.WriteLine($"Multiple methods match '{method}':");
        foreach (var m in matches.Take(10))
            Console.WriteLine($"  {m.FullName}");
        if (matches.Count > 10)
            Console.WriteLine($"  ... and {matches.Count - 10} more");
        Console.WriteLine("Please use a more specific name.");
        return;
    }

    var rootId = matches.First(m => m.FullName == method || matches.Count == 1).Id;
    var rootInfo = await storage.GetMethodInfoAsync(rootId, cancellationToken);

    // BFS traversal
    var visited = new HashSet<string>();
    var nodes = new List<(string Id, string FullName, int Depth, string Direction)>();
    var edges = new List<(string From, string To)>();
    var queue = new Queue<(string Id, int Depth)>();

    queue.Enqueue((rootId, 0));
    visited.Add(rootId);

    while (queue.Count > 0)
    {
        var (currentId, currentDepth) = queue.Dequeue();
        var info = await storage.GetMethodInfoAsync(currentId, cancellationToken);
        if (info == null) continue;
        nodes.Add((currentId, info.Value.FullName, currentDepth, currentDepth == 0 ? "root" : ""));

        if (currentDepth >= depth) continue;

        if (direction is "callees" or "both")
        {
            foreach (var calleeId in await storage.GetCalleesAsync(currentId, cancellationToken))
            {
                edges.Add((currentId, calleeId));
                if (visited.Add(calleeId))
                    queue.Enqueue((calleeId, currentDepth + 1));
            }
        }
        if (direction is "callers" or "both")
        {
            foreach (var callerId in await storage.GetCallersAsync(currentId, cancellationToken))
            {
                edges.Add((callerId, currentId));
                if (visited.Add(callerId))
                    queue.Enqueue((callerId, currentDepth + 1));
            }
        }
    }

    if (format == "json")
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            root = new { id = rootId, name = rootInfo?.FullName },
            nodes = nodes.OrderBy(n => n.FullName).Select(n => new { n.Id, name = n.FullName, n.Depth }),
            edges = edges.OrderBy(e => e.From).ThenBy(e => e.To).Select(e => new { from = e.From, to = e.To }),
            metadata = new { depth, direction }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else
    {
        Console.WriteLine($"{rootInfo?.FullName ?? rootId}");
        PrintCallTree(rootId, edges, nodes, 1, depth, new HashSet<string> { rootId });
    }
});

// --- hotspots command ---
var topOption = new Option<int>("--top", "-t") { Description = "Number of results", DefaultValueFactory = _ => 20 };
var thresholdOption = new Option<int?>("--threshold") { Description = "Minimum complexity score" };
var hsFormatOption = new Option<string>("--format", "-f") { Description = "table|json", DefaultValueFactory = _ => "table" };
var hsDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var hotspotsCommand = new Command("hotspots", "Show complexity hotspots")
{
    topOption, thresholdOption, hsFormatOption, hsDbOption
};

hotspotsCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var top = parseResult.GetValue(topOption);
    var threshold = parseResult.GetValue(thresholdOption);
    var format = parseResult.GetValue(hsFormatOption) ?? "table";
    var dbPath = parseResult.GetValue(hsDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var hotspots = await storage.GetHotspotsWithThresholdAsync(top, threshold, cancellationToken);

    if (hotspots.Count == 0)
    {
        Console.WriteLine("No hotspots found.");
        return;
    }

    if (format == "json")
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            hotspots = hotspots.Select(h => new
            {
                method = h.FullName,
                complexity = h.Complexity,
                loc = h.Loc,
                maxNesting = h.Nesting,
                location = h.FilePath != null ? $"{h.FilePath}:{h.StartLine}" : null
            }),
            metadata = new { total = hotspots.Count, threshold, top }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else
    {
        var nameWidth = Math.Min(60, hotspots.Max(h => h.FullName.Length));
        Console.WriteLine($"{"Method".PadRight(nameWidth)}  {"CC",4}  {"LOC",4}  {"Nest",4}  Location");
        Console.WriteLine(new string('-', nameWidth + 30));
        foreach (var h in hotspots)
        {
            var name = h.FullName.Length > nameWidth ? h.FullName[..(nameWidth - 3)] + "..." : h.FullName;
            var location = h.FilePath != null ? $"{Path.GetFileName(h.FilePath)}:{h.StartLine}" : "";
            Console.WriteLine($"{name.PadRight(nameWidth)}  {h.Complexity,4}  {h.Loc,4}  {h.Nesting,4}  {location}");
        }
    }
});

// --- tree command ---
var nsFilterOption = new Option<string?>("--namespace", "-n") { Description = "Filter by namespace prefix" };
var typeFilterOption = new Option<string?>("--type") { Description = "Filter by type name" };
var treeFormatOption = new Option<string>("--format", "-f") { Description = "tree|json", DefaultValueFactory = _ => "tree" };
var treeDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var treeCommand = new Command("tree", "Display code structure tree")
{
    nsFilterOption, typeFilterOption, treeFormatOption, treeDbOption
};

treeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var nsFilter = parseResult.GetValue(nsFilterOption);
    var typeFilter = parseResult.GetValue(typeFilterOption);
    var format = parseResult.GetValue(treeFormatOption) ?? "tree";
    var dbPath = parseResult.GetValue(treeDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var rows = await storage.GetTreeAsync(nsFilter, typeFilter, cancellationToken);

    if (rows.Count == 0)
    {
        Console.WriteLine("No results found.");
        return;
    }

    if (format == "json")
    {
        var hierarchy = rows
            .GroupBy(r => r.ProjectName)
            .Select(pg => new
            {
                name = pg.Key,
                namespaces = pg.GroupBy(r => r.NamespaceName).OrderBy(g => g.Key).Select(ng => new
                {
                    name = ng.Key,
                    types = ng.GroupBy(r => (r.TypeName, r.TypeKind)).OrderBy(g => g.Key.TypeName).Select(tg => new
                    {
                        name = tg.Key.TypeName,
                        kind = tg.Key.TypeKind.ToLower(),
                        methods = tg.OrderBy(r => r.MethodName).Select(r => new { name = r.MethodName, returnType = r.ReturnType })
                    })
                })
            });

        var json = System.Text.Json.JsonSerializer.Serialize(new { projects = hierarchy },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else
    {
        var lastProject = "";
        var lastNs = "";
        var lastType = "";

        foreach (var row in rows)
        {
            if (row.ProjectName != lastProject)
            {
                Console.WriteLine(row.ProjectName);
                lastProject = row.ProjectName;
                lastNs = "";
                lastType = "";
            }
            if (row.NamespaceName != lastNs)
            {
                Console.WriteLine($"  {row.NamespaceName}");
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
                Console.WriteLine($"    {kindTag} {row.TypeName}");
                lastType = row.TypeName;
            }
            Console.WriteLine($"        {row.ReturnType} {row.MethodName}()");
        }
    }
});

// --- similar command ---
var simMethodArg = new Argument<string>("method") { Description = "Method name to find similar methods for" };
var simTopOption = new Option<int>("--top", "-t") { Description = "Number of results", DefaultValueFactory = _ => 10 };
var simFormatOption = new Option<string>("--format", "-f") { Description = "table|json", DefaultValueFactory = _ => "table" };
var simDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var similarCommand = new Command("similar", "Find methods with similar intent")
{
    simMethodArg, simTopOption, simFormatOption, simDbOption
};

similarCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var method = parseResult.GetValue(simMethodArg)!;
    var top = parseResult.GetValue(simTopOption);
    var format = parseResult.GetValue(simFormatOption) ?? "table";
    var dbPath = parseResult.GetValue(simDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var matches = await storage.SearchMethodsAsync(method, cancellationToken);
    if (matches.Count == 0)
    {
        Console.Error.WriteLine($"No methods found matching '{method}'.");
        Environment.ExitCode = 1;
        return;
    }

    var targetId = matches.First().Id;
    var allEmbeddings = await storage.GetEmbeddingsAsync(cancellationToken);

    if (allEmbeddings.Count == 0)
    {
        Console.Error.WriteLine("No embeddings found. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    var targetEmbedding = allEmbeddings.FirstOrDefault(e => e.MethodId == targetId);
    if (targetEmbedding.Vector == null)
    {
        Console.Error.WriteLine($"No embedding found for method '{method}'.");
        Environment.ExitCode = 1;
        return;
    }

    var index = new VectorIndex();
    index.BuildIndex(allEmbeddings.Where(e => e.MethodId != targetId).ToList());
    var results = index.Search(targetEmbedding.Vector, top);

    if (format == "json")
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            query = matches.First().FullName,
            results = results.Select(r => new { id = r.Id, score = Math.Round(r.Score, 4) }),
            metadata = new { top }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else
    {
        Console.WriteLine($"Methods similar to: {matches.First().FullName}");
        Console.WriteLine(new string('-', 60));
        foreach (var (id, score) in results)
        {
            var info = await storage.GetMethodInfoAsync(id, cancellationToken);
            var name = info?.FullName ?? id;
            Console.WriteLine($"  {score:F4}  {name}");
        }
    }
});

// --- duplicates command ---
var dupTopOption = new Option<int>("--top", "-t") { Description = "Number of results", DefaultValueFactory = _ => 20 };
var dupThresholdOption = new Option<float>("--threshold") { Description = "Minimum hybrid score", DefaultValueFactory = _ => 0.5f };
var dupTypeOption = new Option<string?>("--type") { Description = "Filter by clone type: Type1|Type2|Semantic" };
var dupFormatOption = new Option<string>("--format", "-f") { Description = "table|json", DefaultValueFactory = _ => "table" };
var dupDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var duplicatesCommand = new Command("duplicates", "Show detected code clones")
{
    dupTopOption, dupThresholdOption, dupTypeOption, dupFormatOption, dupDbOption
};

duplicatesCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var top = parseResult.GetValue(dupTopOption);
    var threshold = parseResult.GetValue(dupThresholdOption);
    var typeStr = parseResult.GetValue(dupTypeOption);
    var format = parseResult.GetValue(dupFormatOption) ?? "table";
    var dbPath = parseResult.GetValue(dupDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    CloneType? typeFilter = typeStr != null ? Enum.Parse<CloneType>(typeStr, ignoreCase: true) : null;

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var pairs = await storage.GetClonePairsAsync(threshold, typeFilter, cancellationToken);
    pairs = pairs.Take(top).ToList();

    if (pairs.Count == 0)
    {
        Console.WriteLine("No clone pairs found.");
        return;
    }

    if (format == "json")
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            clones = pairs.Select(p => new
            {
                methodA = p.MethodIdA,
                methodB = p.MethodIdB,
                structural = Math.Round(p.StructuralSimilarity, 4),
                semantic = Math.Round(p.SemanticSimilarity, 4),
                hybrid = Math.Round(p.HybridScore, 4),
                type = p.Type.ToString()
            }),
            metadata = new { total = pairs.Count, threshold, typeFilter = typeStr }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else
    {
        Console.WriteLine($"{"Type",-10} {"Hybrid",6} {"Struct",6} {"Seman",6}  Method A / Method B");
        Console.WriteLine(new string('-', 80));
        foreach (var p in pairs)
        {
            var infoA = await storage.GetMethodInfoAsync(p.MethodIdA, cancellationToken);
            var infoB = await storage.GetMethodInfoAsync(p.MethodIdB, cancellationToken);
            var nameA = infoA?.FullName ?? p.MethodIdA;
            var nameB = infoB?.FullName ?? p.MethodIdB;
            Console.WriteLine($"{p.Type,-10} {p.HybridScore,6:F3} {p.StructuralSimilarity,6:F3} {p.SemanticSimilarity,6:F3}  {nameA}");
            Console.WriteLine($"{"",10} {"",6} {"",6} {"",6}  {nameB}");
        }
    }
});

// --- clusters command ---
var clFormatOption = new Option<string>("--format", "-f") { Description = "table|json", DefaultValueFactory = _ => "table" };
var clDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var clustersCommand = new Command("clusters", "Show intent clusters")
{
    clFormatOption, clDbOption
};

clustersCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var format = parseResult.GetValue(clFormatOption) ?? "table";
    var dbPath = parseResult.GetValue(clDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var clusters = await storage.GetClustersAsync(cancellationToken);

    if (clusters.Count == 0)
    {
        Console.WriteLine("No clusters found.");
        return;
    }

    if (format == "json")
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            clusters = clusters.Select(c => new
            {
                id = c.Id,
                label = c.Label,
                description = c.Description,
                cohesion = Math.Round(c.Cohesion, 4),
                members = c.MethodIds
            })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else
    {
        foreach (var cluster in clusters)
        {
            Console.WriteLine($"[{cluster.Id}] {cluster.Label} (cohesion: {cluster.Cohesion:F2}, members: {cluster.MethodIds.Count})");
            foreach (var methodId in cluster.MethodIds.Take(5))
            {
                var info = await storage.GetMethodInfoAsync(methodId, cancellationToken);
                Console.WriteLine($"    {info?.FullName ?? methodId}");
            }
            if (cluster.MethodIds.Count > 5)
                Console.WriteLine($"    ... and {cluster.MethodIds.Count - 5} more");
            Console.WriteLine();
        }
    }
});

rootCommand.Add(analyzeCommand);
rootCommand.Add(callgraphCommand);
rootCommand.Add(hotspotsCommand);
rootCommand.Add(treeCommand);
rootCommand.Add(similarCommand);
rootCommand.Add(duplicatesCommand);
rootCommand.Add(clustersCommand);

var parseResult = CommandLineParser.Parse(rootCommand, args);
return await parseResult.InvokeAsync();

static int CountTypes(ProjectModel project)
{
    return project.Namespaces.Sum(ns => CountTypesInNamespace(ns));
}

static int CountTypesInNamespace(NamespaceModel ns)
{
    return ns.Types.Count
        + ns.Types.Sum(t => CountNestedTypes(t))
        + ns.ChildNamespaces.Sum(c => CountTypesInNamespace(c));
}

static int CountNestedTypes(TypeModel type)
{
    return type.NestedTypes.Count + type.NestedTypes.Sum(CountNestedTypes);
}

static int CountMethods(ProjectModel project)
{
    return project.Namespaces.Sum(ns => CountMethodsInNamespace(ns));
}

static int CountMethodsInNamespace(NamespaceModel ns)
{
    return ns.Types.Sum(t => t.Methods.Count + t.NestedTypes.Sum(CountMethodsInType))
        + ns.ChildNamespaces.Sum(c => CountMethodsInNamespace(c));
}

static int CountMethodsInType(TypeModel type)
{
    return type.Methods.Count + type.NestedTypes.Sum(CountMethodsInType);
}

static void PrintCallTree(string nodeId, List<(string From, string To)> edges, List<(string Id, string FullName, int Depth, string Direction)> nodes, int currentDepth, int maxDepth, HashSet<string> printed)
{
    if (currentDepth > maxDepth) return;
    var indent = new string(' ', currentDepth * 2);

    // callees
    foreach (var edge in edges.Where(e => e.From == nodeId))
    {
        var node = nodes.FirstOrDefault(n => n.Id == edge.To);
        if (node == default) continue;
        var marker = printed.Add(edge.To) ? "" : " (*)";
        Console.WriteLine($"{indent}\u2192 {node.FullName}{marker}");
        if (marker == "")
            PrintCallTree(edge.To, edges, nodes, currentDepth + 1, maxDepth, printed);
    }

    // callers
    foreach (var edge in edges.Where(e => e.To == nodeId))
    {
        var node = nodes.FirstOrDefault(n => n.Id == edge.From);
        if (node == default) continue;
        var marker = printed.Add(edge.From) ? "" : " (*)";
        Console.WriteLine($"{indent}\u2190 {node.FullName}{marker}");
        if (marker == "")
            PrintCallTree(edge.From, edges, nodes, currentDepth + 1, maxDepth, printed);
    }
}
