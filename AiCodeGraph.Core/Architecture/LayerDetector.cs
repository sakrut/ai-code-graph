using System.Text.RegularExpressions;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Architecture;

public enum ArchitecturalLayer
{
    Presentation,    // Controllers, ViewModels, Views, Api
    Application,     // Services, Handlers, Commands, Queries
    Domain,          // Entities, ValueObjects, DomainServices, Core
    Infrastructure,  // Repositories, DbContexts, Persistence, Data
    Shared,          // Cross-cutting (logging, exceptions, extensions)
    Unknown
}

public record LayerAssignment(
    string TypeId,
    ArchitecturalLayer Layer,
    float Confidence,
    string Reason);

public class LayerDetector
{
    private static readonly Dictionary<ArchitecturalLayer, string[]> DefaultPatterns = new()
    {
        [ArchitecturalLayer.Presentation] = new[]
        {
            "*.Controllers.*", "*.Controllers", "*.Api.*", "*.Api",
            "*.Web.*", "*.ViewModels.*", "*.Views.*", "*.Blazor.*",
            "*.Mvc.*", "*.WebApi.*", "*.Endpoints.*"
        },
        [ArchitecturalLayer.Application] = new[]
        {
            "*.Application.*", "*.Application", "*.Services.*",
            "*.Handlers.*", "*.Commands.*", "*.Queries.*",
            "*.UseCases.*", "*.Mediator.*", "*.Cqrs.*"
        },
        [ArchitecturalLayer.Domain] = new[]
        {
            "*.Domain.*", "*.Domain", "*.Core.*",
            "*.Entities.*", "*.Models.*", "*.ValueObjects.*",
            "*.Aggregates.*", "*.DomainServices.*", "*.DomainEvents.*"
        },
        [ArchitecturalLayer.Infrastructure] = new[]
        {
            "*.Infrastructure.*", "*.Infrastructure", "*.Data.*",
            "*.Repositories.*", "*.Persistence.*", "*.EntityFramework.*",
            "*.Ef.*", "*.Sql.*", "*.External.*", "*.Adapters.*"
        },
        [ArchitecturalLayer.Shared] = new[]
        {
            "*.Shared.*", "*.Common.*", "*.CrossCutting.*",
            "*.Extensions.*", "*.Utilities.*", "*.Helpers.*",
            "*.Logging.*", "*.Exceptions.*"
        }
    };

    private readonly Dictionary<ArchitecturalLayer, string[]> _patterns;

    public LayerDetector(Dictionary<ArchitecturalLayer, string[]>? customPatterns = null)
    {
        _patterns = customPatterns ?? DefaultPatterns;
    }

    public async Task<List<LayerAssignment>> DetectLayersAsync(
        IStorageService storage,
        CancellationToken ct = default)
    {
        var treeData = await storage.GetTreeAsync(
            namespaceFilter: null,
            typeFilter: null,
            includePrivate: true,
            includeConstructors: false,
            skipTests: true,
            skipInterfaces: false,
            excludeNamespaces: null,
            cancellationToken: ct).ConfigureAwait(false);

        // Group by unique types (tree data has one row per method)
        var types = treeData
            .Select(t => (TypeId: $"{t.NamespaceName}.{t.TypeName}", NamespaceName: t.NamespaceName, TypeName: t.TypeName, TypeKind: t.TypeKind))
            .DistinctBy(t => t.TypeId)
            .ToList();

        var assignments = new List<LayerAssignment>();

        foreach (var (typeId, ns, typeName, typeKind) in types)
        {
            var (layer, confidence, reason) = DetectLayer(typeId, ns, typeName, typeKind);
            assignments.Add(new LayerAssignment(typeId, layer, confidence, reason));
        }

        return assignments;
    }

