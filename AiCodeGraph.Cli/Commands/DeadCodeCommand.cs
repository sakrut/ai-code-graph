using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class DeadCodeCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var topOption = OutputOptions.CreateTopOption(20);
        var dbOption = OutputOptions.CreateDbOption();

        var includeOverridesOption = new Option<bool>("--include-overrides")
        {
            Description = "Include override/abstract methods"
        };

        var command = new Command("dead-code", "Find methods with no callers (potential dead code)")
        {
            formatOption, topOption, dbOption, includeOverridesOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var top = parseResult.GetValue(topOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var includeOverrides = parseResult.GetValue(includeOverridesOption);

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var deadCode = await storage.GetDeadCodeAsync(includeOverrides, cancellationToken);
            var total = deadCode.Count;
            deadCode = deadCode.Take(top).ToList();

            if (deadCode.Count == 0)
            {
                Console.WriteLine("No dead code detected.");
                return;
            }

            if (OutputOptions.IsJson(format))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    items = deadCode.Select(m => new
                    {
                        methodId = m.FullName,
                        location = m.FilePath != null ? $"{m.FilePath}:{m.StartLine}" : null,
                        complexity = m.Complexity
                    }),
                    metadata = new { total, returned = deadCode.Count }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                foreach (var m in deadCode)
                {
                    var location = m.FilePath != null ? $" {Path.GetFileName(m.FilePath)}:{m.StartLine}" : "";
                    Console.WriteLine($"{m.FullName} - 0 callers{location}");
                }
                if (total > deadCode.Count)
                    Console.WriteLine($"(+{total - deadCode.Count} more)");
            }
            else if (OutputOptions.IsCsv(format))
            {
                Console.WriteLine("method,location,complexity");
                foreach (var m in deadCode)
                {
                    var location = m.FilePath != null ? $"{m.FilePath}:{m.StartLine}" : "";
                    Console.WriteLine($"{OutputHelpers.CsvEscape(m.FullName)},{OutputHelpers.CsvEscape(location)},{m.Complexity}");
                }
            }
            else // table
            {
                Console.WriteLine($"{"Method",-60} {"File",-30} {"CC",4}");
                Console.WriteLine(new string('-', 96));
                foreach (var m in deadCode)
                {
                    var file = m.FilePath != null ? $"{Path.GetFileName(m.FilePath)}:{m.StartLine}" : "";
                    var name = m.FullName.Length > 58 ? m.FullName[..55] + "..." : m.FullName;
                    Console.WriteLine($"{name,-60} {file,-30} {m.Complexity,4}");
                }
                Console.WriteLine($"\nTotal: {total} potentially unreachable methods");
            }
        });

        return command;
    }
}
