using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using AiCodeGraph.Core;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Models.CodeGraph;
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

        // 6. Store results
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
        Console.WriteLine($" done ({stageTimer.Elapsed.TotalSeconds:F1}s)");

        // 7. Summary
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

rootCommand.Add(analyzeCommand);

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