    private (ArchitecturalLayer Layer, float Confidence, string Reason) DetectLayer(
        string fullName, string namespaceName, string typeName, string typeKind)
    {
        // Check for attribute-based hints in type name
        if (typeName.EndsWith("Controller") || typeName.EndsWith("ApiController"))
            return (ArchitecturalLayer.Presentation, 1.0f, "Type name ends with Controller");

        if (typeName.EndsWith("Repository") || typeName.EndsWith("DbContext"))
            return (ArchitecturalLayer.Infrastructure, 1.0f, $"Type name ends with {(typeName.EndsWith("Repository") ? "Repository" : "DbContext")}");

        if (typeName.EndsWith("Handler") || typeName.EndsWith("CommandHandler") || typeName.EndsWith("QueryHandler"))
            return (ArchitecturalLayer.Application, 1.0f, "Type name ends with Handler");

        if (typeName.EndsWith("Service") && !namespaceName.Contains("Domain"))
            return (ArchitecturalLayer.Application, 0.9f, "Type name ends with Service");

        if (typeKind == "Interface" && typeName.StartsWith("I") && typeName.Length > 1)
        {
            // Check the interface name without the I prefix
            var baseName = typeName[1..];
            if (baseName.EndsWith("Repository"))
                return (ArchitecturalLayer.Domain, 0.9f, "Interface for Repository pattern (Domain defines, Infrastructure implements)");
        }

        // Pattern matching against namespace
        var bestMatch = (Layer: ArchitecturalLayer.Unknown, Confidence: 0f, Reason: "No pattern match");

        foreach (var (layer, patterns) in _patterns)
        {
            foreach (var pattern in patterns)
            {
                var matchResult = MatchPattern(fullName, namespaceName, pattern);
                if (matchResult.IsMatch && matchResult.Confidence > bestMatch.Confidence)
                {
                    bestMatch = (layer, matchResult.Confidence, $"Namespace pattern: {pattern}");
                }
            }
        }

        if (bestMatch.Layer == ArchitecturalLayer.Unknown)
        {
            // Try partial matching as fallback
            foreach (var (layer, patterns) in _patterns)
            {
                foreach (var pattern in patterns)
                {
                    var keyword = pattern.Replace("*.", "").Replace(".*", "").Replace("*", "");
                    if (!string.IsNullOrEmpty(keyword) &&
                        (namespaceName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                         typeName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (0.5f > bestMatch.Confidence)
                        {
                            bestMatch = (layer, 0.5f, $"Partial match: contains '{keyword}'");
                        }
                    }
                }
            }
        }

        return bestMatch;
    }

    private static (bool IsMatch, float Confidence) MatchPattern(string fullName, string namespaceName, string pattern)
    {
        // Convert glob pattern to regex
        // *.Controllers.* matches "MyApp.Controllers.UserController"
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        // Try matching against full name
        if (Regex.IsMatch(fullName, regexPattern, RegexOptions.IgnoreCase))
            return (true, 1.0f);

        // Try matching against namespace
        if (Regex.IsMatch(namespaceName, regexPattern, RegexOptions.IgnoreCase))
            return (true, 0.95f);

        // Try partial pattern match (e.g., "*.Controllers" matches "MyApp.Controllers")
        var simplePattern = pattern.TrimEnd('.', '*');
        if (namespaceName.EndsWith(simplePattern, StringComparison.OrdinalIgnoreCase))
            return (true, 0.9f);

        if (namespaceName.Contains(simplePattern.Replace("*.", ""), StringComparison.OrdinalIgnoreCase))
            return (true, 0.8f);

        return (false, 0f);
    }

    // Valid dependency directions in Clean Architecture
    private static readonly Dictionary<ArchitecturalLayer, ArchitecturalLayer[]> ValidDependencies = new()
    {
        [ArchitecturalLayer.Presentation] = new[] { ArchitecturalLayer.Application, ArchitecturalLayer.Domain, ArchitecturalLayer.Shared },
        [ArchitecturalLayer.Application] = new[] { ArchitecturalLayer.Domain, ArchitecturalLayer.Shared },
        [ArchitecturalLayer.Domain] = new[] { ArchitecturalLayer.Shared },
        [ArchitecturalLayer.Infrastructure] = new[] { ArchitecturalLayer.Domain, ArchitecturalLayer.Shared },
        [ArchitecturalLayer.Shared] = Array.Empty<ArchitecturalLayer>()
    };

    public bool IsDependencyValid(ArchitecturalLayer from, ArchitecturalLayer to)
    {
        if (from == ArchitecturalLayer.Unknown || to == ArchitecturalLayer.Unknown)
            return true; // Can't validate unknown layers

        if (from == to)
            return true; // Same layer dependencies are always valid

        return ValidDependencies.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    /// <summary>
    /// Refines layer assignments by analyzing dependency directions.
    /// Types with architectural violations get reduced confidence and warnings in Reason.
    /// </summary>
    public async Task<List<LayerAssignment>> RefineByDependencyDirectionAsync(
        List<LayerAssignment> assignments,
        IStorageService storage,
        CancellationToken ct = default)
    {
        if (assignments.Count == 0)
            return assignments;

        // Build lookup from TypeId to LayerAssignment
        var assignmentByType = assignments.ToDictionary(a => a.TypeId, a => a);

        // Get all methods and their call relationships
        var methods = await storage.GetMethodsForExportAsync(null, ct).ConfigureAwait(false);
        if (methods.Count == 0)
            return assignments;

        var allMethodIds = methods.Select(m => m.Id).ToHashSet();
        var allCalls = await storage.GetCallGraphForMethodsAsync(allMethodIds, ct).ConfigureAwait(false);

        // Map each method to its containing type (Namespace.TypeName)
        var methodToType = new Dictionary<string, string>();
        foreach (var m in methods)
        {
            var typeId = GetTypeFromMethodFullName(m.FullName);
            if (!string.IsNullOrEmpty(typeId))
                methodToType[m.Id] = typeId;
        }

        // Build type-level dependency graph from call graph
        var typeDependencies = new Dictionary<string, HashSet<string>>();
        foreach (var (callerId, calleeId) in allCalls)
        {
            if (!methodToType.TryGetValue(callerId, out var callerType) ||
                !methodToType.TryGetValue(calleeId, out var calleeType))
                continue;

            if (callerType == calleeType)
                continue; // Skip same-type calls

            if (!typeDependencies.ContainsKey(callerType))
                typeDependencies[callerType] = new HashSet<string>();
            typeDependencies[callerType].Add(calleeType);
        }

        // Analyze violations for each type
        var violationCounts = new Dictionary<string, int>();
        var violationDetails = new Dictionary<string, List<string>>();

        foreach (var (sourceType, targetTypes) in typeDependencies)
        {
            if (!assignmentByType.TryGetValue(sourceType, out var sourceAssignment))
                continue;

            foreach (var targetType in targetTypes)
            {
                if (!assignmentByType.TryGetValue(targetType, out var targetAssignment))
                    continue;

                if (!IsDependencyValid(sourceAssignment.Layer, targetAssignment.Layer))
                {
                    if (!violationCounts.ContainsKey(sourceType))
                    {
                        violationCounts[sourceType] = 0;
                        violationDetails[sourceType] = new List<string>();
                    }
                    violationCounts[sourceType]++;
                    violationDetails[sourceType].Add(
                        $"{sourceAssignment.Layer}→{targetAssignment.Layer}");
                }
            }
        }

        // Compute consistent dependency behavior for confidence boosting
        var consistentBehavior = new Dictionary<string, int>();
        foreach (var (sourceType, targetTypes) in typeDependencies)
        {
            if (!assignmentByType.TryGetValue(sourceType, out var sourceAssignment))
                continue;

            var validCount = 0;
            foreach (var targetType in targetTypes)
            {
                if (!assignmentByType.TryGetValue(targetType, out var targetAssignment))
                    continue;

                if (IsDependencyValid(sourceAssignment.Layer, targetAssignment.Layer))
                    validCount++;
            }
            consistentBehavior[sourceType] = validCount;
        }

        // Create refined assignments
        var refined = new List<LayerAssignment>();
        foreach (var assignment in assignments)
        {
            var newConfidence = assignment.Confidence;
            var newReason = assignment.Reason;

            // Reduce confidence based on violations
            if (violationCounts.TryGetValue(assignment.TypeId, out var vCount) && vCount > 0)
            {
                // Reduce confidence by 10% per violation, but never below 0.1
                newConfidence = MathF.Max(0.1f, assignment.Confidence * (1f - 0.1f * vCount));
                var uniqueViolations = violationDetails[assignment.TypeId].Distinct().ToList();
                newReason = $"{assignment.Reason}; WARNING: {vCount} dependency violation(s): {string.Join(", ", uniqueViolations)}";
            }
            // Boost confidence for low-confidence types with consistent valid dependencies
            else if (assignment.Confidence < 0.8f &&
                     consistentBehavior.TryGetValue(assignment.TypeId, out var validDeps) &&
                     validDeps >= 2)
            {
                // Boost by 10% for consistent behavior, capped at 0.9
                newConfidence = MathF.Min(0.9f, assignment.Confidence + 0.1f);
                newReason = $"{assignment.Reason}; consistent dependencies support classification";
            }

            refined.Add(new LayerAssignment(assignment.TypeId, assignment.Layer, newConfidence, newReason));
        }

        return refined;
    }

    private static string GetTypeFromMethodFullName(string fullName)
    {
        // FullName format: "ReturnType Namespace.SubNamespace.Type.Method(params)"
        var parenIdx = fullName.IndexOf('(');
        var nameOnly = parenIdx >= 0 ? fullName[..parenIdx] : fullName;

        // Strip return type prefix (everything before the last space)
        var spaceIdx = nameOnly.LastIndexOf(' ');
        if (spaceIdx >= 0)
            nameOnly = nameOnly[(spaceIdx + 1)..];

        // Return Namespace.Type (everything except last part which is the method)
        var parts = nameOnly.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts[..^1]) : string.Empty;
    }
}
