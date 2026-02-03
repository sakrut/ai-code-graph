using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class LayersCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var topOption = OutputOptions.CreateTopOption(50);
        var dbOption = OutputOptions.CreateDbOption();

        var layerOption = new Option<string?>("--layer", "-l")
        {
            Description = "Filter by layer (Presentation|Application|Domain|Infrastructure|Shared|Unknown)"
        };

        var minConfidenceOption = new Option<float>("--min-confidence")
        {
            Description = "Minimum confidence threshold",
            DefaultValueFactory = _ => 0.0f
        };

        var command = new Command("layers", "Display architectural layer assignments for types")
        {
            formatOption, topOption, dbOption, layerOption, minConfidenceOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var top = parseResult.GetValue(topOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var layerFilter = parseResult.GetValue(layerOption);
            var minConfidence = parseResult.GetValue(minConfidenceOption);

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var assignments = await storage.GetLayerAssignmentsAsync(cancellationToken);

            // Apply filters
            if (!string.IsNullOrEmpty(layerFilter))
            {
                if (Enum.TryParse<ArchitecturalLayer>(layerFilter, ignoreCase: true, out var layer))
                {
                    assignments = assignments.Where(a => a.Layer == layer).ToList();
                }
                else
                {
                    Console.Error.WriteLine($"Unknown layer: {layerFilter}. Valid values: Presentation, Application, Domain, Infrastructure, Shared, Unknown");
                    return;
                }
            }

            assignments = assignments
                .Where(a => a.Confidence >= minConfidence)
                .OrderBy(a => a.Layer)
                .ThenByDescending(a => a.Confidence)
                .ToList();

            var total = assignments.Count;
            assignments = assignments.Take(top).ToList();

            if (assignments.Count == 0)
            {
                Console.WriteLine("No layer assignments found.");
                return;
            }

            if (OutputOptions.IsJson(format))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    items = assignments.Select(a => new
                    {
                        typeId = a.TypeId,
                        layer = a.Layer.ToString(),
                        confidence = a.Confidence,
                        reason = a.Reason
                    }),
                    metadata = new { total, returned = assignments.Count }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                foreach (var a in assignments)
                {
                    var warning = a.Reason.Contains("WARNING") ? " [!]" : "";
                    Console.WriteLine($"{a.Layer,-14} {a.TypeId} ({a.Confidence:F2}){warning}");
                }
                if (total > assignments.Count)
                    Console.WriteLine($"(+{total - assignments.Count} more)");
            }
            else if (OutputOptions.IsCsv(format))
            {
                Console.WriteLine("layer,typeId,confidence,reason");
                foreach (var a in assignments)
                {
                    Console.WriteLine($"{a.Layer},{OutputHelpers.CsvEscape(a.TypeId)},{a.Confidence:F2},{OutputHelpers.CsvEscape(a.Reason)}");
                }
            }
            else // table
            {
                Console.WriteLine($"{"Layer",-14} {"Type",-50} {"Conf",6} {"Reason"}");
                Console.WriteLine(new string('-', 110));
                foreach (var a in assignments)
                {
                    var typeName = a.TypeId.Length > 48 ? a.TypeId[..45] + "..." : a.TypeId;
                    var reason = a.Reason.Length > 35 ? a.Reason[..32] + "..." : a.Reason;
                    Console.WriteLine($"{a.Layer,-14} {typeName,-50} {a.Confidence,6:F2} {reason}");
                }
                Console.WriteLine($"\nTotal: {total} types assigned to layers");
            }
        });

        return command;
    }
}
