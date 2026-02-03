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
        // Quick query options
        var seedOption = new Option<string?>("--seed")
        {
            Description = "Quick seed: method pattern or exact ID (e.g., '*Service*' or 'MyApp.UserService.GetUser()')"
        };

        var depthOption = new Option<int>("--depth")
        {
            Description = "Traversal depth (default: 3)",
            DefaultValueFactory = _ => 3
        };

        var directionOption = new Option<string>("--direction")
        {
            Description = "Traversal direction: callers|callees|both|none (default: both)",
            DefaultValueFactory = _ => "both"
        };

        var rankOption = new Option<string>("--rank")
        {
            Description = "Ranking strategy: blast-radius|complexity|coupling|combined (default: blast-radius)",
            DefaultValueFactory = _ => "blast-radius"
        };

        var topOption = OutputOptions.CreateTopOption(20);

        // Full query options
        var queryFileOption = new Option<FileInfo?>("--query-file")
        {
            Description = "Path to JSON file containing the full query"
        };

        var jsonOption = new Option<string?>("--json")
        {
            Description = "Inline JSON query (for complex queries)"
        };

        var schemaOption = new Option<bool>("--schema")
        {
            Description = "Output JSON schema for GraphQuery and exit"
        };

        var dbOption = OutputOptions.CreateDbOption();
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);

        var command = new Command("query", "Execute a graph query (recommended for agents)")
        {
            seedOption, depthOption, directionOption, rankOption, topOption,
            queryFileOption, jsonOption, schemaOption, dbOption, formatOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var showSchema = parseResult.GetValue(schemaOption);
            if (showSchema)
            {
                Console.WriteLine(GraphQuerySerializer.GenerateJsonSchema());
                return;
            }

            var seed = parseResult.GetValue(seedOption);
            var depth = parseResult.GetValue(depthOption);
            var direction = parseResult.GetValue(directionOption);
            var rank = parseResult.GetValue(rankOption);
            var top = parseResult.GetValue(topOption);
            var queryFile = parseResult.GetValue(queryFileOption);
            var queryJson = parseResult.GetValue(jsonOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var format = parseResult.GetValue(formatOption) ?? "compact";

            GraphQuery query;

            // Priority: --query-file > --json > --seed
            if (queryFile != null)
            {
                if (!queryFile.Exists)
                {
                    Console.Error.WriteLine($"Error: Query file not found: {queryFile.FullName}");
                    return;
                }
                var json = await File.ReadAllTextAsync(queryFile.FullName, cancellationToken);
                if (!GraphQuerySerializer.TryDeserialize(json, out var parsedQuery, out var parseError) || parsedQuery == null)
                {
                    Console.Error.WriteLine($"Error parsing query file: {parseError}");
                    return;
                }
                query = parsedQuery;
            }
            else if (!string.IsNullOrWhiteSpace(queryJson))
            {
                if (!GraphQuerySerializer.TryDeserialize(queryJson, out var parsedQuery, out var parseError) || parsedQuery == null)
                {
                    Console.Error.WriteLine($"Error parsing JSON query: {parseError}");
                    return;
                }
                query = parsedQuery;
            }
            else if (!string.IsNullOrWhiteSpace(seed))
            {
                // Build query from quick options
                query = BuildQueryFromOptions(seed, depth, direction ?? "both", rank ?? "blast-radius", top);
            }
            else
            {
                Console.Error.WriteLine("Error: One of --seed, --json, or --query-file is required");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Quick query examples:");
                Console.Error.WriteLine("  query --seed \"*Service*\" --depth 2 --rank complexity");
                Console.Error.WriteLine("  query --seed \"MyApp.UserService.GetUser()\" --direction callers");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Full JSON query:");
                Console.Error.WriteLine("  query --json '{\"seed\":{\"methodPattern\":\"*Validate*\"},\"expand\":{\"direction\":\"callers\"}}'");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Use --schema to see the full query format");
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
            OutputResults(result, query, format);
        });

        return command;
    }

    private static GraphQuery BuildQueryFromOptions(string seed, int depth, string direction, string rank, int top)
    {
        // Determine if seed is a pattern (contains * or ?) or exact ID
        var isPattern = seed.Contains('*') || seed.Contains('?');

        var querySeed = isPattern
            ? new QuerySeed { MethodPattern = seed }
            : new QuerySeed { MethodId = seed };

        var expandDirection = direction.ToLower() switch
        {
            "callers" => ExpandDirection.Callers,
            "callees" => ExpandDirection.Callees,
            "both" => ExpandDirection.Both,
            "none" => ExpandDirection.None,
            _ => ExpandDirection.Both
        };

        var rankStrategy = rank.ToLower().Replace("-", "") switch
        {
            "blastradius" => RankStrategy.BlastRadius,
            "complexity" => RankStrategy.Complexity,
            "coupling" => RankStrategy.Coupling,
            "combined" => RankStrategy.Combined,
            _ => RankStrategy.BlastRadius
        };

        return new GraphQuery
        {
            Seed = querySeed,
            Expand = new QueryExpand
            {
                Direction = expandDirection,
                MaxDepth = depth,
                IncludeTransitive = expandDirection != ExpandDirection.None
            },
            Rank = new QueryRank
            {
                Strategy = rankStrategy,
                Descending = true
            },
            Output = new QueryOutput
            {
                MaxResults = top,
                IncludeMetrics = true,
                IncludeLocation = true
            }
        };
    }

    private static void OutputResults(QueryResult result, GraphQuery query, string format)
    {
        if (OutputOptions.IsJson(format))
        {
            var json = JsonSerializer.Serialize(new
            {
                success = result.Success,
                query = new
                {
                    seed = query.Seed.MethodId ?? query.Seed.MethodPattern ?? query.Seed.Namespace ?? query.Seed.Cluster,
                    direction = query.Expand?.Direction.ToString().ToLower(),
                    depth = query.Expand?.MaxDepth,
                    rank = query.Rank?.Strategy.ToString()
                },
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
            // Query summary line
            var seedInfo = query.Seed.MethodId ?? query.Seed.MethodPattern ?? query.Seed.Namespace ?? query.Seed.Cluster ?? "?";
            var dirInfo = query.Expand?.Direction.ToString().ToLower() ?? "both";
            var depthInfo = query.Expand?.MaxDepth ?? 3;
            var rankInfo = query.Rank?.Strategy.ToString().ToLower() ?? "blast-radius";

            Console.WriteLine($"Query: seed={seedInfo}, direction={dirInfo}, depth={depthInfo}, rank={rankInfo}");
            Console.WriteLine($"Results ({result.Nodes.Count} of {result.TotalMatches}):");

            foreach (var node in result.Nodes)
            {
                var br = node.RankScore.HasValue ? $"BR={node.RankScore:F0}" : "";
                var cc = node.Complexity.HasValue ? $"CC={node.Complexity}" : "";
                var metrics = string.Join(" ", new[] { br, cc }.Where(s => !string.IsNullOrEmpty(s)));
                metrics = metrics.Length > 0 ? metrics + " " : "";

                var location = node.FilePath != null
                    ? $" {Path.GetFileName(node.FilePath)}:{node.Line}"
                    : "";

                Console.WriteLine($"{metrics}{node.FullName}{location}");
            }
        }
        else // table
        {
            if (result.Nodes.Count == 0)
            {
                Console.WriteLine("No results found.");
                return;
            }

            var nameWidth = Math.Min(55, result.Nodes.Max(n => n.FullName.Length));
            Console.WriteLine($"{"BR",5}  {"CC",4}  {"Method".PadRight(nameWidth)}  Location");
            Console.WriteLine(new string('-', nameWidth + 20));

            foreach (var node in result.Nodes)
            {
                var name = node.FullName.Length > nameWidth
                    ? node.FullName[..(nameWidth - 3)] + "..."
                    : node.FullName;
                var br = node.RankScore?.ToString("F0") ?? "-";
                var cc = node.Complexity?.ToString() ?? "-";
                var location = node.FilePath != null
                    ? $"{Path.GetFileName(node.FilePath)}:{node.Line}"
                    : "";
                Console.WriteLine($"{br,5}  {cc,4}  {name.PadRight(nameWidth)}  {location}");
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {result.Nodes.Count} of {result.TotalMatches} matches ({result.ExecutionTime.TotalMilliseconds:F0}ms)");
        }
    }
}
