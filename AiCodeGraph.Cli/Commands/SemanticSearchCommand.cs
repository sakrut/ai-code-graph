using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class SemanticSearchCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var queryArgument = new Argument<string>("query")
        {
            Description = "Natural language search query"
        };

        var topOption = new Option<int>("--top", "-n")
        {
            Description = "Number of results",
            DefaultValueFactory = _ => 10
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "table|json",
            DefaultValueFactory = _ => "table"
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("semantic-search", "Search code by semantic meaning (fallback when query returns no results)")
        {
            queryArgument, topOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var top = parseResult.GetValue(topOption);
            var format = parseResult.GetValue(formatOption) ?? "table";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var allEmbeddings = await storage.GetEmbeddingsAsync(cancellationToken);
            if (allEmbeddings.Count == 0)
            {
                Console.Error.WriteLine("No embeddings found. Run 'analyze' first.");
                Environment.ExitCode = 1;
                return;
            }

            var engineType = await storage.GetMetadataAsync("embedding_engine", cancellationToken) ?? "hash";
            var modelName = await storage.GetMetadataAsync("embedding_model", cancellationToken);
            var dimStr = await storage.GetMetadataAsync("embedding_dimensions", cancellationToken);
            var dimensions = int.TryParse(dimStr, out var d) ? d : 384;

            if (engineType == "hash")
            {
                Console.Error.WriteLine("Warning: Database uses hash-based embeddings. Results are token-overlap, not semantic.");
                Console.Error.WriteLine("Re-analyze with --embedding-engine openai for true semantic search.");
            }

            using var engine = AnalysisStageHelpers.CreateEmbeddingEngine(engineType, modelName, dimensions, false);
            var queryVector = engine.GenerateEmbedding(query);

            var index = VectorIndexCache.GetOrBuild(dbPath, allEmbeddings);
            var searchResults = index.Search(queryVector, top);

            if (searchResults.Count == 0)
            {
                Console.WriteLine("No results found.");
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
                    engine = engineType,
                    results = enriched
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Semantic search: \"{query}\" (engine: {engineType})");
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

        return command;
    }
}
