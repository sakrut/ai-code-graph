using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class TokenSearchCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var queryArgument = new Argument<string>("query")
        {
            Description = "Natural language search query"
        };

        var topOption = new Option<int>("--top", "-t")
        {
            Description = "Number of results",
            DefaultValueFactory = _ => 10
        };

        var thresholdOption = new Option<float>("--threshold")
        {
            Description = "Minimum similarity score",
            DefaultValueFactory = _ => 0.5f
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

        var command = new Command("token-search", "Search code by token overlap")
        {
            queryArgument, topOption, thresholdOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var top = parseResult.GetValue(topOption);
            var threshold = parseResult.GetValue(thresholdOption);
            var format = parseResult.GetValue(formatOption) ?? "table";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

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
            var index = VectorIndexCache.GetOrBuild(dbPath, allEmbeddings);
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

        return command;
    }
}
