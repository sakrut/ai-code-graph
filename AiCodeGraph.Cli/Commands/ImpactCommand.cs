using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class ImpactCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var methodArgument = new Argument<string?>("method")
        {
            Description = "Method name or pattern to search for",
            Arity = ArgumentArity.ZeroOrOne
        };

        var idOption = OutputOptions.CreateMethodIdOption();

        var depthOption = new Option<int?>("--depth", "-d")
        {
            Description = "Max traversal depth (unlimited if omitted)"
        };

        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var topOption = OutputOptions.CreateTopOption(20);
        var dbOption = OutputOptions.CreateDbOption();

        var command = new Command("impact", "Show transitive impact of changing a method (all callers)")
        {
            methodArgument, idOption, depthOption, formatOption, topOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var method = parseResult.GetValue(methodArgument);
            var methodId = parseResult.GetValue(idOption);
            var maxDepth = parseResult.GetValue(depthOption);
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var top = parseResult.GetValue(topOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var targetId = await MethodResolver.ResolveAsync(storage, methodId, method, cancellationToken);
            if (targetId == null) return;
            var targetInfo = await storage.GetMethodInfoAsync(targetId, cancellationToken);

            // BFS traversal for transitive callers
            var visited = new HashSet<string> { targetId };
            var queue = new Queue<(string Id, int Depth)>();
            var parentMap = new Dictionary<string, List<string>>(); // child -> parents (callers)
            var depthMap = new Dictionary<string, int> { [targetId] = 0 };
            var entryPoints = new List<string>();

            queue.Enqueue((targetId, 0));

            while (queue.Count > 0)
            {
                var (currentId, currentDepth) = queue.Dequeue();

                if (maxDepth.HasValue && currentDepth >= maxDepth.Value) continue;

                var callers = await storage.GetCallersAsync(currentId, cancellationToken);
                if (callers.Count == 0 && currentId != targetId)
                    entryPoints.Add(currentId);

                foreach (var callerId in callers)
                {
                    if (!parentMap.ContainsKey(currentId))
                        parentMap[currentId] = new List<string>();
                    parentMap[currentId].Add(callerId);

                    if (visited.Add(callerId))
                    {
                        depthMap[callerId] = currentDepth + 1;
                        queue.Enqueue((callerId, currentDepth + 1));
                    }
                }
            }

            // Check which callers at the edge are entry points (no further callers)
            foreach (var id in visited)
            {
                if (id == targetId) continue;
                var callers = await storage.GetCallersAsync(id, cancellationToken);
                if (callers.All(c => !visited.Contains(c)) && callers.Count == 0 && !entryPoints.Contains(id))
                    entryPoints.Add(id);
            }

            if (OutputOptions.IsJson(format))
            {
                var nodeList = new List<object>();
                foreach (var id in visited)
                {
                    var info = await storage.GetMethodInfoAsync(id, cancellationToken);
                    nodeList.Add(new { methodId = id, name = info?.FullName ?? id, depth = depthMap.GetValueOrDefault(id), isEntryPoint = entryPoints.Contains(id) });
                }

                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    target = new { methodId = targetId, name = targetInfo?.FullName ?? targetId },
                    affectedMethods = visited.Count,
                    entryPointCount = entryPoints.Count,
                    maxDepthReached = depthMap.Values.DefaultIfEmpty(0).Max(),
                    nodes = nodeList,
                    entryPoints = entryPoints
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                Console.WriteLine($"Impact: {targetInfo?.FullName ?? targetId}");
                Console.WriteLine($"Affected: {visited.Count} methods, {entryPoints.Count} entry points");

                // Flat list of affected methods by depth
                var affected = visited.Where(id => id != targetId)
                    .OrderBy(id => depthMap.GetValueOrDefault(id))
                    .Take(top)
                    .ToList();

                foreach (var id in affected)
                {
                    var info = await storage.GetMethodInfoAsync(id, cancellationToken);
                    var ep = entryPoints.Contains(id) ? " [entry]" : "";
                    var d = depthMap.GetValueOrDefault(id);
                    Console.WriteLine($"<- d{d} {info?.FullName ?? id}{ep}");
                }
                if (visited.Count - 1 > top)
                    Console.WriteLine($"(+{visited.Count - 1 - top} more)");
            }
            else // table
            {
                Console.WriteLine($"Impact analysis for: {targetInfo?.FullName ?? targetId}");
                Console.WriteLine(new string('-', 60));

                // Print tree by depth level
                var byDepth = visited.Where(id => id != targetId)
                    .GroupBy(id => depthMap.GetValueOrDefault(id))
                    .OrderBy(g => g.Key);

                foreach (var group in byDepth)
                {
                    Console.WriteLine($"\n  Depth {group.Key} ({group.Count()} methods):");
                    foreach (var id in group.OrderBy(id => id))
                    {
                        var info = await storage.GetMethodInfoAsync(id, cancellationToken);
                        var ep = entryPoints.Contains(id) ? " [entry point]" : "";
                        Console.WriteLine($"    {info?.FullName ?? id}{ep}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Total: {visited.Count} methods affected, {entryPoints.Count} entry points");
            }

            // Check for protected zones in the blast radius
            var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(dbPath)) ?? ".";
            var zoneManager = ProtectedZoneManager.TryLoadFromProject(projectRoot);
            if (zoneManager.Zones.Count > 0)
            {
                var protectedInBlast = await zoneManager.FilterProtectedAsync(visited, storage, cancellationToken);
                if (protectedInBlast.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"[!] Protected zones affected ({protectedInBlast.Count}):");
                    foreach (var (protectedId, fullName, zone) in protectedInBlast.Take(10))
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
                    if (protectedInBlast.Count > 10)
                        Console.WriteLine($"  (+{protectedInBlast.Count - 10} more)");
                }
            }
        });

        return command;
    }
}
