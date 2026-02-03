using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class CallgraphCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var methodArgument = new Argument<string>("method")
        {
            Description = "Method name or pattern to search for"
        };

        var depthOption = new Option<int>("--depth", "-d")
        {
            Description = "Traversal depth",
            DefaultValueFactory = _ => 2
        };

        var directionOption = new Option<string>("--direction")
        {
            Description = "callers|callees|both",
            DefaultValueFactory = _ => "both"
        };

        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var dbOption = OutputOptions.CreateDbOption();

        var command = new Command("callgraph", "Explore method call graph")
        {
            methodArgument, depthOption, directionOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var method = parseResult.GetValue(methodArgument)!;
            var depth = parseResult.GetValue(depthOption);
            var direction = parseResult.GetValue(directionOption) ?? "both";
            var format = parseResult.GetValue(formatOption) ?? "compact";
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

            if (OutputOptions.IsJson(format))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    root = new { methodId = rootId, name = rootInfo?.FullName },
                    nodes = nodes.OrderBy(n => n.FullName).Select(n => new { methodId = n.Id, name = n.FullName, n.Depth }),
                    edges = edges.OrderBy(e => e.From).ThenBy(e => e.To).Select(e => new { from = e.From, to = e.To }),
                    metadata = new { depth, direction }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                Console.WriteLine(rootInfo?.FullName ?? rootId);
                // Flat compact output: callers first, then callees
                var callers = edges.Where(e => e.To == rootId).Select(e => e.From).ToList();
                var callees = edges.Where(e => e.From == rootId).Select(e => e.To).ToList();

                foreach (var callerId in callers)
                {
                    var node = nodes.FirstOrDefault(n => n.Id == callerId);
                    Console.WriteLine($"← {node.FullName}");
                }
                foreach (var calleeId in callees)
                {
                    var node = nodes.FirstOrDefault(n => n.Id == calleeId);
                    Console.WriteLine($"→ {node.FullName}");
                }
            }
            else // table/tree
            {
                Console.WriteLine($"{rootInfo?.FullName ?? rootId}");
                OutputHelpers.PrintCallTree(rootId, edges, nodes, 1, depth, new HashSet<string> { rootId });
            }
        });

        return command;
    }
}
