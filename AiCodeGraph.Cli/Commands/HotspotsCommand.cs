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

        var command = new Command("hotspots", "Show complexity hotspots")
        {
            topOption, thresholdOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var top = parseResult.GetValue(topOption);
            var threshold = parseResult.GetValue(thresholdOption);
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var hotspots = await storage.GetHotspotsWithThresholdAsync(top, threshold, cancellationToken);

            if (hotspots.Count == 0)
            {
                Console.WriteLine("No hotspots found.");
                return;
            }

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
                        location = h.FilePath != null ? $"{h.FilePath}:{h.StartLine}" : null
                    }),
                    metadata = new { total = hotspots.Count, returned = hotspots.Count, threshold, top }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                foreach (var h in hotspots)
                {
                    var location = h.FilePath != null ? $" {Path.GetFileName(h.FilePath)}:{h.StartLine}" : "";
                    Console.WriteLine($"{h.FullName} CC:{h.Complexity} LOC:{h.Loc} Nest:{h.Nesting}{location}");
                }
            }
            else if (OutputOptions.IsCsv(format))
            {
                Console.WriteLine("method,complexity,loc,nesting,location");
                foreach (var h in hotspots)
                {
                    var location = h.FilePath != null ? $"{h.FilePath}:{h.StartLine}" : "";
                    Console.WriteLine($"{OutputHelpers.CsvEscape(h.FullName)},{h.Complexity},{h.Loc},{h.Nesting},{OutputHelpers.CsvEscape(location)}");
                }
            }
            else // table
            {
                var nameWidth = Math.Min(60, hotspots.Max(h => h.FullName.Length));
                Console.WriteLine($"{"Method".PadRight(nameWidth)}  {"CC",4}  {"LOC",4}  {"Nest",4}  Location");
                Console.WriteLine(new string('-', nameWidth + 30));
                foreach (var h in hotspots)
                {
                    var name = h.FullName.Length > nameWidth ? h.FullName[..(nameWidth - 3)] + "..." : h.FullName;
                    var location = h.FilePath != null ? $"{Path.GetFileName(h.FilePath)}:{h.StartLine}" : "";
                    Console.WriteLine($"{name.PadRight(nameWidth)}  {h.Complexity,4}  {h.Loc,4}  {h.Nesting,4}  {location}");
                }
            }
        });

        return command;
    }
}
