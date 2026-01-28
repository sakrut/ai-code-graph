using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class DiffCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var fromOption = new Option<string>("--from")
        {
            Description = "Base git ref",
            DefaultValueFactory = _ => "HEAD~1"
        };

        var toOption = new Option<string>("--to")
        {
            Description = "Target git ref",
            DefaultValueFactory = _ => "HEAD"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "summary|detail|json",
            DefaultValueFactory = _ => "summary"
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("diff", "Compare code graphs between git refs")
        {
            fromOption, toOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var fromRef = parseResult.GetValue(fromOption) ?? "HEAD~1";
            var toRef = parseResult.GetValue(toOption) ?? "HEAD";
            var format = parseResult.GetValue(formatOption) ?? "summary";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            // Get changed .cs files between refs
            var changedFiles = await GitHelpers.GetChangedCsFiles(fromRef, toRef, cancellationToken);
            if (changedFiles.Count == 0)
            {
                Console.WriteLine($"No C# files changed between {fromRef}..{toRef}.");
                return;
            }

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var allMethods = await storage.GetMethodsForExportAsync(null, cancellationToken);
            var changedFileSet = changedFiles.Select(f => Path.GetFullPath(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var affectedMethods = allMethods
                .Where(m => m.FilePath != null && changedFileSet.Contains(Path.GetFullPath(m.FilePath)))
                .ToList();

            // Also try matching by filename only (for cases where paths differ)
            if (affectedMethods.Count == 0)
            {
                var changedFileNames = changedFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                affectedMethods = allMethods
                    .Where(m => m.FilePath != null && changedFileNames.Contains(Path.GetFileName(m.FilePath)))
                    .ToList();
            }

            if (format == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    from = fromRef,
                    to = toRef,
                    filesChanged = changedFiles.Count,
                    methodsAffected = affectedMethods.Count,
                    files = changedFiles,
                    methods = affectedMethods.Select(m => new
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
                Console.WriteLine($"Changes between {fromRef}..{toRef}:\n");
                Console.WriteLine($"Files changed: {changedFiles.Count}");
                Console.WriteLine($"Methods affected: {affectedMethods.Count}");

                if (format == "detail" && affectedMethods.Count > 0)
                {
                    Console.WriteLine($"\n{"Method",-50} {"File",-25} {"CC",4}");
                    Console.WriteLine(new string('-', 83));
                    foreach (var m in affectedMethods.OrderByDescending(m => m.Complexity))
                    {
                        var name = m.FullName.Length > 48 ? m.FullName[..45] + "..." : m.FullName;
                        var file = m.FilePath != null ? Path.GetFileName(m.FilePath) : "";
                        Console.WriteLine($"{name,-50} {file,-25} {m.Complexity,4}");
                    }
                }
                else if (affectedMethods.Count > 0)
                {
                    var highComplexity = affectedMethods.Where(m => m.Complexity > 10).ToList();
                    if (highComplexity.Count > 0)
                    {
                        Console.WriteLine($"\nHigh-complexity methods in changed files ({highComplexity.Count}):");
                        foreach (var m in highComplexity.OrderByDescending(m => m.Complexity).Take(10))
                            Console.WriteLine($"  {m.FullName} (CC={m.Complexity})");
                    }
                }
            }
        });

        return command;
    }
}
