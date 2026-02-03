using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using AiCodeGraph.Core.Architecture;
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

            // Extract type ID from method full name
            var parenIdx = info.Value.FullName.IndexOf('(');
            var nameWithoutParams = parenIdx >= 0 ? info.Value.FullName[..parenIdx] : info.Value.FullName;
            var parts = nameWithoutParams.Split('.');
            var typeId = parts.Length >= 2 ? string.Join(".", parts[..^1]) : null;

            // Layer assignment
            LayerAssignment? layerAssignment = null;
            if (typeId != null)
            {
                layerAssignment = await storage.GetLayerForTypeAsync(typeId, cancellationToken);
                if (layerAssignment != null)
                {
                    Console.WriteLine($"Layer: {layerAssignment.Layer} (confidence: {layerAssignment.Confidence:F2})");
                }
            }

            // Check protected zone
            var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(dbPath)) ?? ".";
            var zoneManager = ProtectedZoneManager.TryLoadFromProject(projectRoot);
            var protection = zoneManager.CheckProtection(info.Value.FullName);
            if (protection.IsProtected)
                Console.WriteLine($"Protection: {protection.Zone?.Level} - {protection.Zone?.Reason}");
            else
                Console.WriteLine("Protection: None");

            // Metrics
            var metrics = await storage.GetMethodMetricsAsync(targetId, cancellationToken);
            if (metrics != null)
            {
                Console.WriteLine($"Complexity: CC={metrics.Value.CognitiveComplexity} LOC={metrics.Value.LinesOfCode} Nesting={metrics.Value.NestingDepth}");

                // Blast radius with entry points
                if (metrics.Value.BlastRadius > 0)
                {
                    var risk = metrics.Value.CognitiveComplexity * (1 + Math.Log(metrics.Value.BlastRadius + 1));
                    var blastInfo = $"Blast Radius: {metrics.Value.BlastRadius} callers (depth: {metrics.Value.BlastDepth}, risk: {risk:F1})";

                    // Find entry points (callers with no callers)
                    var entryPoints = new List<string>();
                    var visited = new HashSet<string> { targetId };
                    var queue = new Queue<string>();
                    queue.Enqueue(targetId);

                    while (queue.Count > 0 && visited.Count < 200) // Limit traversal
                    {
                        var current = queue.Dequeue();
                        var currentCallers = await storage.GetCallersAsync(current, cancellationToken);

                        if (currentCallers.Count == 0 && current != targetId)
                            entryPoints.Add(current);

                        foreach (var callerId in currentCallers)
                        {
                            if (visited.Add(callerId))
                                queue.Enqueue(callerId);
                        }
                    }

                    if (entryPoints.Count > 0)
                    {
                        var epNames = new List<string>();
                        foreach (var ep in entryPoints.Take(3))
                        {
                            var epInfo = await storage.GetMethodInfoAsync(ep, cancellationToken);
                            epNames.Add(epInfo?.Name ?? ep);
                        }
                        var epSuffix = entryPoints.Count > 3 ? $" (+{entryPoints.Count - 3} more)" : "";
                        blastInfo += $"\n  Entry points: {string.Join(", ", epNames)}{epSuffix}";
                    }

                    Console.WriteLine(blastInfo);
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

            // Architectural Notes
            var archNotes = new List<string>();

            // High blast radius warning
            if (metrics?.BlastRadius > 50)
                archNotes.Add($"⚠ High blast radius - changes affect {metrics.Value.BlastRadius} callers");
            else if (metrics?.BlastRadius > 20)
                archNotes.Add($"⚠ Moderate blast radius - changes affect {metrics.Value.BlastRadius} callers");

            // High complexity warning
            if (metrics?.CognitiveComplexity > 15)
                archNotes.Add($"⚠ High complexity (CC={metrics.Value.CognitiveComplexity}) - consider refactoring");

            // Protection zone
            if (protection.IsProtected && protection.Zone != null)
                archNotes.Add($"⚠ {protection.WarningMessage}");

            // Check for deprecated callees
            if (zoneManager.Zones.Count > 0)
            {
                foreach (var calleeId in callees.Take(20))
                {
                    var calleeInfo = await storage.GetMethodInfoAsync(calleeId, cancellationToken);
                    if (calleeInfo != null)
                    {
                        var calleeProtection = zoneManager.CheckProtection(calleeInfo.Value.FullName);
                        if (calleeProtection.IsProtected && calleeProtection.Zone?.Level == ProtectionLevel.Deprecated)
                        {
                            archNotes.Add($"⚠ Calls deprecated method: {calleeInfo.Value.Name}");
                        }
                    }
                }
            }

            // Check dependency violations (if layer data exists)
            if (layerAssignment != null)
            {
                var violations = new List<string>();
                var detector = new LayerDetector();
                foreach (var calleeId in callees.Take(20))
                {
                    var calleeInfo = await storage.GetMethodInfoAsync(calleeId, cancellationToken);
                    if (calleeInfo != null)
                    {
                        var calleeParenIdx = calleeInfo.Value.FullName.IndexOf('(');
                        var calleeNameOnly = calleeParenIdx >= 0 ? calleeInfo.Value.FullName[..calleeParenIdx] : calleeInfo.Value.FullName;
                        var calleeTypeParts = calleeNameOnly.Split('.');
                        var calleeTypeId = calleeTypeParts.Length >= 2 ? string.Join(".", calleeTypeParts[..^1]) : null;

                        if (calleeTypeId != null)
                        {
                            var calleeLayer = await storage.GetLayerForTypeAsync(calleeTypeId, cancellationToken);
                            if (calleeLayer != null && !detector.IsDependencyValid(layerAssignment.Layer, calleeLayer.Layer))
                            {
                                violations.Add($"{layerAssignment.Layer}→{calleeLayer.Layer}");
                            }
                        }
                    }
                }
                if (violations.Count > 0)
                    archNotes.Add($"⚠ Layer violations: {string.Join(", ", violations.Distinct())}");
            }

            if (archNotes.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Architectural Notes:");
                foreach (var note in archNotes)
                    Console.WriteLine($"  {note}");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Architectural Notes: ✓ No issues detected");
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
