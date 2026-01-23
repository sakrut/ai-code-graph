using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddSingleton<IWorkspaceLoader, WorkspaceLoader>();
services.AddSingleton<ICodeGraphStorage, CodeGraphStorage>();

var serviceProvider = services.BuildServiceProvider();

var rootCommand = new RootCommand("AI Code Graph - Roslyn-based static analysis tool for .NET codebases");

var solutionPathArgument = new Argument<string>("solution-path")
{
    Description = "Path to the .sln file to analyze"
};

var analyzeCommand = new Command("analyze", "Analyze a .NET solution and build the code graph")
{
    solutionPathArgument
};

analyzeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var solutionPath = parseResult.GetValue(solutionPathArgument)!;
    var loader = serviceProvider.GetRequiredService<IWorkspaceLoader>();
    var storage = serviceProvider.GetRequiredService<ICodeGraphStorage>();

    Console.WriteLine($"Analyzing solution: {solutionPath}");

    await storage.InitializeAsync(cancellationToken);
    var workspace = await loader.LoadSolutionAsync(solutionPath, cancellationToken);

    Console.WriteLine($"Loaded {workspace.Compilations.Count} projects.");

    if (workspace.HasErrors)
    {
        foreach (var diag in workspace.Diagnostics)
            Console.Error.WriteLine($"  [{diag.Kind}] {diag.Message}");
    }

    await storage.SaveAsync(cancellationToken);

    Console.WriteLine("Analysis complete. Output written to ./ai-code-graph/");
});

rootCommand.Add(analyzeCommand);

var parseResult = CommandLineParser.Parse(rootCommand, args);
return await parseResult.InvokeAsync();
