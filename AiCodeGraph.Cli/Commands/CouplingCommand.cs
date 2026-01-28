using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class CouplingCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var levelOption = new Option<string>("--level", "-l")
        {
            Description = "namespace|type",
            DefaultValueFactory = _ => "namespace"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "table|json",
            DefaultValueFactory = _ => "table"
        };

        var topOption = new Option<int>("--top", "-n")
        {
            Description = "Number of results",
            DefaultValueFactory = _ => 20
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("coupling", "Show afferent/efferent coupling and instability metrics")
        {
            levelOption, formatOption, topOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var level = parseResult.GetValue(levelOption) ?? "namespace";
            var format = parseResult.GetValue(formatOption) ?? "table";
            var top = parseResult.GetValue(topOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var analyzer = new CouplingAnalyzer();
            var results = await analyzer.AnalyzeAsync(storage, level, cancellationToken);
            results = results.Take(top).ToList();

            if (results.Count == 0)
            {
                Console.WriteLine("No coupling data found.");
                return;
            }

            if (format == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    level,
                    count = results.Count,
                    metrics = results.Select(r => new
                    {
                        name = r.Name,
                        afferentCoupling = r.AfferentCoupling,
                        efferentCoupling = r.EfferentCoupling,
                        instability = Math.Round(r.Instability, 4),
                        abstractness = Math.Round(r.Abstractness, 4),
                        distanceFromMain = Math.Round(r.DistanceFromMain, 4)
                    })
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"Coupling metrics (level: {level}):\n");
                Console.WriteLine($"{"Name",-45} {"Ca",4} {"Ce",4} {"I",5} {"A",5} {"D",5}");
                Console.WriteLine(new string('-', 72));
                foreach (var r in results)
                {
                    var name = r.Name.Length > 43 ? r.Name[..40] + "..." : r.Name;
                    Console.WriteLine($"{name,-45} {r.AfferentCoupling,4} {r.EfferentCoupling,4} {r.Instability,5:F2} {r.Abstractness,5:F2} {r.DistanceFromMain,5:F2}");
                }
                Console.WriteLine($"\nCa=Afferent Ce=Efferent I=Instability A=Abstractness D=Distance from Main Sequence");
            }
        });

        return command;
    }
}
