using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using AiCodeGraph.Core;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Drift;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Mcp;

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

var saveBaselineOption = new Option<bool>("--save-baseline")
{
    Description = "Save the analysis result as baseline for drift detection"
};

var analyzeCommand = new Command("analyze", "Analyze a .NET solution and build the code graph")
{
    solutionOption,
    outputOption,
    verboseOption,
    saveBaselineOption
};

analyzeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var solutionPath = parseResult.GetValue(solutionOption);
    var output = parseResult.GetValue(outputOption) ?? "./ai-code-graph";
    var verbose = parseResult.GetValue(verboseOption);
    var saveBaseline = parseResult.GetValue(saveBaselineOption);
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

        // 10. Save baseline if requested
        if (saveBaseline)
        {
            var baselinePath = Path.Combine(output, "baseline.db");
            File.Copy(dbPath, baselinePath, overwrite: true);
            Console.WriteLine($"Baseline saved: {Path.GetFullPath(baselinePath)}");
        }

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
var dupConceptOption = new Option<string?>("--concept") { Description = "Filter by intent cluster label" };
var dupFormatOption = new Option<string>("--format", "-f") { Description = "table|json", DefaultValueFactory = _ => "table" };
var dupDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var duplicatesCommand = new Command("duplicates", "Show detected code clones")
{
    dupTopOption, dupThresholdOption, dupTypeOption, dupConceptOption, dupFormatOption, dupDbOption
};

duplicatesCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var top = parseResult.GetValue(dupTopOption);
    var threshold = parseResult.GetValue(dupThresholdOption);
    var typeStr = parseResult.GetValue(dupTypeOption);
    var concept = parseResult.GetValue(dupConceptOption);
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

    var pairs = await storage.GetClonePairsAsync(threshold, typeFilter, concept, cancellationToken);
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

// --- search command ---
var searchQueryArg = new Argument<string>("query") { Description = "Natural language search query" };
var searchTopOption = new Option<int>("--top", "-t") { Description = "Number of results", DefaultValueFactory = _ => 10 };
var searchThresholdOption = new Option<float>("--threshold") { Description = "Minimum similarity score", DefaultValueFactory = _ => 0.5f };
var searchFormatOption = new Option<string>("--format", "-f") { Description = "table|json", DefaultValueFactory = _ => "table" };
var searchDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var searchCommand = new Command("search", "Search code by natural language intent")
{
    searchQueryArg, searchTopOption, searchThresholdOption, searchFormatOption, searchDbOption
};

searchCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var query = parseResult.GetValue(searchQueryArg)!;
    var top = parseResult.GetValue(searchTopOption);
    var threshold = parseResult.GetValue(searchThresholdOption);
    var format = parseResult.GetValue(searchFormatOption) ?? "table";
    var dbPath = parseResult.GetValue(searchDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var allEmbeddings = await storage.GetEmbeddingsAsync(cancellationToken);
    if (allEmbeddings.Count == 0)
    {
        Console.Error.WriteLine("No embeddings found. Run 'analyze' first to build embeddings.");
        Environment.ExitCode = 1;
        return;
    }

    // Generate embedding for the query
    using var embeddingEngine = new HashEmbeddingEngine();
    var queryVector = embeddingEngine.GenerateEmbedding(query);

    // Build index and search
    var index = new VectorIndex();
    index.BuildIndex(allEmbeddings);
    var searchResults = index.Search(queryVector, top)
        .Where(r => r.Score >= threshold)
        .ToList();

    if (searchResults.Count == 0)
    {
        Console.WriteLine($"No results found above threshold {threshold:F2}.");
        return;
    }

    if (format == "json")
    {
        var enriched = new List<object>();
        foreach (var (id, score) in searchResults)
        {
            var info = await storage.GetMethodInfoAsync(id, cancellationToken);
            enriched.Add(new
            {
                methodId = id,
                fullName = info?.FullName ?? id,
                score = Math.Round(score, 4),
                filePath = info?.FilePath,
                line = info?.StartLine ?? 0
            });
        }

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            query,
            results = enriched,
            metadata = new { top, threshold }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else
    {
        Console.WriteLine($"Search: \"{query}\"");
        Console.WriteLine($"{"Score",6}  Method");
        Console.WriteLine(new string('-', 70));
        foreach (var (id, score) in searchResults)
        {
            var info = await storage.GetMethodInfoAsync(id, cancellationToken);
            var name = info?.FullName ?? id;
            var location = info?.FilePath != null ? $"  {Path.GetFileName(info.Value.FilePath)}:{info.Value.StartLine}" : "";
            Console.WriteLine($"{score,6:F4}  {name}{location}");
        }
    }
});

