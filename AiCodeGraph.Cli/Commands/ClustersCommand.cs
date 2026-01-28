using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class ClustersCommand : ICommandHandler
{
    public Command BuildCommand()
    {
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

        var command = new Command("clusters", "Show intent clusters")
        {
            formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = parseResult.GetValue(formatOption) ?? "table";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var clusters = await storage.GetClustersAsync(cancellationToken);

            if (clusters.Count == 0)
            {
                Console.WriteLine("No clusters found.");
                return;
            }

            if (format == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    clusters = clusters.Select(c => new
                    {
                        id = c.Id,
                        label = c.Label,
                        description = c.Description,
                        cohesion = Math.Round(c.Cohesion, 4),
                        members = c.MethodIds
                    })
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else
            {
                foreach (var cluster in clusters)
                {
                    Console.WriteLine($"[{cluster.Id}] {cluster.Label} (cohesion: {cluster.Cohesion:F2}, members: {cluster.MethodIds.Count})");
                    foreach (var methodId in cluster.MethodIds.Take(5))
                    {
                        var info = await storage.GetMethodInfoAsync(methodId, cancellationToken);
                        Console.WriteLine($"    {info?.FullName ?? methodId}");
                    }
                    if (cluster.MethodIds.Count > 5)
                        Console.WriteLine($"    ... and {cluster.MethodIds.Count - 5} more");
                    Console.WriteLine();
                }
            }
        });

        return command;
    }
}
