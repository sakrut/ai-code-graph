using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class DuplicatesCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var topOption = new Option<int>("--top", "-t")
        {
            Description = "Number of results",
            DefaultValueFactory = _ => 20
        };

        var thresholdOption = new Option<float>("--threshold")
        {
            Description = "Minimum hybrid score",
            DefaultValueFactory = _ => 0.5f
        };

        var typeOption = new Option<string?>("--type")
        {
            Description = "Filter by clone type: Type1|Type2|Semantic"
        };

        var conceptOption = new Option<string?>("--concept")
        {
            Description = "Filter by intent cluster label"
        };

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

        var command = new Command("duplicates", "Show detected code clones")
        {
            topOption, thresholdOption, typeOption, conceptOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var top = parseResult.GetValue(topOption);
            var threshold = parseResult.GetValue(thresholdOption);
            var typeStr = parseResult.GetValue(typeOption);
            var concept = parseResult.GetValue(conceptOption);
            var format = parseResult.GetValue(formatOption) ?? "table";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            CloneType? typeFilter = typeStr != null ? Enum.Parse<CloneType>(typeStr, ignoreCase: true) : null;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var pairs = await storage.GetClonePairsAsync(threshold, typeFilter, concept, cancellationToken);
            pairs = pairs.Take(top).ToList();

            if (pairs.Count == 0)
            {
                Console.WriteLine("No clone pairs found.");
                return;
            }

            if (format == "json")
            {
                var cloneList = new List<object>();
                foreach (var p in pairs)
                {
                    var infoA = await storage.GetMethodInfoAsync(p.MethodIdA, cancellationToken);
                    var infoB = await storage.GetMethodInfoAsync(p.MethodIdB, cancellationToken);
                    cloneList.Add(new
                    {
                        methodA = infoA?.FullName ?? p.MethodIdA,
                        methodB = infoB?.FullName ?? p.MethodIdB,
                        locationA = infoA?.FilePath != null ? $"{infoA.Value.FilePath}:{infoA.Value.StartLine}" : (string?)null,
                        locationB = infoB?.FilePath != null ? $"{infoB.Value.FilePath}:{infoB.Value.StartLine}" : (string?)null,
                        structural = Math.Round(p.StructuralSimilarity, 4),
                        semantic = Math.Round(p.SemanticSimilarity, 4),
                        hybrid = Math.Round(p.HybridScore, 4),
                        type = p.Type.ToString()
                    });
                }
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    clones = cloneList,
                    metadata = new { total = pairs.Count, threshold, typeFilter = typeStr }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else
            {
                Console.WriteLine($"{"Type",-10} {"Hybrid",6} {"Struct",6} {"Seman",6}  Method / Location");
                Console.WriteLine(new string('-', 90));
                foreach (var p in pairs)
                {
                    var infoA = await storage.GetMethodInfoAsync(p.MethodIdA, cancellationToken);
                    var infoB = await storage.GetMethodInfoAsync(p.MethodIdB, cancellationToken);
                    var nameA = infoA?.Name ?? p.MethodIdA;
                    var locA = infoA?.FilePath != null ? $"{infoA.Value.FilePath}:{infoA.Value.StartLine}" : "";
                    var nameB = infoB?.Name ?? p.MethodIdB;
                    var locB = infoB?.FilePath != null ? $"{infoB.Value.FilePath}:{infoB.Value.StartLine}" : "";
                    Console.WriteLine($"{p.Type,-10} {p.HybridScore,6:F3} {p.StructuralSimilarity,6:F3} {p.SemanticSimilarity,6:F3}  {nameA}  {locA}");
                    Console.WriteLine($"{"",10} {"",6} {"",6} {"",6}  {nameB}  {locB}");
                }
            }
        });

        return command;
    }
}
