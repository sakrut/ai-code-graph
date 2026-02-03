using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Cli.Helpers;

namespace AiCodeGraph.Cli.Commands;

public class ContextCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var methodArgument = new Argument<string?>("method")
        {
            Description = "Method name or pattern",
            Arity = ArgumentArity.ZeroOrOne
        };

        var idOption = OutputOptions.CreateMethodIdOption();
        var formatOption = OutputOptions.CreateFormatOption(OutputFormat.Compact);
        var dbOption = OutputOptions.CreateDbOption();

        var command = new Command("context", "Get compact method context (complexity, callers, callees, cluster, duplicates)")
        {
            methodArgument, idOption, formatOption, dbOption
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var method = parseResult.GetValue(methodArgument);
            var methodId = parseResult.GetValue(idOption);
            var format = parseResult.GetValue(formatOption) ?? "compact";
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";

            if (!CommandHelpers.ValidateDatabase(dbPath)) return;

            await using var storage = new StorageService(dbPath);
            await storage.OpenAsync(cancellationToken);

            var targetId = await MethodResolver.ResolveAsync(storage, methodId, method, cancellationToken);
            if (targetId == null) return;

            var info = await storage.GetMethodInfoAsync(targetId, cancellationToken);
            if (info == null) return;

            // Method identity (include ID for agent copy-paste)
            Console.WriteLine($"Method: {info.Value.FullName}");
            Console.WriteLine($"Id: {targetId}");
            if (info.Value.FilePath != null)
                Console.WriteLine($"File: {info.Value.FilePath}:{info.Value.StartLine}");

            // Metrics
            var metrics = await storage.GetMethodMetricsAsync(targetId, cancellationToken);
            if (metrics != null)
            {
                Console.WriteLine($"Complexity: CC={metrics.Value.CognitiveComplexity} LOC={metrics.Value.LinesOfCode} Nesting={metrics.Value.NestingDepth}");

                // Blast radius
                if (metrics.Value.BlastRadius > 0)
                {
                    var risk = metrics.Value.CognitiveComplexity * (1 + Math.Log(metrics.Value.BlastRadius + 1));
                    Console.WriteLine($"Blast Radius: {metrics.Value.BlastRadius} callers (depth: {metrics.Value.BlastDepth}, risk: {risk:F1})");
                }
            }

            // Callers
            var callers = await storage.GetCallersAsync(targetId, cancellationToken);
            if (callers.Count > 0)
            {
                var callerNames = new List<string>();
                foreach (var cid in callers.Take(5))
                {
                    var ci = await storage.GetMethodInfoAsync(cid, cancellationToken);
                    callerNames.Add(ci?.Name ?? cid);
                }
                var suffix = callers.Count > 5 ? $" (+{callers.Count - 5} more)" : "";
                Console.WriteLine($"Callers ({callers.Count}): {string.Join(", ", callerNames)}{suffix}");
            }

            // Callees
            var callees = await storage.GetCalleesAsync(targetId, cancellationToken);
            if (callees.Count > 0)
            {
                var calleeNames = new List<string>();
                foreach (var cid in callees.Take(5))
                {
                    var ci = await storage.GetMethodInfoAsync(cid, cancellationToken);
                    calleeNames.Add(ci?.Name ?? cid);
                }
                var suffix = callees.Count > 5 ? $" (+{callees.Count - 5} more)" : "";
                Console.WriteLine($"Callees ({callees.Count}): {string.Join(", ", calleeNames)}{suffix}");
            }

            // Cluster
            var cluster = await storage.GetMethodClusterAsync(targetId, cancellationToken);
            if (cluster != null)
                Console.WriteLine($"Cluster: \"{cluster.Value.Label}\" ({cluster.Value.MemberCount} members, cohesion: {cluster.Value.Cohesion:F2})");

            // Recent cluster activity
            if (cluster != null)
            {
                var clusters = await storage.GetClustersAsync(cancellationToken);
                var myCluster = clusters.FirstOrDefault(c => c.MethodIds.Contains(targetId));
                if (myCluster != null && myCluster.MethodIds.Count > 1)
                {
                    var recentChanges = new List<(string MethodName, TimeSpan Age)>();
                    foreach (var memberId in myCluster.MethodIds.Where(id => id != targetId).Take(10))
                    {
                        var memberInfo = await storage.GetMethodInfoAsync(memberId, cancellationToken);
                        if (memberInfo?.FilePath == null || !File.Exists(memberInfo.Value.FilePath)) continue;
                        try
                        {
                            var psi = new ProcessStartInfo("git", $"log -1 --format=%ct -- \"{memberInfo.Value.FilePath}\"")
                            {
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var process = Process.Start(psi);
                            if (process != null)
                            {
                                var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
                                await process.WaitForExitAsync(cancellationToken);
                                if (long.TryParse(output, out var ts))
                                {
                                    var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts);
                                    recentChanges.Add((memberInfo.Value.Name, age));
                                }
                            }
                        }
                        catch { /* skip on git errors */ }
                    }
                    if (recentChanges.Count > 0)
                    {
                        var top3 = recentChanges.OrderBy(r => r.Age).Take(3);
                        var formatted = string.Join(", ", top3.Select(r => $"{r.MethodName} ({OutputHelpers.FormatAge(r.Age)})"));
                        Console.WriteLine($"Recent cluster activity: {formatted}");
                    }
                }
            }

            // Duplicates
            var dupes = await storage.GetMethodDuplicatesAsync(targetId, cancellationToken);
            if (dupes.Count > 0)
            {
                var dupeStrs = dupes.Take(3).Select(d =>
                {
                    var name = d.OtherFullName;
                    // Extract Type.Method from full qualified name
                    var parenIdx = name.IndexOf('(');
                    var nameWithoutParams = parenIdx >= 0 ? name[..parenIdx] : name;
                    var parts = nameWithoutParams.Split('.');
                    var shortName = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
                    return $"{shortName} ({d.HybridScore:F2})";
                });
                var suffix = dupes.Count > 3 ? $" (+{dupes.Count - 3} more)" : "";
                Console.WriteLine($"Duplicates ({dupes.Count}): {string.Join(", ", dupeStrs)}{suffix}");
            }

            // Test coverage
            var methodShortName = info.Value.Name;
            var testMatches = await storage.SearchMethodsAsync($"%{methodShortName}%Test%", cancellationToken);
            var testMatches2 = await storage.SearchMethodsAsync($"%Test%{methodShortName}%", cancellationToken);
            var allTests = testMatches.Concat(testMatches2)
                .DistinctBy(t => t.Id)
                .Where(t => t.FullName.Contains("Test", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (allTests.Count > 0)
            {
                var testNames = allTests.Take(5).Select(t =>
                {
                    var parenIdx = t.FullName.IndexOf('(');
                    var nameOnly = parenIdx >= 0 ? t.FullName[..parenIdx] : t.FullName;
                    var parts = nameOnly.Split('.');
                    return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : parts[^1];
                });
                var suffix = allTests.Count > 5 ? $" (+{allTests.Count - 5} more)" : "";
                Console.WriteLine($"Tests ({allTests.Count}): {string.Join(", ", testNames)}{suffix}");
            }
            else
            {
                Console.WriteLine("Tests: none found");
            }

            // Source snippet
            if (info.Value.FilePath != null && info.Value.StartLine > 0 && File.Exists(info.Value.FilePath))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(info.Value.FilePath, cancellationToken);
                    var startIdx = info.Value.StartLine - 1;
                    var endIdx = Math.Min(lines.Length, startIdx + 20);

                    if (startIdx < lines.Length)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Source (first 20 lines):");
                        for (int i = startIdx; i < endIdx; i++)
                            Console.WriteLine($"  {lines[i]}");
                    }
                }
                catch (IOException) { }
            }

            // Git blame
            if (info.Value.FilePath != null && info.Value.StartLine > 0)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"blame -L {info.Value.StartLine},+1 --porcelain \"{info.Value.FilePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
                        await proc.WaitForExitAsync(cancellationToken);

                        if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                        {
                            string? author = null;
                            long? timestamp = null;
                            foreach (var line in output.Split('\n'))
                            {
                                if (line.StartsWith("author "))
                                    author = line["author ".Length..];
                                else if (line.StartsWith("author-time "))
                                    if (long.TryParse(line["author-time ".Length..], out var ts))
                                        timestamp = ts;
                            }
                            if (author != null && timestamp != null)
                            {
                                var date = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).LocalDateTime;
                                Console.WriteLine($"Last modified: {author} on {date:yyyy-MM-dd}");
                            }
                        }
                    }
                }
                catch (Exception) { }
            }
        });

        return command;
    }
}
