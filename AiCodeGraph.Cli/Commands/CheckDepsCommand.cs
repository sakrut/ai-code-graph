using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class CheckDepsCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var rulesOption = new Option<FileInfo?>("--rules")
        {
            Description = "Path to rules.json file (optional, uses defaults if not provided)"
        };

        var showRulesOption = new Option<bool>("--show-rules")
        {
            Description = "Show loaded rules and exit"
        };

        var sampleOption = new Option<bool>("--sample")
        {
            Description = "Generate sample rules.json and exit"
        };

        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var dbOption = OutputOptions.CreateDbOption();

        var command = new Command("check-deps", "Check for forbidden dependency violations")
        {
            rulesOption, showRulesOption, sampleOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var rulesFile = parseResult.GetValue(rulesOption);
            var showRules = parseResult.GetValue(showRulesOption);
            var sample = parseResult.GetValue(sampleOption);
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            // Generate sample rules.json
            if (sample)
            {
                Console.WriteLine(DependencyRuleEngine.GenerateSampleRulesJson());
                return;
            }

            // Load rules
            DependencyRuleEngine engine;
            if (rulesFile != null)
            {
                if (!rulesFile.Exists)
                {
                    Console.Error.WriteLine($"Error: Rules file not found: {rulesFile.FullName}");
                    return;
                }
                try
                {
                    engine = DependencyRuleEngine.LoadFromFile(rulesFile.FullName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error loading rules: {ex.Message}");
                    return;
                }
            }
            else
            {
                engine = DependencyRuleEngine.CreateWithDefaults();
            }

            // Show rules
            if (showRules)
            {
                Console.WriteLine($"Loaded {engine.Rules.Count} rules:");
                Console.WriteLine();
                foreach (var rule in engine.Rules)
                {
                    Console.WriteLine($"  [{rule.Type}] {rule.Name}");
                    Console.WriteLine($"    From: {rule.FromPattern}");
                    Console.WriteLine($"    To:   {rule.ToPattern}");
                    if (rule.Explanation != null)
                        Console.WriteLine($"    Why:  {rule.Explanation}");
                    Console.WriteLine();
                }
                return;
            }

            // Check database
            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            // Run checks
            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var result = await engine.CheckViolationsAsync(storage, cancellationToken);

            // Output results
            OutputResults(result, format);
        });

        return command;
    }

    private static void OutputResults(DependencyCheckResult result, string format)
    {
        if (OutputOptions.IsJson(format))
        {
            var json = JsonSerializer.Serialize(new
            {
                violations = result.Violations.Select(v => new
                {
                    rule = v.Rule.Name,
                    ruleType = v.Rule.Type.ToString().ToLower(),
                    severity = v.Rule.Severity.ToString().ToLower(),
                    from = v.FromFullName,
                    to = v.ToFullName,
                    location = v.FromFilePath != null ? $"{v.FromFilePath}:{v.FromLine}" : null,
                    explanation = v.Rule.Explanation
                }),
                statistics = new
                {
                    violationCount = result.Violations.Count,
                    callsChecked = result.TotalCallsChecked,
                    rulesApplied = result.RulesApplied,
                    elapsedMs = result.ElapsedTime.TotalMilliseconds
                }
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            Console.WriteLine(json);
        }
        else
        {
            // Group violations by rule
            var grouped = result.Violations
                .GroupBy(v => v.Rule.Name)
                .OrderByDescending(g => g.Count());

            if (!grouped.Any())
            {
                Console.WriteLine("✓ No dependency violations found.");
                Console.WriteLine();
                Console.WriteLine($"Checked {result.TotalCallsChecked} method calls against {result.RulesApplied} rules in {result.ElapsedTime.TotalMilliseconds:F0}ms");
                return;
            }

            var errorCount = result.Violations.Count(v => v.Rule.Severity == ViolationSeverity.Error);
            var warningCount = result.Violations.Count(v => v.Rule.Severity == ViolationSeverity.Warning);

            Console.WriteLine($"✗ Found {result.Violations.Count} dependency violations:");
            if (errorCount > 0)
                Console.WriteLine($"  {errorCount} error(s)");
            if (warningCount > 0)
                Console.WriteLine($"  {warningCount} warning(s)");
            Console.WriteLine();

            foreach (var group in grouped)
            {
                var rule = group.First().Rule;
                var severityMarker = rule.Severity == ViolationSeverity.Error ? "ERROR" : "WARNING";
                Console.WriteLine($"[{severityMarker}] {rule.Name} ({group.Count()} violations)");
                if (rule.Explanation != null)
                    Console.WriteLine($"  Why: {rule.Explanation}");
                Console.WriteLine();

                foreach (var violation in group.Take(10)) // Limit to 10 per rule for compact output
                {
                    var location = violation.FromFilePath != null
                        ? $" ({Path.GetFileName(violation.FromFilePath)}:{violation.FromLine})"
                        : "";
                    Console.WriteLine($"    {violation.FromFullName}{location}");
                    Console.WriteLine($"      → {violation.ToFullName}");
                }

                if (group.Count() > 10)
                    Console.WriteLine($"    ... and {group.Count() - 10} more violations");
                Console.WriteLine();
            }

            Console.WriteLine($"Checked {result.TotalCallsChecked} method calls against {result.RulesApplied} rules in {result.ElapsedTime.TotalMilliseconds:F0}ms");
        }
    }
}
