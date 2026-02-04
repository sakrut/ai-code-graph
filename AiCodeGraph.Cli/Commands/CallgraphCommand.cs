using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class CallgraphCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var methodArgument = new Argument<string?>("method")
        {
            Description = "Method name or pattern to search for",
            Arity = ArgumentArity.ZeroOrOne
        };

        var idOption = OutputOptions.CreateMethodIdOption();

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
            methodArgument, idOption, depthOption, directionOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var method = parseResult.GetValue(methodArgument);
            var methodId = parseResult.GetValue(idOption);
            var depth = parseResult.GetValue(depthOption);
            var direction = parseResult.GetValue(directionOption) ?? "both";
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var rootId = await MethodResolver.ResolveAsync(storage, methodId, method, cancellationToken);
            if (rootId == null) return;
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
                    Console.WriteLine($"<- {node.FullName}");
                }
                foreach (var calleeId in callees)
                {
                    var node = nodes.FirstOrDefault(n => n.Id == calleeId);
                    Console.WriteLine($"-> {node.FullName}");
                }
            }
            else // table/tree
            {
                Console.WriteLine($"{rootInfo?.FullName ?? rootId}");
                OutputHelpers.PrintCallTree(rootId, edges, nodes, 1, depth, new HashSet<string> { rootId });
            }

            // Check for protected zones in the call graph
            if (!OutputOptions.IsJson(format))
            {
                var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(dbPath)) ?? ".";
                var zoneManager = ProtectedZoneManager.TryLoadFromProject(projectRoot);
                if (zoneManager.Zones.Count > 0)
                {
                    var protectedInGraph = await zoneManager.FilterProtectedAsync(visited, storage, cancellationToken);
                    if (protectedInGraph.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"[!] Protected zones in graph ({protectedInGraph.Count}):");
                        foreach (var (protectedId, fullName, zone) in protectedInGraph.Take(5))
                        {
                            var levelText = zone.Level switch
                            {
                                ProtectionLevel.DoNotModify => "[DO NOT MODIFY]",
                                ProtectionLevel.RequireApproval => "[REQUIRES APPROVAL]",
                                ProtectionLevel.Deprecated => "[DEPRECATED]",
                                _ => $"[{zone.Level}]"
                            };
                            Console.WriteLine($"  {levelText} {fullName}");
                        }
                        if (protectedInGraph.Count > 5)
                            Console.WriteLine($"  (+{protectedInGraph.Count - 5} more)");
                    }
                }
            }
        });

        return command;
    }
}
