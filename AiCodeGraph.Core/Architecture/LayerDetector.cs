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
}
