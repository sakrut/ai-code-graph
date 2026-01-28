using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class ChurnCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var sinceOption = new Option<string>("--since")
        {
            Description = "Git log time range (e.g. '6 months ago')",
            DefaultValueFactory = _ => "6 months ago"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "table|json",
            DefaultValueFactory = _ => "table"
        };

        var topOption = new Option<int>("--top")
        {
            Description = "Number of results to show",
            DefaultValueFactory = _ => 20
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("churn", "Show methods with high change-frequency × complexity (churn hotspots)")
        {
            sinceOption, formatOption, topOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var since = parseResult.GetValue(sinceOption) ?? "6 months ago";
            var format = parseResult.GetValue(formatOption) ?? "table";
            var top = parseResult.GetValue(topOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var analyzer = new ChurnAnalyzer();
            var results = await analyzer.AnalyzeAsync(storage, since, top, cancellationToken);

            if (results.Count == 0)
            {
                Console.WriteLine("No churn hotspots found.");
                return;
            }

            if (format == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    since,
                    count = results.Count,
                    methods = results.Select(r => new
                    {
                        id = r.MethodId,
                        name = r.MethodName,
                        file = r.FilePath,
                        changes = r.Changes,
                        complexity = r.CognitiveComplexity,
                        churnScore = r.ChurnScore
                    })
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Churn hotspots (since: {since}):\n");
                Console.WriteLine($"{"Method",-50} {"File",-25} {"Chg",4} {"CC",4} {"Score",6}");
                Console.WriteLine(new string('-', 93));
                foreach (var r in results)
                {
                    var file = r.FilePath != null ? Path.GetFileName(r.FilePath) : "";
                    var name = r.MethodName.Length > 48 ? r.MethodName[..45] + "..." : r.MethodName;
                    Console.WriteLine($"{name,-50} {file,-25} {r.Changes,4} {r.CognitiveComplexity,4} {r.ChurnScore,6:F0}");
                }
                Console.WriteLine($"\nTotal: {results.Count} methods with churn score > 0");
            }
        });

        return command;
    }
}
