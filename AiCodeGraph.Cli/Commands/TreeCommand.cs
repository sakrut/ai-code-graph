using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class TreeCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var nsFilterOption = new Option<string?>("--namespace", "-n")
        {
            Description = "Filter by namespace prefix"
        };

        var typeFilterOption = new Option<string?>("--type")
        {
            Description = "Filter by type name"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "tree|json",
            DefaultValueFactory = _ => "tree"
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var includePrivateOption = new Option<bool>("--include-private")
        {
            Description = "Include non-public methods"
        };

        var skipTestsOption = new Option<bool>("--skip-tests")
        {
            Description = "Exclude *.Tests projects"
        };

        var skipInterfacesOption = new Option<bool>("--skip-interfaces")
        {
            Description = "Exclude interface types"
        };

        var skipNsOption = new Option<string?>("--skip-ns")
        {
            Description = "Exclude namespaces matching patterns (comma-separated)"
        };

        var maxMethodsOption = new Option<int?>("--max-methods")
        {
            Description = "Show first N methods per type, then '... (+X more)'"
        };

        var noReturnTypesOption = new Option<bool>("--no-return-types")
        {
            Description = "Omit return type signatures"
        };

        var compactOption = new Option<bool>("--compact")
        {
            Description = "Enable compact mode with sensible defaults for LLM context"
        };

        var command = new Command("tree", "Display code structure tree")
        {
            nsFilterOption, typeFilterOption, formatOption, dbOption, includePrivateOption,
            skipTestsOption, skipInterfacesOption, skipNsOption, maxMethodsOption, noReturnTypesOption, compactOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var nsFilter = parseResult.GetValue(nsFilterOption);
            var typeFilter = parseResult.GetValue(typeFilterOption);
            var format = parseResult.GetValue(formatOption) ?? "tree";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var includePrivate = parseResult.GetValue(includePrivateOption);
            var skipTests = parseResult.GetValue(skipTestsOption);
            var skipInterfaces = parseResult.GetValue(skipInterfacesOption);
            var skipNs = parseResult.GetValue(skipNsOption);
            var maxMethods = parseResult.GetValue(maxMethodsOption);
            var noReturnTypes = parseResult.GetValue(noReturnTypesOption);
            var compact = parseResult.GetValue(compactOption);

            // Apply compact mode defaults (can be overridden by explicit flags)
            if (compact)
            {
                skipTests = skipTests || true;
                skipInterfaces = skipInterfaces || true;
                skipNs ??= "Migrations,Models";
                maxMethods ??= 5;
                noReturnTypes = noReturnTypes || true;
            }

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var rows = await storage.GetTreeAsync(nsFilter, typeFilter, includePrivate, includeConstructors: false, skipTests, skipInterfaces, skipNs, cancellationToken);

            if (rows.Count == 0)
            {
                Console.WriteLine("No results found.");
                return;
            }

            if (format == "json")
            {
                PrintJsonTree(rows, compact, skipTests, skipInterfaces, skipNs, maxMethods, noReturnTypes);
            }
            else if (compact)
            {
                PrintCompactTree(rows, maxMethods);
            }
            else
            {
                PrintVerboseTree(rows, maxMethods, noReturnTypes);
            }
        });

        return command;
    }

    private static void PrintJsonTree(
        List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)> rows,
        bool compact,
        bool skipTests,
        bool skipInterfaces,
        string? skipNs,
        int? maxMethods,
        bool noReturnTypes)
    {
        var hierarchy = rows
            .GroupBy(r => r.ProjectName)
            .Select(pg => new
            {
                name = pg.Key,
                namespaces = pg.GroupBy(r => r.NamespaceName).OrderBy(g => g.Key).Select(ng => new
                {
                    name = ng.Key,
                    types = ng.GroupBy(r => (r.TypeName, r.TypeKind)).OrderBy(g => g.Key.TypeName).Select(tg =>
                    {
                        var methods = tg.OrderBy(r => r.MethodName).ToList();
                        var displayMethods = maxMethods.HasValue ? methods.Take(maxMethods.Value).ToList() : methods;
                        var truncated = maxMethods.HasValue && methods.Count > maxMethods.Value ? methods.Count - maxMethods.Value : 0;
                        return new
                        {
                            name = tg.Key.TypeName,
                            kind = tg.Key.TypeKind.ToLower(),
                            methods = displayMethods.Select(r => new
                            {
                                name = r.MethodName,
                                returnType = noReturnTypes ? null : r.ReturnType,
                                accessibility = r.Accessibility.ToLower()
                            }),
                            truncatedCount = truncated > 0 ? truncated : (int?)null
                        };
                    })
                })
            });

        // Build filter metadata - only include when non-default filters are active
        var hasActiveFilters = skipTests || skipInterfaces || !string.IsNullOrEmpty(skipNs) || maxMethods.HasValue || noReturnTypes;
        var excludedNsList = string.IsNullOrEmpty(skipNs)
            ? Array.Empty<string>()
            : skipNs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        object output;
        if (hasActiveFilters)
        {
            output = new
            {
                projects = hierarchy,
                compact = compact ? true : (bool?)null,
                filters = new
                {
                    skipTests = skipTests ? true : (bool?)null,
                    skipInterfaces = skipInterfaces ? true : (bool?)null,
                    excludedNamespaces = excludedNsList.Length > 0 ? excludedNsList : null,
                    maxMethodsPerType = maxMethods,
                    noReturnTypes = noReturnTypes ? true : (bool?)null
                }
            };
        }
        else
        {
            output = new { projects = hierarchy };
        }

        var json = System.Text.Json.JsonSerializer.Serialize(output,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        Console.WriteLine(json);
    }

    private static void PrintCompactTree(List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)> rows, int? maxMethods)
    {
        // Compact markdown-style output for LLM context
        var grouped = rows.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);
        foreach (var project in grouped)
        {
            Console.WriteLine($"# {project.Key}");
            var byNamespace = project.GroupBy(r => r.NamespaceName).OrderBy(g => g.Key);
            foreach (var ns in byNamespace)
            {
                var nsDisplay = ns.Key.Contains('.') ? ns.Key[(ns.Key.LastIndexOf('.') + 1)..] : ns.Key;
                Console.WriteLine($"\n## {nsDisplay}");
                var byType = ns.GroupBy(r => (r.TypeName, r.TypeKind)).OrderBy(g => g.Key.TypeName);
                foreach (var type in byType)
                {
                    var methods = type.OrderBy(r => r.MethodName).ToList();
                    var displayMethods = maxMethods.HasValue ? methods.Take(maxMethods.Value).ToList() : methods;
                    var remaining = methods.Count - displayMethods.Count;
                    var methodList = string.Join(", ", displayMethods.Select(m => m.MethodName));
                    var suffix = remaining > 0 ? $"... (+{remaining} more)" : "";
                    Console.WriteLine($"  {type.Key.TypeName}: {methodList}{suffix}");
                }
            }
            Console.WriteLine();
        }
    }

    private static void PrintVerboseTree(List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)> rows, int? maxMethods, bool noReturnTypes)
    {
        // Standard verbose tree output
        var lastProject = "";
        var lastNs = "";
        var lastType = "";
        var methodCount = 0;

        foreach (var row in rows)
        {
            if (row.ProjectName != lastProject)
            {
                Console.WriteLine(row.ProjectName);
                lastProject = row.ProjectName;
                lastNs = "";
                lastType = "";
            }
            if (row.NamespaceName != lastNs)
            {
                Console.WriteLine($"  {row.NamespaceName}");
                lastNs = row.NamespaceName;
                lastType = "";
            }
            if (row.TypeName != lastType)
            {
                var kindTag = row.TypeKind switch
                {
                    "Class" => "[C]",
                    "Interface" => "[I]",
                    "Record" => "[R]",
                    "Struct" => "[S]",
                    "Enum" => "[E]",
                    _ => "[?]"
                };
                Console.WriteLine($"    {kindTag} {row.TypeName}");
                lastType = row.TypeName;
                methodCount = 0;
            }
            methodCount++;
            if (maxMethods.HasValue && methodCount > maxMethods.Value)
            {
                if (methodCount == maxMethods.Value + 1)
                {
                    var remaining = rows.Count(r => r.ProjectName == row.ProjectName && r.NamespaceName == row.NamespaceName && r.TypeName == row.TypeName) - maxMethods.Value;
                    Console.WriteLine($"        ... (+{remaining} more)");
                }
                continue;
            }
            var returnType = noReturnTypes ? "" : $"{row.ReturnType} ";
            var visibilityTag = row.Accessibility != "Public" ? $" [{row.Accessibility.ToLower()}]" : "";
            Console.WriteLine($"        {returnType}{row.MethodName}(){visibilityTag}");
        }
    }
}
