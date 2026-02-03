using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using AiCodeGraph.Core;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class AnalyzeCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var solutionArgument = new Argument<string?>("solution")
        {
            Description = "Path to .sln file (auto-discovered if omitted)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var solutionOption = new Option<string?>("--solution", "-s")
        {
            Description = "Path to .sln file (auto-discovered if omitted)"
        };

        var outputOption = new Option<string>("--output", "-o", "--db")
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

        var embeddingEngineOption = new Option<string>("--embedding-engine")
        {
            Description = "Embedding engine: hash|openai|onnx",
            DefaultValueFactory = _ => "hash"
        };

        var embeddingModelOption = new Option<string?>("--embedding-model")
        {
            Description = "Model name (e.g., text-embedding-3-small for openai, path for onnx)"
        };

        var embeddingDimensionsOption = new Option<int>("--embedding-dimensions")
        {
            Description = "Embedding vector dimensions",
            DefaultValueFactory = _ => 384
        };

        var stagesOption = new Option<string>("--stages")
        {
            Description = "Analysis stages: core (fast, essential) | full (all features)",
            DefaultValueFactory = _ => "core"
        };

        var command = new Command("analyze", "Analyze a .NET solution and build the code graph")
        {
            solutionArgument,
            solutionOption,
            outputOption,
            verboseOption,
            saveBaselineOption,
            embeddingEngineOption,
            embeddingModelOption,
            embeddingDimensionsOption,
            stagesOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            // Prefer positional argument, fallback to option for backward compatibility
            var solutionPath = parseResult.GetValue(solutionArgument) ?? parseResult.GetValue(solutionOption);
            var output = parseResult.GetValue(outputOption) ?? "./ai-code-graph";
            var verbose = parseResult.GetValue(verboseOption);
            var saveBaseline = parseResult.GetValue(saveBaselineOption);
            var embeddingEngine = parseResult.GetValue(embeddingEngineOption) ?? "hash";
            var embeddingModel = parseResult.GetValue(embeddingModelOption);
            var embeddingDimensions = parseResult.GetValue(embeddingDimensionsOption);
            var stages = parseResult.GetValue(stagesOption) ?? "core";
            var isFull = stages.Equals("full", StringComparison.OrdinalIgnoreCase);
            var totalTimer = Stopwatch.StartNew();

            try
            {
                var resolvedPath = SolutionDiscovery.FindSolutionFile(solutionPath);
                Console.WriteLine($"Solution: {resolvedPath}");

                var workspace = await AnalysisStageHelpers.LoadWorkspaceStage(resolvedPath, verbose, cancellationToken);
                var extractionResults = AnalysisStageHelpers.ExtractCodeModelStage(workspace, verbose);
                var edges = AnalysisStageHelpers.BuildCallGraphStage(workspace);
                var metrics = AnalysisStageHelpers.ComputeMetricsStage(workspace);
                var normalized = AnalysisStageHelpers.NormalizeMethodsStage(workspace);

                using var engine = AnalysisStageHelpers.CreateEmbeddingEngine(embeddingEngine, embeddingModel, embeddingDimensions, verbose);
                var embeddingResults = AnalysisStageHelpers.GenerateEmbeddingsStage(normalized, engine, embeddingEngine);

                Directory.CreateDirectory(output);
                var dbPath = Path.Combine(output, "graph.db");

                await using var storage = new StorageService(dbPath);
                await AnalysisStageHelpers.StoreResultsStage(storage, extractionResults, edges, metrics, normalized, embeddingResults, cancellationToken);
                var blastRadius = await AnalysisStageHelpers.ComputeBlastRadiusStage(storage, verbose, cancellationToken);
                var (clonePairs, clusters) = await AnalysisStageHelpers.DetectDuplicatesStage(storage, normalized, embeddingResults, cancellationToken, includeClusters: isFull);

                await storage.SaveMetadataAsync("embedding_engine", embeddingEngine, cancellationToken);
                await storage.SaveMetadataAsync("embedding_model", embeddingModel ?? (embeddingEngine == "hash" ? "hash-v1" : ""), cancellationToken);
                await storage.SaveMetadataAsync("embedding_dimensions", embeddingDimensions.ToString(), cancellationToken);

                // Save analysis metadata for staleness detection
                await storage.SaveMetadataAsync("analyzed_at", DateTimeOffset.UtcNow.ToString("o"), cancellationToken);
                await storage.SaveMetadataAsync("solution_path", Path.GetFullPath(resolvedPath), cancellationToken);
                await storage.SaveMetadataAsync("tool_version", typeof(AnalyzeCommand).Assembly.GetName().Version?.ToString() ?? "unknown", cancellationToken);
                await storage.SaveMetadataAsync("git_commit", GitHelpers.GetCurrentCommitHash() ?? "", cancellationToken);
                await storage.SaveMetadataAsync("stages", stages, cancellationToken);

                if (saveBaseline)
                    AnalysisStageHelpers.SaveBaselineStage(output, dbPath);

                totalTimer.Stop();
                AnalysisStageHelpers.PrintAnalysisSummary(extractionResults, edges, metrics, clonePairs, clusters, totalTimer, dbPath);

                VectorIndexCache.Invalidate(dbPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
            {
                CommandHelpers.HandleCommandError(ex, verbose);
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

        return command;
    }
}
