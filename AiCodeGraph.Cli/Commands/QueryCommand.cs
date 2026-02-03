using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using AiCodeGraph.Core.Query;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class QueryCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var queryFileOption = new Option<FileInfo?>("--query-file")
        {
            Description = "Path to JSON file containing the query"
        };

        var queryJsonArgument = new Argument<string?>("query")
        {
            Description = "Inline JSON query (alternative to --query-file)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var schemaOption = new Option<bool>("--schema")
        {
            Description = "Output JSON schema for GraphQuery and exit"
        };

        var dbOption = OutputOptions.CreateDbOption();
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);

        var command = new Command("query", "Execute a unified graph query from JSON")
        {
            queryJsonArgument, queryFileOption, schemaOption, dbOption, formatOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var showSchema = parseResult.GetValue(schemaOption);
            if (showSchema)
            {
                Console.WriteLine(GraphQuerySerializer.GenerateJsonSchema());
                return;
            }

            var queryFile = parseResult.GetValue(queryFileOption);
            var queryJson = parseResult.GetValue(queryJsonArgument);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var format = parseResult.GetValue(formatOption) ?? "compact";

            // Get query JSON from file or argument
            string? json = null;
            if (queryFile != null)
            {
                if (!queryFile.Exists)
                {
                    Console.Error.WriteLine($"Error: Query file not found: {queryFile.FullName}");
                    return;
                }
                json = await File.ReadAllTextAsync(queryFile.FullName, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(queryJson))
            {
                json = queryJson;
            }
            else
            {
                Console.Error.WriteLine("Error: Either --query-file or inline query argument is required");
                Console.Error.WriteLine("Use --schema to see the query format");
                return;
            }

            // Parse query
            if (!GraphQuerySerializer.TryDeserialize(json, out var query, out var parseError))
            {
                Console.Error.WriteLine($"Error parsing query: {parseError}");
                return;
            }

            if (query == null)
            {
                Console.Error.WriteLine("Error: Query is null");
                return;
            }

            // Validate query
            var validation = query.Validate();
            if (!validation.IsValid)
            {
                Console.Error.WriteLine("Query validation failed:");
                foreach (var error in validation.Errors)
                {
                    Console.Error.WriteLine($"  - {error}");
                }
                return;
            }

            // Check database
            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            // Execute query
            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var traversalEngine = new GraphTraversalEngine(storage);
            var executor = new GraphQueryExecutor(storage, traversalEngine);

            var result = await executor.ExecuteAsync(query, useCache: true, ct: cancellationToken);

            if (!result.Success)
            {
                Console.Error.WriteLine($"Error: {result.Error}");
                return;
            }

            // Output results
            OutputResults(result, format);
        });

        return command;
    }

    private static void OutputResults(QueryResult result, string format)
    {
        if (OutputOptions.IsJson(format))
        {
            var json = JsonSerializer.Serialize(new
            {
                success = result.Success,
                totalMatches = result.TotalMatches,
                returned = result.Nodes.Count,
                executionTimeMs = result.ExecutionTime.TotalMilliseconds,
                nodes = result.Nodes.Select(n => new
                {
                    n.MethodId,
                    n.FullName,
                    n.Depth,
                    n.RankScore,
                    n.Complexity,
                    n.Loc,
                    n.Nesting,
                    n.FilePath,
                    n.Line
                })
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            Console.WriteLine(json);
        }
        else if (OutputOptions.IsCompact(format))
        {
            Console.WriteLine($"# Query returned {result.Nodes.Count} of {result.TotalMatches} matches ({result.ExecutionTime.TotalMilliseconds:F0}ms)");
            foreach (var node in result.Nodes)
            {
                var metrics = node.Complexity.HasValue ? $" CC:{node.Complexity}" : "";
                var location = node.FilePath != null ? $" {Path.GetFileName(node.FilePath)}:{node.Line}" : "";
                var score = node.RankScore.HasValue ? $" Score:{node.RankScore:F1}" : "";
                Console.WriteLine($"[{node.Depth}] {node.FullName}{metrics}{score}{location}");
            }
        }
        else // table
        {
            if (result.Nodes.Count == 0)
            {
                Console.WriteLine("No results found.");
                return;
            }

            var nameWidth = Math.Min(60, result.Nodes.Max(n => n.FullName.Length));
            Console.WriteLine($"{"Depth",5}  {"Method".PadRight(nameWidth)}  {"CC",4}  {"Score",6}  Location");
            Console.WriteLine(new string('-', nameWidth + 30));

            foreach (var node in result.Nodes)
            {
                var name = node.FullName.Length > nameWidth
                    ? node.FullName[..(nameWidth - 3)] + "..."
                    : node.FullName;
                var cc = node.Complexity?.ToString() ?? "-";
                var score = node.RankScore?.ToString("F1") ?? "-";
                var location = node.FilePath != null
                    ? $"{Path.GetFileName(node.FilePath)}:{node.Line}"
                    : "";
                Console.WriteLine($"{node.Depth,5}  {name.PadRight(nameWidth)}  {cc,4}  {score,6}  {location}");
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {result.Nodes.Count} of {result.TotalMatches} matches ({result.ExecutionTime.TotalMilliseconds:F0}ms)");
        }
    }
}
