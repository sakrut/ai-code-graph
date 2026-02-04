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

        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var topOption = OutputOptions.CreateTopOption(20);
        var dbOption = OutputOptions.CreateDbOption();

        var command = new Command("coupling", "Show afferent/efferent coupling and instability metrics")
        {
            levelOption, formatOption, topOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var level = parseResult.GetValue(levelOption) ?? "namespace";
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var top = parseResult.GetValue(topOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var analyzer = new CouplingAnalyzer();
            var results = await analyzer.AnalyzeAsync(storage, level, cancellationToken);
            var total = results.Count;
            results = results.Take(top).ToList();

            if (results.Count == 0)
            {
                Console.WriteLine("No coupling data found.");
                return;
            }

            if (OutputOptions.IsJson(format))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    items = results.Select(r => new
                    {
                        name = r.Name,
                        afferentCoupling = r.AfferentCoupling,
                        efferentCoupling = r.EfferentCoupling,
                        instability = Math.Round(r.Instability, 4),
                        abstractness = Math.Round(r.Abstractness, 4),
                        distanceFromMain = Math.Round(r.DistanceFromMain, 4)
                    }),
                    metadata = new { level, total, returned = results.Count }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                foreach (var r in results)
                    Console.WriteLine($"{r.Name} Ca:{r.AfferentCoupling} Ce:{r.EfferentCoupling} I:{r.Instability:F2}");
                if (total > results.Count)
                    Console.WriteLine($"(+{total - results.Count} more)");
            }
            else if (OutputOptions.IsCsv(format))
            {
                Console.WriteLine("name,afferentCoupling,efferentCoupling,instability,abstractness,distanceFromMain");
                foreach (var r in results)
                    Console.WriteLine($"{OutputHelpers.CsvEscape(r.Name)},{r.AfferentCoupling},{r.EfferentCoupling},{r.Instability:F4},{r.Abstractness:F4},{r.DistanceFromMain:F4}");
            }
            else // table
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