// --- export command ---
var exportConceptOption = new Option<string?>("--concept") { Description = "Filter by concept/cluster label" };
var exportFormatOption = new Option<string>("--format", "-f") { Description = "json|csv", DefaultValueFactory = _ => "json" };
var exportDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var exportCommand = new Command("export", "Export code graph data")
{
    exportConceptOption, exportFormatOption, exportDbOption
};

exportCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var concept = parseResult.GetValue(exportConceptOption);
    var format = parseResult.GetValue(exportFormatOption) ?? "json";
    var dbPath = parseResult.GetValue(exportDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    await using var storage = new StorageService(dbPath);
    await storage.OpenAsync(cancellationToken);

    var methods = await storage.GetMethodsForExportAsync(concept, cancellationToken);
    if (methods.Count == 0)
    {
        Console.WriteLine("No methods found.");
        return;
    }

    var methodIds = methods.Select(m => m.Id).ToHashSet();
    var relationships = await storage.GetCallGraphForMethodsAsync(methodIds, cancellationToken);

    if (format == "csv")
    {
        Console.WriteLine("Id,FullName,ReturnType,FilePath,Line,Complexity,LOC,Nesting,ClusterLabel");
        foreach (var m in methods)
        {
            var filePath = CsvEscape(m.FilePath ?? "");
            var label = CsvEscape(m.ClusterLabel ?? "");
            Console.WriteLine($"{CsvEscape(m.Id)},{CsvEscape(m.FullName)},{CsvEscape(m.ReturnType)},{filePath},{m.StartLine},{m.Complexity},{m.Loc},{m.Nesting},{label}");
        }
    }
    else
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
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
            relationships = relationships.OrderBy(r => r.CallerId).ThenBy(r => r.CalleeId).Select(r => new
            {
                caller = r.CallerId,
                callee = r.CalleeId
            }),
            metadata = new { methodCount = methods.Count, relationshipCount = relationships.Count, conceptFilter = concept }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
});

