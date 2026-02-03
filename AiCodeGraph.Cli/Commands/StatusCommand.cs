using System.CommandLine;
using System.CommandLine.Parsing;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class StatusCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var dbOption = OutputOptions.CreateDbOption();

        var command = new Command("status", "Show database info and staleness check (alias: db-info)")
        {
            formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            // Read metadata
            var analyzedAtStr = await storage.GetMetadataAsync("analyzed_at", cancellationToken);
            var solutionPath = await storage.GetMetadataAsync("solution_path", cancellationToken);
            var toolVersion = await storage.GetMetadataAsync("tool_version", cancellationToken);
            var gitCommit = await storage.GetMetadataAsync("git_commit", cancellationToken);
            var embeddingEngine = await storage.GetMetadataAsync("embedding_engine", cancellationToken);

            DateTimeOffset? analyzedAt = null;
            if (analyzedAtStr != null && DateTimeOffset.TryParse(analyzedAtStr, out var parsed))
                analyzedAt = parsed;

            // Staleness check
            var stalenessResult = CheckStaleness(analyzedAt, gitCommit, solutionPath);

            if (OutputOptions.IsJson(format))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    database = dbPath,
                    analyzedAt = analyzedAtStr,
                    solutionPath,
                    toolVersion,
                    gitCommit,
                    embeddingEngine,
                    staleness = new
                    {
                        isStale = stalenessResult.IsStale,
                        reason = stalenessResult.Reason,
                        confidence = stalenessResult.Confidence
                    }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                Console.WriteLine(json);
            }
            else if (OutputOptions.IsCompact(format))
            {
                Console.WriteLine($"DB: {dbPath}");
                if (analyzedAt != null)
                {
                    var age = DateTimeOffset.UtcNow - analyzedAt.Value;
                    Console.WriteLine($"Analyzed: {analyzedAt.Value:yyyy-MM-dd HH:mm} UTC ({OutputHelpers.FormatAge(age)})");
                }
                else
                {
                    Console.WriteLine("Analyzed: unknown");
                }
                if (!string.IsNullOrEmpty(solutionPath))
                    Console.WriteLine($"Solution: {solutionPath}");
                if (!string.IsNullOrEmpty(gitCommit))
                    Console.WriteLine($"Commit: {gitCommit[..Math.Min(7, gitCommit.Length)]}");
                if (!string.IsNullOrEmpty(toolVersion))
                    Console.WriteLine($"Version: {toolVersion}");

                // Staleness indicator
                if (stalenessResult.IsStale)
                    Console.WriteLine($"Status: STALE ({stalenessResult.Reason})");
                else
                    Console.WriteLine($"Status: OK ({stalenessResult.Reason})");
            }
            else // table
            {
                Console.WriteLine("Database Information");
                Console.WriteLine(new string('-', 50));
                Console.WriteLine($"  Path:      {dbPath}");
                Console.WriteLine($"  Analyzed:  {analyzedAtStr ?? "unknown"}");
                Console.WriteLine($"  Solution:  {solutionPath ?? "unknown"}");
                Console.WriteLine($"  Commit:    {gitCommit ?? "unknown"}");
                Console.WriteLine($"  Version:   {toolVersion ?? "unknown"}");
                Console.WriteLine($"  Embedding: {embeddingEngine ?? "unknown"}");
                Console.WriteLine();

                if (stalenessResult.IsStale)
                {
                    Console.WriteLine($"⚠ STALE: {stalenessResult.Reason}");
                    Console.WriteLine("  Run 'ai-code-graph analyze' to update.");
                }
                else
                {
                    Console.WriteLine($"✓ {stalenessResult.Reason}");
                }
            }
        });

        return command;
    }

    private static (bool IsStale, string Reason, string Confidence) CheckStaleness(
        DateTimeOffset? analyzedAt,
        string? storedCommit,
        string? solutionPath)
    {
        // No metadata = definitely stale (or very old db)
        if (analyzedAt == null)
            return (true, "No analysis timestamp found", "high");

        // Check git commit if available
        var currentCommit = GitHelpers.GetCurrentCommitHash();
        if (!string.IsNullOrEmpty(storedCommit) && !string.IsNullOrEmpty(currentCommit))
        {
            if (storedCommit != currentCommit)
                return (true, $"Git HEAD changed ({storedCommit[..7]} → {currentCommit[..7]})", "high");
        }

        // Check last modified time of .cs files
        var lastCsModified = GitHelpers.GetLastModifiedTime("*.cs");
        if (lastCsModified != null && lastCsModified > analyzedAt)
            return (true, "Source files modified after analysis", "medium");

        // Check if solution file was modified
        if (!string.IsNullOrEmpty(solutionPath) && File.Exists(solutionPath))
        {
            var slnModified = File.GetLastWriteTimeUtc(solutionPath);
            if (slnModified > analyzedAt.Value.UtcDateTime)
                return (true, "Solution file modified after analysis", "medium");
        }

        // Age-based heuristic
        var age = DateTimeOffset.UtcNow - analyzedAt.Value;
        if (age.TotalDays > 7)
            return (false, $"Analysis is {(int)age.TotalDays} days old (consider re-analyzing)", "low");

        return (false, "Analysis appears current", "high");
    }
}
