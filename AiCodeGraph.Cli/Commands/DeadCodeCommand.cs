using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class DeadCodeCommand : ICommandHandler
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

        var includeOverridesOption = new Option<bool>("--include-overrides")
        {
            Description = "Include override/abstract methods"
        };

        var command = new Command("dead-code", "Find methods with no callers (potential dead code)")
        {
            formatOption, dbOption, includeOverridesOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = parseResult.GetValue(formatOption) ?? "table";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var includeOverrides = parseResult.GetValue(includeOverridesOption);

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var deadCode = await storage.GetDeadCodeAsync(includeOverrides, cancellationToken);

            if (deadCode.Count == 0)
            {
                Console.WriteLine("No dead code detected.");
                return;
            }

            if (format == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    count = deadCode.Count,
                    methods = deadCode.Select(m => new
                    {
                        id = m.Id,
                        name = m.FullName,
                        file = m.FilePath,
                        line = m.StartLine,
                        complexity = m.Complexity
                    })
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"{"Method",-60} {"File",-30} {"CC",4}");
                Console.WriteLine(new string('-', 96));
                foreach (var m in deadCode)
                {
                    var file = m.FilePath != null ? $"{Path.GetFileName(m.FilePath)}:{m.StartLine}" : "";
                    var name = m.FullName.Length > 58 ? m.FullName[..55] + "..." : m.FullName;
                    Console.WriteLine($"{name,-60} {file,-30} {m.Complexity,4}");
                }
                Console.WriteLine($"\nTotal: {deadCode.Count} potentially unreachable methods");
            }
        });

        return command;
    }
}