// --- drift command ---
var driftVsOption = new Option<string>("--vs") { Description = "Baseline path or 'baseline' keyword", DefaultValueFactory = _ => "baseline" };
var driftFormatOption = new Option<string>("--format", "-f") { Description = "summary|detail|json", DefaultValueFactory = _ => "summary" };
var driftComplexityPctOption = new Option<double>("--complexity-pct") { Description = "Complexity percentage threshold", DefaultValueFactory = _ => 0.25 };
var driftComplexityAbsOption = new Option<int>("--complexity-abs") { Description = "Complexity absolute threshold", DefaultValueFactory = _ => 15 };
var driftDbOption = new Option<string>("--db") { Description = "Path to current graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };

var driftCommand = new Command("drift", "Detect architectural drift")
{
    driftVsOption, driftFormatOption, driftComplexityPctOption, driftComplexityAbsOption, driftDbOption
};

driftCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var vs = parseResult.GetValue(driftVsOption) ?? "baseline";
    var format = parseResult.GetValue(driftFormatOption) ?? "summary";
    var complexityPct = parseResult.GetValue(driftComplexityPctOption);
    var complexityAbs = parseResult.GetValue(driftComplexityAbsOption);
    var dbPath = parseResult.GetValue(driftDbOption) ?? "./ai-code-graph/graph.db";

    if (!File.Exists(dbPath))
    {
        Console.Error.WriteLine($"Error: Database not found at {dbPath}. Run 'analyze' first.");
        Environment.ExitCode = 1;
        return;
    }

    var baselinePath = vs == "baseline"
        ? Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "baseline.db")
        : vs;

    if (!File.Exists(baselinePath))
    {
        Console.Error.WriteLine($"Error: Baseline not found at {baselinePath}. Run 'analyze --save-baseline' first.");
        Environment.ExitCode = 1;
        return;
    }

    var options = new DriftDetectorOptions
    {
        ComplexityPercentageThreshold = complexityPct,
        ComplexityAbsoluteThreshold = complexityAbs
    };

    var detector = new DriftDetector(options);
    var report = await detector.CompareAsync(dbPath, baselinePath, cancellationToken);

    var hasDrift = report.NewMethods.Count > 0 || report.RemovedMethods.Count > 0
        || report.Regressions.Count > 0 || report.NewDuplicates.Count > 0
        || report.IntentScattering.Count > 0;

    if (format == "json")
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            newMethods = report.NewMethods.Select(m => new { m.MethodId, m.FullName, m.Namespace, m.FilePath }),
            removedMethods = report.RemovedMethods.Select(m => new { m.MethodId, m.FullName, m.Namespace, m.FilePath }),
            regressions = report.Regressions.Select(r => new { r.MethodId, r.FullName, r.BaselineComplexity, r.CurrentComplexity, r.PercentageIncrease, r.CrossedAbsoluteThreshold }),
            newDuplicates = report.NewDuplicates.Select(d => new { d.MethodIdA, d.MethodIdB, d.HybridScore, type = d.Type.ToString() }),
            intentScattering = report.IntentScattering.Select(s => new { s.ClusterLabel, s.BaselineNamespaces, s.NewNamespaces, s.NewMemberMethods, s.TotalMemberCount }),
            hasDrift
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
    }
    else if (format == "detail")
    {
        if (report.NewMethods.Count > 0)
        {
            Console.WriteLine($"New Methods ({report.NewMethods.Count}):");
            foreach (var m in report.NewMethods)
                Console.WriteLine($"  + {m.FullName}  [{m.Namespace}]");
            Console.WriteLine();
        }

        if (report.RemovedMethods.Count > 0)
        {
            Console.WriteLine($"Removed Methods ({report.RemovedMethods.Count}):");
            foreach (var m in report.RemovedMethods)
                Console.WriteLine($"  - {m.FullName}  [{m.Namespace}]");
            Console.WriteLine();
        }

        if (report.Regressions.Count > 0)
        {
            Console.WriteLine($"Complexity Regressions ({report.Regressions.Count}):");
            foreach (var r in report.Regressions)
            {
                var pct = (r.PercentageIncrease * 100).ToString("F0");
                var threshold = r.CrossedAbsoluteThreshold ? " [CROSSED THRESHOLD]" : "";
                Console.WriteLine($"  {r.FullName}: {r.BaselineComplexity} -> {r.CurrentComplexity} (+{pct}%){threshold}");
            }
            Console.WriteLine();
        }

        if (report.NewDuplicates.Count > 0)
        {
            Console.WriteLine($"New Duplicates ({report.NewDuplicates.Count}):");
            foreach (var d in report.NewDuplicates)
                Console.WriteLine($"  {d.MethodIdA} <-> {d.MethodIdB} (score: {d.HybridScore:F3})");
            Console.WriteLine();
        }

        if (report.IntentScattering.Count > 0)
        {
            Console.WriteLine($"Intent Scattering ({report.IntentScattering.Count}):");
            foreach (var s in report.IntentScattering)
            {
                Console.WriteLine($"  Cluster '{s.ClusterLabel}' spread to: {string.Join(", ", s.NewNamespaces)}");
                Console.WriteLine($"    New members: {string.Join(", ", s.NewMemberMethods.Take(5))}");
            }
            Console.WriteLine();
        }

        if (!hasDrift)
            Console.WriteLine("No drift detected.");
    }
    else // summary
    {
        if (!hasDrift)
        {
            Console.WriteLine("No drift detected.");
        }
        else
        {
            var parts = new List<string>();
            if (report.NewMethods.Count > 0)
                parts.Add($"{report.NewMethods.Count} new method(s)");
            if (report.RemovedMethods.Count > 0)
                parts.Add($"{report.RemovedMethods.Count} removed method(s)");
            if (report.Regressions.Count > 0)
                parts.Add($"{report.Regressions.Count} complexity regression(s)");
            if (report.NewDuplicates.Count > 0)
                parts.Add($"{report.NewDuplicates.Count} new duplicate(s)");
            if (report.IntentScattering.Count > 0)
                parts.Add($"{report.IntentScattering.Count} scattering alert(s)");

            Console.WriteLine($"Drift detected: {string.Join(", ", parts)}");
        }
    }

    Environment.ExitCode = hasDrift ? 1 : 0;
});

