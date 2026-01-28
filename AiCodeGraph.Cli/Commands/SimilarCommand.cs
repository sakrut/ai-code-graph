using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class SimilarCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var methodArgument = new Argument<string>("method")
        {
            Description = "Method name to find similar methods for"
        };

        var topOption = new Option<int>("--top", "-t")
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

        var command = new Command("similar", "Find methods with similar intent")
        {
            methodArgument, topOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var method = parseResult.GetValue(methodArgument)!;
            var top = parseResult.GetValue(topOption);
            var format = parseResult.GetValue(formatOption) ?? "table";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

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

            var index = VectorIndexCache.GetOrBuild(dbPath, allEmbeddings);
            var results = index.Search(targetEmbedding.Vector, top + 1)
                .Where(r => r.Id != targetId)
                .Take(top)
                .ToList();

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

        return command;
    }
}
