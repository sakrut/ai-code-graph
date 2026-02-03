using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class HotspotsCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var topOption = OutputOptions.CreateTopOption(20);
        var thresholdOption = new Option<int?>("--threshold")
        {
            Description = "Minimum complexity score"
        };
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var dbOption = OutputOptions.CreateDbOption();
        var sortOption = new Option<string>("--sort")
        {
            Description = "Sort by: complexity|blast-radius|risk",
            DefaultValueFactory = _ => "complexity"
        };

        var command = new Command("hotspots", "Show complexity hotspots")
        {
            topOption, thresholdOption, formatOption, dbOption, sortOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var top = parseResult.GetValue(topOption);
            var threshold = parseResult.GetValue(thresholdOption);
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var sortBy = parseResult.GetValue(sortOption) ?? "complexity";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var hotspots = await storage.GetHotspotsWithThresholdAsync(top, threshold, sortBy, cancellationToken);

            if (hotspots.Count == 0)
            {
                Console.WriteLine("No hotspots found.");
                return;
            }

            var showBlastRadius = sortBy == "blast-radius" || sortBy == "blast" || sortBy == "risk";

            if (OutputOptions.IsJson(format))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    items = hotspots.Select(h => new
                    {
                        methodId = h.FullName,
                        complexity = h.Complexity,
                        loc = h.Loc,
                        maxNesting = h.Nesting,
                        blastRadius = h.BlastRadius,
                        blastDepth = h.BlastDepth,
                        risk = ComputeRisk(h.Complexity, h.BlastRadius),
                        location = h.FilePath != null ? $"{h.FilePath}:{h.StartLine}" : null
                    }),
                    metadata = new { total = hotspots.Count, returned = hotspots.Count, threshold, top, sortBy }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                foreach (var h in hotspots)
                {
                    var location = h.FilePath != null ? $" {Path.GetFileName(h.FilePath)}:{h.StartLine}" : "";
                    var blastInfo = showBlastRadius ? $" Blast:{h.BlastRadius}" : "";
                    Console.WriteLine($"{h.FullName} CC:{h.Complexity} LOC:{h.Loc} Nest:{h.Nesting}{blastInfo}{location}");
                }
            }
            else if (OutputOptions.IsCsv(format))
            {
                Console.WriteLine("method,complexity,loc,nesting,blastRadius,blastDepth,risk,location");
                foreach (var h in hotspots)
                {
                    var location = h.FilePath != null ? $"{h.FilePath}:{h.StartLine}" : "";
                    var risk = ComputeRisk(h.Complexity, h.BlastRadius);
                    Console.WriteLine($"{OutputHelpers.CsvEscape(h.FullName)},{h.Complexity},{h.Loc},{h.Nesting},{h.BlastRadius},{h.BlastDepth},{risk:F1},{OutputHelpers.CsvEscape(location)}");
                }
            }
            else // table
            {
                var nameWidth = Math.Min(55, hotspots.Max(h => h.FullName.Length));
                if (showBlastRadius)
                {
                    Console.WriteLine($"{"Method".PadRight(nameWidth)}  {"CC",4}  {"Blast",5}  {"Risk",5}  Location");
                    Console.WriteLine(new string('-', nameWidth + 28));
                    foreach (var h in hotspots)
                    {
                        var name = h.FullName.Length > nameWidth ? h.FullName[..(nameWidth - 3)] + "..." : h.FullName;
                        var location = h.FilePath != null ? $"{Path.GetFileName(h.FilePath)}:{h.StartLine}" : "";
                        var risk = ComputeRisk(h.Complexity, h.BlastRadius);
                        Console.WriteLine($"{name.PadRight(nameWidth)}  {h.Complexity,4}  {h.BlastRadius,5}  {risk,5:F1}  {location}");
                    }
                }
                else
                {
                    Console.WriteLine($"{"Method".PadRight(nameWidth)}  {"CC",4}  {"LOC",4}  {"Nest",4}  Location");
                    Console.WriteLine(new string('-', nameWidth + 28));
                    foreach (var h in hotspots)
                    {
                        var name = h.FullName.Length > nameWidth ? h.FullName[..(nameWidth - 3)] + "..." : h.FullName;
                        var location = h.FilePath != null ? $"{Path.GetFileName(h.FilePath)}:{h.StartLine}" : "";
                        Console.WriteLine($"{name.PadRight(nameWidth)}  {h.Complexity,4}  {h.Loc,4}  {h.Nesting,4}  {location}");
                    }
                }
            }
        });

        return command;
    }

    private static double ComputeRisk(int complexity, int blastRadius)
    {
        return complexity * (1 + Math.Log(blastRadius + 1));
    }
}