// Context command - compact combined method summary for Claude Code integration
var ctxMethodArg = new Argument<string>("method") { Description = "Method name or pattern" };
var ctxDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };
var contextCommand = new Command("context", "Get compact method context (complexity, callers, callees, cluster, duplicates)")
{
    ctxMethodArg, ctxDbOption
};

contextCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var method = parseResult.GetValue(ctxMethodArg)!;
    var dbPath = parseResult.GetValue(ctxDbOption) ?? "./ai-code-graph/graph.db";

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
        Console.Error.WriteLine($"Method not found: '{method}'");
        Environment.ExitCode = 1;
        return;
    }

    // If multiple matches and none exact, list them
    if (matches.Count > 1 && !matches.Any(m => m.FullName.Contains(method, StringComparison.OrdinalIgnoreCase) && m.FullName.Split('.').Last().Split('(').First() == method.Split('.').Last().Split('(').First()))
    {
        Console.WriteLine($"Multiple matches for '{method}':");
        foreach (var m in matches.Take(5))
            Console.WriteLine($"  {m.FullName}");
        if (matches.Count > 5)
            Console.WriteLine($"  ... and {matches.Count - 5} more");
        return;
    }

    var targetId = matches.Count == 1
        ? matches[0].Id
        : matches.First(m => m.FullName.Contains(method, StringComparison.OrdinalIgnoreCase)).Id;

    var info = await storage.GetMethodInfoAsync(targetId, cancellationToken);
    if (info == null) return;

    // Method identity
    Console.WriteLine($"Method: {info.Value.FullName}");
    if (info.Value.FilePath != null)
        Console.WriteLine($"File: {info.Value.FilePath}:{info.Value.StartLine}");

    // Metrics
    var metrics = await storage.GetMethodMetricsAsync(targetId, cancellationToken);
    if (metrics != null)
        Console.WriteLine($"Complexity: CC={metrics.Value.CognitiveComplexity} LOC={metrics.Value.LinesOfCode} Nesting={metrics.Value.NestingDepth}");

    // Callers
    var callers = await storage.GetCallersAsync(targetId, cancellationToken);
    if (callers.Count > 0)
    {
        var callerNames = new List<string>();
        foreach (var cid in callers.Take(5))
        {
            var ci = await storage.GetMethodInfoAsync(cid, cancellationToken);
            callerNames.Add(ci?.Name ?? cid);
        }
        var suffix = callers.Count > 5 ? $" (+{callers.Count - 5} more)" : "";
        Console.WriteLine($"Callers ({callers.Count}): {string.Join(", ", callerNames)}{suffix}");
    }

    // Callees
    var callees = await storage.GetCalleesAsync(targetId, cancellationToken);
    if (callees.Count > 0)
    {
        var calleeNames = new List<string>();
        foreach (var cid in callees.Take(5))
        {
            var ci = await storage.GetMethodInfoAsync(cid, cancellationToken);
            calleeNames.Add(ci?.Name ?? cid);
        }
        var suffix = callees.Count > 5 ? $" (+{callees.Count - 5} more)" : "";
        Console.WriteLine($"Callees ({callees.Count}): {string.Join(", ", calleeNames)}{suffix}");
    }

    // Cluster
    var cluster = await storage.GetMethodClusterAsync(targetId, cancellationToken);
    if (cluster != null)
        Console.WriteLine($"Cluster: \"{cluster.Value.Label}\" ({cluster.Value.MemberCount} members, cohesion: {cluster.Value.Cohesion:F2})");

    // Duplicates
    var dupes = await storage.GetMethodDuplicatesAsync(targetId, cancellationToken);
    if (dupes.Count > 0)
    {
        var dupeStrs = dupes.Take(3).Select(d =>
        {
            var name = d.OtherFullName;
            // Extract Type.Method from full qualified name
            var parenIdx = name.IndexOf('(');
            var nameWithoutParams = parenIdx >= 0 ? name[..parenIdx] : name;
            var parts = nameWithoutParams.Split('.');
            var shortName = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
            return $"{shortName} ({d.HybridScore:F2})";
        });
        var suffix = dupes.Count > 3 ? $" (+{dupes.Count - 3} more)" : "";
        Console.WriteLine($"Duplicates ({dupes.Count}): {string.Join(", ", dupeStrs)}{suffix}");
    }
});

