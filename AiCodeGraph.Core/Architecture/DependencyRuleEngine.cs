using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Architecture;

/// <summary>
/// Type of dependency rule.
/// </summary>
public enum RuleType
{
    /// <summary>This dependency pattern is forbidden.</summary>
    Forbidden,
    /// <summary>This dependency pattern is required (warning if absent).</summary>
    Required,
    /// <summary>This dependency pattern is explicitly allowed.</summary>
    Allowed
}

/// <summary>
/// Severity level for violations.
/// </summary>
public enum ViolationSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// A dependency rule that matches source and target patterns.
/// </summary>
public record DependencyRule(
    string Name,
    string FromPattern,
    string ToPattern,
    RuleType Type,
    string? Explanation = null,
    ViolationSeverity Severity = ViolationSeverity.Error);

/// <summary>
/// A violation of a dependency rule.
/// </summary>
public record DependencyViolation(
    DependencyRule Rule,
    string FromMethodId,
    string ToMethodId,
    string FromFullName,
    string ToFullName,
    string? FromFilePath = null,
    int? FromLine = null);

/// <summary>
/// Result of rule checking.
/// </summary>
public record DependencyCheckResult(
    List<DependencyViolation> Violations,
    int TotalCallsChecked,
    int RulesApplied,
    TimeSpan ElapsedTime);

/// <summary>
/// Rules configuration file format.
/// </summary>
public record DependencyRulesConfig
{
    [JsonPropertyName("rules")]
    public List<DependencyRuleDto> Rules { get; init; } = new();

    [JsonPropertyName("includeDefaults")]
    public bool IncludeDefaults { get; init; } = true;
}

/// <summary>
/// DTO for JSON serialization of rules.
/// </summary>
public record DependencyRuleDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "forbidden";

    [JsonPropertyName("explanation")]
    public string? Explanation { get; init; }

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "error";

    public DependencyRule ToRule() => new(
        Name,
        From,
        To,
        Enum.Parse<RuleType>(Type, ignoreCase: true),
        Explanation,
        Enum.Parse<ViolationSeverity>(Severity, ignoreCase: true));
}

/// <summary>
/// Engine for checking dependencies against architectural rules.
/// </summary>
public class DependencyRuleEngine
{
    private readonly List<DependencyRule> _rules;

    private static readonly List<DependencyRule> DefaultCleanArchitectureRules = new()
    {
        // Domain should not depend on other layers
        new DependencyRule(
            "Domain → Infrastructure",
            "*.Domain.*",
            "*.Infrastructure.*",
            RuleType.Forbidden,
            "Domain layer should not depend on Infrastructure. Use interfaces and dependency injection."),

        new DependencyRule(
            "Domain → Presentation",
            "*.Domain.*",
            "*.Controllers.*",
            RuleType.Forbidden,
            "Domain layer should not depend on Presentation layer."),

        new DependencyRule(
            "Domain → Api",
            "*.Domain.*",
            "*.Api.*",
            RuleType.Forbidden,
            "Domain layer should not depend on API layer."),

        new DependencyRule(
            "Domain → Web",
            "*.Domain.*",
            "*.Web.*",
            RuleType.Forbidden,
            "Domain layer should not depend on Web layer."),

        // Application should not depend on presentation
        new DependencyRule(
            "Application → Presentation",
            "*.Application.*",
            "*.Controllers.*",
            RuleType.Forbidden,
            "Application layer should not depend on Presentation. Controllers should call services, not vice versa."),

        new DependencyRule(
            "Application → Api",
            "*.Application.*",
            "*.Api.*",
            RuleType.Forbidden,
            "Application layer should not depend on API layer."),

        // Infrastructure should not depend on Presentation
        new DependencyRule(
            "Infrastructure → Presentation",
            "*.Infrastructure.*",
            "*.Controllers.*",
            RuleType.Forbidden,
            "Infrastructure layer should not depend on Presentation."),

        // Services/Handlers should not call Controllers
        new DependencyRule(
            "Service → Controller",
            "*.Services.*",
            "*.Controllers.*",
            RuleType.Forbidden,
            "Services should not call Controllers directly."),

        new DependencyRule(
            "Handler → Controller",
            "*.Handlers.*",
            "*.Controllers.*",
            RuleType.Forbidden,
            "Handlers should not call Controllers directly."),

        // Repositories typically shouldn't call Services
        new DependencyRule(
            "Repository → Service",
            "*.Repositories.*",
            "*.Services.*",
            RuleType.Forbidden,
            "Repositories should not depend on Services. Data access should be independent.",
            ViolationSeverity.Warning),

        // Core/Domain entities shouldn't call data access directly
        new DependencyRule(
            "Entity → DbContext",
            "*.Entities.*",
            "*.DbContext*",
            RuleType.Forbidden,
            "Entities should not depend on DbContext. Keep entities pure."),

        new DependencyRule(
            "Entity → Repository",
            "*.Entities.*",
            "*.Repositories.*",
            RuleType.Forbidden,
            "Entities should not depend on Repositories.")
    };

