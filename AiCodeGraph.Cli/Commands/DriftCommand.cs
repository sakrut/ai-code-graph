using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Drift;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class DriftCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var vsOption = new Option<string>("--vs")
        {
            Description = "Baseline path or 'baseline' keyword",
            DefaultValueFactory = _ => "baseline"
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "summary|detail|json",
            DefaultValueFactory = _ => "summary"
        };

        var complexityPctOption = new Option<double>("--complexity-pct")
        {
            Description = "Complexity percentage threshold",
            DefaultValueFactory = _ => 0.25
        };

        var complexityAbsOption = new Option<int>("--complexity-abs")
        {
            Description = "Complexity absolute threshold",
            DefaultValueFactory = _ => 15
        };

        var dbOption = new Option<string>("--db")
        {
            Description = "Path to current graph.db",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("drift", "Detect architectural drift")
        {
            vsOption, formatOption, complexityPctOption, complexityAbsOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var vs = parseResult.GetValue(vsOption) ?? "baseline";
            var format = parseResult.GetValue(formatOption) ?? "summary";
            var complexityPct = parseResult.GetValue(complexityPctOption);
            var complexityAbs = parseResult.GetValue(complexityAbsOption);
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            var baselinePath = vs == "baseline"
                ? Path.Combine(Path.GetDirectoryName(dbPath) ?? ".", "baseline.db")
                : vs;

            if (!File.Exists(baselinePath))
            {
                Console.Error.WriteLine($"Error: Baseline not found at {baselinePath}. Run 'analyze --save-baseline' first.");
                Environment.ExitCode = 1;
                return;
            }

            var options = new DriftDetectorOptions
            {
                ComplexityPercentageThreshold = complexityPct,
                ComplexityAbsoluteThreshold = complexityAbs
            };

            var detector = new DriftDetector(options);
            var report = await detector.CompareAsync(dbPath, baselinePath, cancellationToken);

            var hasDrift = report.NewMethods.Count > 0 || report.RemovedMethods.Count > 0
                || report.Regressions.Count > 0 || report.NewDuplicates.Count > 0
                || report.IntentScattering.Count > 0;

            if (format == "json")
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    newMethods = report.NewMethods.Select(m => new { m.MethodId, m.FullName, m.Namespace, m.FilePath }),
                    removedMethods = report.RemovedMethods.Select(m => new { m.MethodId, m.FullName, m.Namespace, m.FilePath }),
                    regressions = report.Regressions.Select(r => new { r.MethodId, r.FullName, r.BaselineComplexity, r.CurrentComplexity, r.PercentageIncrease, r.CrossedAbsoluteThreshold }),
                    newDuplicates = report.NewDuplicates.Select(d => new { d.MethodIdA, d.MethodIdB, d.HybridScore, type = d.Type.ToString() }),
                    intentScattering = report.IntentScattering.Select(s => new { s.ClusterLabel, s.BaselineNamespaces, s.NewNamespaces, s.NewMemberMethods, s.TotalMemberCount }),
                    hasDrift
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (format == "detail")
            {
                PrintDetailedReport(report, hasDrift);
            }
            else // summary
            {
                PrintSummaryReport(report, hasDrift);
            }

            Environment.ExitCode = hasDrift ? 1 : 0;
        });

        return command;
    }

    private static void PrintDetailedReport(DriftReport report, bool hasDrift)
    {
        if (report.NewMethods.Count > 0)
        {
            Console.WriteLine($"New Methods ({report.NewMethods.Count}):");
            foreach (var m in report.NewMethods)
                Console.WriteLine($"  + {m.FullName}  [{m.Namespace}]");
            Console.WriteLine();
        }

        if (report.RemovedMethods.Count > 0)
        {
            Console.WriteLine($"Removed Methods ({report.RemovedMethods.Count}):");
            foreach (var m in report.RemovedMethods)
                Console.WriteLine($"  - {m.FullName}  [{m.Namespace}]");
            Console.WriteLine();
        }

        if (report.Regressions.Count > 0)
        {
            Console.WriteLine($"Complexity Regressions ({report.Regressions.Count}):");
            foreach (var r in report.Regressions)
            {
                var pct = (r.PercentageIncrease * 100).ToString("F0");
                var threshold = r.CrossedAbsoluteThreshold ? " [CROSSED THRESHOLD]" : "";
                Console.WriteLine($"  {r.FullName}: {r.BaselineComplexity} -> {r.CurrentComplexity} (+{pct}%){threshold}");
            }
            Console.WriteLine();
        }

        if (report.NewDuplicates.Count > 0)
        {
            Console.WriteLine($"New Duplicates ({report.NewDuplicates.Count}):");
            foreach (var d in report.NewDuplicates)
                Console.WriteLine($"  {d.MethodIdA} <-> {d.MethodIdB} (score: {d.HybridScore:F3})");
            Console.WriteLine();
        }

        if (report.IntentScattering.Count > 0)
        {
            Console.WriteLine($"Intent Scattering ({report.IntentScattering.Count}):");
            foreach (var s in report.IntentScattering)
            {
                Console.WriteLine($"  Cluster '{s.ClusterLabel}' spread to: {string.Join(", ", s.NewNamespaces)}");
                Console.WriteLine($"    New members: {string.Join(", ", s.NewMemberMethods.Take(5))}");
            }
            Console.WriteLine();
        }

        if (!hasDrift)
            Console.WriteLine("No drift detected.");
    }

    private static void PrintSummaryReport(DriftReport report, bool hasDrift)
    {
        if (!hasDrift)
        {
            Console.WriteLine("No drift detected.");
        }
        else
        {
            var parts = new List<string>();
            if (report.NewMethods.Count > 0)
                parts.Add($"{report.NewMethods.Count} new method(s)");
            if (report.RemovedMethods.Count > 0)
                parts.Add($"{report.RemovedMethods.Count} removed method(s)");
            if (report.Regressions.Count > 0)
                parts.Add($"{report.Regressions.Count} complexity regression(s)");
            if (report.NewDuplicates.Count > 0)
                parts.Add($"{report.NewDuplicates.Count} new duplicate(s)");
            if (report.IntentScattering.Count > 0)
                parts.Add($"{report.IntentScattering.Count} scattering alert(s)");

            Console.WriteLine($"Drift detected: {string.Join(", ", parts)}");
        }
    }
}