rootCommand.Add(analyzeCommand);
rootCommand.Add(callgraphCommand);
rootCommand.Add(hotspotsCommand);
rootCommand.Add(treeCommand);
rootCommand.Add(similarCommand);
rootCommand.Add(duplicatesCommand);
rootCommand.Add(clustersCommand);
rootCommand.Add(searchCommand);
rootCommand.Add(exportCommand);
rootCommand.Add(driftCommand);
rootCommand.Add(contextCommand);

// MCP server command
var mcpDbOption = new Option<string>("--db") { Description = "Path to graph.db", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };
var mcpCommand = new Command("mcp", "Run as MCP server (JSON-RPC over stdin/stdout)")
{
    mcpDbOption
};
mcpCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var dbPath = parseResult.GetValue(mcpDbOption) ?? "./ai-code-graph/graph.db";
    var server = new McpServer(dbPath);
    await server.RunAsync(cancellationToken);
});
rootCommand.Add(mcpCommand);

// --- setup-claude command ---
var setupDbOption = new Option<string>("--db") { Description = "Path to graph.db used by commands", DefaultValueFactory = _ => "./ai-code-graph/graph.db" };
var setupCommand = new Command("setup-claude", "Scaffold Claude Code slash commands, CLAUDE.md snippet, and MCP config into the current project")
{
    setupDbOption
};