    public DependencyRuleEngine(List<DependencyRule>? rules = null)
    {
        _rules = rules ?? DefaultCleanArchitectureRules;
    }

    /// <summary>
    /// Gets the rules currently configured in this engine.
    /// </summary>
    public IReadOnlyList<DependencyRule> Rules => _rules.AsReadOnly();

    /// <summary>
    /// Loads rules from a JSON file, optionally including default rules.
    /// </summary>
    public static DependencyRuleEngine LoadFromFile(string rulesPath)
    {
        var json = File.ReadAllText(rulesPath);
        return LoadFromJson(json);
    }

    /// <summary>
    /// Loads rules from JSON string.
    /// </summary>
    public static DependencyRuleEngine LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var config = JsonSerializer.Deserialize<DependencyRulesConfig>(json, options);
        if (config == null)
            throw new InvalidOperationException("Failed to parse rules configuration");

        var rules = new List<DependencyRule>();

        if (config.IncludeDefaults)
            rules.AddRange(DefaultCleanArchitectureRules);

        foreach (var ruleDto in config.Rules)
        {
            rules.Add(ruleDto.ToRule());
        }

        return new DependencyRuleEngine(rules);
    }

    /// <summary>
    /// Creates an engine with only default Clean Architecture rules.
    /// </summary>
    public static DependencyRuleEngine CreateWithDefaults() => new(DefaultCleanArchitectureRules.ToList());

    /// <summary>
    /// Checks all method calls against the configured rules.
    /// </summary>
    public async Task<DependencyCheckResult> CheckViolationsAsync(
        IStorageService storage,
        CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var violations = new List<DependencyViolation>();
        var totalCalls = 0;

        // Get all methods for lookup
        var methods = await storage.GetMethodsForExportAsync(cancellationToken: ct);
        var methodLookup = methods.ToDictionary(m => m.Id, m => (m.FullName, m.FilePath, m.StartLine));

        // Check each method's callees
        foreach (var method in methods)
        {
            ct.ThrowIfCancellationRequested();

            var callees = await storage.GetCalleesAsync(method.Id, ct);
            foreach (var calleeId in callees)
            {
                totalCalls++;

                if (!methodLookup.TryGetValue(calleeId, out var calleeInfo))
                    continue;

                // Check against each forbidden rule
                foreach (var rule in _rules.Where(r => r.Type == RuleType.Forbidden))
                {
                    if (MatchesPattern(method.FullName, rule.FromPattern) &&
                        MatchesPattern(calleeInfo.FullName, rule.ToPattern))
                    {
                        violations.Add(new DependencyViolation(
                            rule,
                            method.Id,
                            calleeId,
                            method.FullName,
                            calleeInfo.FullName,
                            method.FilePath,
                            method.StartLine));
                    }
                }
            }
        }

        stopwatch.Stop();
        return new DependencyCheckResult(
            violations,
            totalCalls,
            _rules.Count,
            stopwatch.Elapsed);
    }

    /// <summary>
    /// Matches a full method name against a glob pattern.
    /// Supports * (any chars) and ? (single char).
    /// </summary>
    public static bool MatchesPattern(string fullName, string pattern)
    {
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(fullName, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Generates a sample rules.json file content.
    /// </summary>
    public static string GenerateSampleRulesJson()
    {
        var sample = new
        {
            includeDefaults = true,
            rules = new[]
            {
                new
                {
                    name = "Custom Rule Example",
                    from = "*.MyApp.Shared.*",
                    to = "*.MyApp.Internal.*",
                    type = "forbidden",
                    explanation = "Shared code should not depend on internal implementation details",
                    severity = "warning"
                }
            }
        };

        return JsonSerializer.Serialize(sample, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}
