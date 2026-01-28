using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class ExportCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var conceptOption = new Option<string?>("--concept")
        {
            Description = "Filter by concept/cluster label"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "json|csv",
            DefaultValueFactory = _ => "json"
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("export", "Export code graph data")
        {
            conceptOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var concept = parseResult.GetValue(conceptOption);
            var format = parseResult.GetValue(formatOption) ?? "json";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var methods = await storage.GetMethodsForExportAsync(concept, cancellationToken);
            if (methods.Count == 0)
            {
                Console.WriteLine("No methods found.");
                return;
            }

            var methodIds = methods.Select(m => m.Id).ToHashSet();
            var relationships = await storage.GetCallGraphForMethodsAsync(methodIds, cancellationToken);

            if (format == "csv")
            {
                Console.WriteLine("Id,FullName,ReturnType,FilePath,Line,Complexity,LOC,Nesting,ClusterLabel");
                foreach (var m in methods)
                {
                    var filePath = OutputHelpers.CsvEscape(m.FilePath ?? "");
                    var label = OutputHelpers.CsvEscape(m.ClusterLabel ?? "");
                    Console.WriteLine($"{OutputHelpers.CsvEscape(m.Id)},{OutputHelpers.CsvEscape(m.FullName)},{OutputHelpers.CsvEscape(m.ReturnType)},{filePath},{m.StartLine},{m.Complexity},{m.Loc},{m.Nesting},{label}");
                }
            }
            else
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    methods = methods.Select(m => new
                    {
                        id = m.Id,
                        fullName = m.FullName,
                        returnType = m.ReturnType,
                        filePath = m.FilePath,
                        line = m.StartLine,
                        complexity = m.Complexity,
                        loc = m.Loc,
                        nesting = m.Nesting,
                        cluster = m.ClusterLabel
                    }),
                    relationships = relationships.OrderBy(r => r.CallerId).ThenBy(r => r.CalleeId).Select(r => new
                    {
                        caller = r.CallerId,
                        callee = r.CalleeId
                    }),
                    metadata = new { methodCount = methods.Count, relationshipCount = relationships.Count, conceptFilter = concept }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
        });

        return command;
    }
}