setupCommand.SetAction((parseResult, _) =>
{
    var dbPath = parseResult.GetValue(setupDbOption) ?? "./ai-code-graph/graph.db";
    var created = new List<string>();

    // 1. Create .claude/commands/ directory
    var commandsDir = Path.Combine(Directory.GetCurrentDirectory(), ".claude", "commands");
    Directory.CreateDirectory(commandsDir);

    // 2. Write slash command files
    var contextCmd = Path.Combine(commandsDir, "context.md");
    if (!File.Exists(contextCmd))
    {
        File.WriteAllText(contextCmd, $@"Get method context before editing: $ARGUMENTS

Steps:
1. Run `ai-code-graph context ""$ARGUMENTS"" --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Review the output: complexity, callers, callees, cluster, and duplicates
4. If complexity (CC) is high (>10), warn about the method's complexity before making changes
5. If the method has callers, note that changes may affect those callers
6. If duplicates exist, suggest whether the change should also apply to the duplicate methods
7. Proceed with the user's requested edit, keeping the context in mind
");
        created.Add(contextCmd);
    }

    var hotspotsCmd = Path.Combine(commandsDir, "hotspots.md");
    if (!File.Exists(hotspotsCmd))
    {
        File.WriteAllText(hotspotsCmd, $@"Show complexity hotspots in the codebase.

Steps:
1. Run `ai-code-graph hotspots --top 15 --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results, highlighting methods with CC > 15 as candidates for refactoring
4. For the top 3 hotspots, briefly suggest what makes them complex (deep nesting, many branches, etc.)
");
        created.Add(hotspotsCmd);
    }

    var duplicatesCmd = Path.Combine(commandsDir, "duplicates.md");
    if (!File.Exists(duplicatesCmd))
    {
        File.WriteAllText(duplicatesCmd, $@"Show detected code duplicates in the codebase.

Steps:
1. Run `ai-code-graph duplicates --top 15 --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Group the results by clone type (Type1 = exact, Type2 = renamed, Semantic = similar logic)
4. For Type1 clones, suggest extracting a shared utility method
5. For Semantic clones, suggest whether they represent a pattern worth abstracting
");
        created.Add(duplicatesCmd);
    }

    var driftCmd = Path.Combine(commandsDir, "drift.md");
    if (!File.Exists(driftCmd))
    {
        File.WriteAllText(driftCmd, $@"Run drift detection against the baseline.

Steps:
1. Check if `{Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "baseline.db")}` exists. If not, inform the user to run `ai-code-graph analyze --save-baseline` first
2. Run `ai-code-graph drift --vs {Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "baseline.db")} --format detail --db {dbPath}`
3. Summarize findings:
   - New methods added
   - Methods removed
   - Complexity regressions (methods that got more complex)
   - New duplicates introduced
   - Intent scattering (logic spreading across namespaces)
4. For complexity regressions, show the before/after values and suggest refactoring if the increase is significant
");
        created.Add(driftCmd);
    }

    // 3. Create .mcp.json for MCP server integration
    var mcpJson = Path.Combine(Directory.GetCurrentDirectory(), ".mcp.json");
    if (!File.Exists(mcpJson))
    {
        File.WriteAllText(mcpJson, $$"""
{
  "mcpServers": {
    "ai-code-graph": {
      "type": "stdio",
      "command": "ai-code-graph",
      "args": ["mcp", "--db", "{{dbPath}}"]
    }
  }
}
""");
        created.Add(mcpJson);
    }

    // 4. Append auto-context section to CLAUDE.md
    var claudeMd = Path.Combine(Directory.GetCurrentDirectory(), "CLAUDE.md");
    var snippet = $@"
## Auto-Context: Code Graph Integration

When modifying methods in this codebase, run the context command first if `{dbPath}` exists:

```bash
ai-code-graph context ""MethodName"" --db {dbPath}
```

This returns complexity, callers, callees, cluster membership, and duplicates in one call. Use this information to:
- Avoid increasing complexity of already-complex methods (CC > 10)
- Update callers if you change a method's signature or behavior
- Apply the same fix to duplicates when fixing bugs
- Understand which intent cluster a method belongs to before refactoring

Available slash commands:
- `/context <method>` - Full method context before editing
- `/hotspots` - Top complexity hotspots
- `/duplicates` - Detected code clones
- `/drift` - Architectural drift from baseline

To rebuild the graph after significant changes: `ai-code-graph analyze -s YourSolution.sln`
";

    if (File.Exists(claudeMd))
    {
        var existing = File.ReadAllText(claudeMd);
        if (!existing.Contains("Auto-Context: Code Graph Integration"))
        {
            File.AppendAllText(claudeMd, snippet);
            created.Add(claudeMd + " (appended)");
        }
    }
    else
    {
        File.WriteAllText(claudeMd, $"# Claude Code Instructions\n{snippet}");
        created.Add(claudeMd);
    }

    // Summary
    if (created.Count > 0)
    {
        Console.WriteLine("Claude Code integration set up:");
        foreach (var path in created)
            Console.WriteLine($"  + {Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  1. Run: ai-code-graph analyze YourSolution.sln");
        Console.WriteLine($"  2. Use /context, /hotspots, /duplicates, /drift in Claude Code");
        Console.WriteLine($"  3. MCP tools are available to any MCP-compatible IDE");
    }
    else
    {
        Console.WriteLine("All Claude Code integration files already exist. Nothing to do.");
    }

    return Task.CompletedTask;
});
rootCommand.Add(setupCommand);

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

static string CsvEscape(string value)
{
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        return $"\"{value.Replace("\"", "\"\"")}\"";
    return value;
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
