using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class SimilarCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var methodArgument = new Argument<string?>("method")
        {
            Description = "Method name to find similar methods for",
            Arity = ArgumentArity.ZeroOrOne
        };

        var idOption = OutputOptions.CreateMethodIdOption();
        var topOption = OutputOptions.CreateTopOption(10);
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var dbOption = OutputOptions.CreateDbOption();

        var command = new Command("similar", "Find methods with similar intent")
        {
            methodArgument, idOption, topOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var method = parseResult.GetValue(methodArgument);
            var methodId = parseResult.GetValue(idOption);
            var top = parseResult.GetValue(topOption);
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var targetId = await MethodResolver.ResolveAsync(storage, methodId, method, cancellationToken);
            if (targetId == null) return;
            var allEmbeddings = await storage.GetEmbeddingsAsync(cancellationToken);

            if (allEmbeddings.Count == 0)
            {
                Console.Error.WriteLine("No embeddings found. Run 'analyze' first.");
                Environment.ExitCode = 1;
                return;
            }

            var targetInfo = await storage.GetMethodInfoAsync(targetId, cancellationToken);
            var targetEmbedding = allEmbeddings.FirstOrDefault(e => e.MethodId == targetId);
            if (targetEmbedding.Vector == null)
            {
                Console.Error.WriteLine($"No embedding found for method '{targetId}'.");
                Environment.ExitCode = 1;
                return;
            }

            var index = VectorIndexCache.GetOrBuild(dbPath, allEmbeddings);
            var results = index.Search(targetEmbedding.Vector, top + 1)
                .Where(r => r.Id != targetId)
                .Take(top)
                .ToList();

            if (OutputOptions.IsJson(format))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    query = new { methodId = targetId, name = targetInfo?.FullName ?? targetId },
                    items = results.Select(r => new { methodId = r.Id, score = Math.Round(r.Score, 4) }),
                    metadata = new { top }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                Console.WriteLine($"Similar to: {targetInfo?.FullName ?? targetId}");
                foreach (var (id, score) in results)
                {
                    var info = await storage.GetMethodInfoAsync(id, cancellationToken);
                    Console.WriteLine($"{score:F3} {info?.FullName ?? id}");
                }
            }
            else // table
            {
                Console.WriteLine($"Methods similar to: {targetInfo?.FullName ?? targetId}");
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
