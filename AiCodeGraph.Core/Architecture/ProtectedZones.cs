using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Architecture;

/// <summary>
/// Protection level for a code zone.
/// </summary>
public enum ProtectionLevel
{
    /// <summary>Code should never be modified.</summary>
    DoNotModify,
    /// <summary>Changes require explicit approval.</summary>
    RequireApproval,
    /// <summary>Code is deprecated and should not receive new dependencies.</summary>
    Deprecated
}

/// <summary>
/// Represents a protected zone in the codebase.
/// </summary>
public record ProtectedZone(
    string Pattern,
    ProtectionLevel Level,
    string Reason,
    string? OwnerContact = null);

/// <summary>
/// DTO for JSON serialization of protected zones.
/// </summary>
public record ProtectedZoneDto
{
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = "DoNotModify";

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    public ProtectedZone ToZone() => new(
        Pattern,
        Enum.Parse<ProtectionLevel>(Level, ignoreCase: true),
        Reason,
        Owner);
}

/// <summary>
/// Configuration file format for protected zones.
/// </summary>
public record ProtectedZonesConfig
{
    [JsonPropertyName("zones")]
    public List<ProtectedZoneDto> Zones { get; init; } = new();
}

/// <summary>
/// Result of checking if a method is protected.
/// </summary>
public record ProtectionCheckResult(
    bool IsProtected,
    ProtectedZone? Zone = null,
    string? WarningMessage = null);

/// <summary>
/// Manages protected zones and checks methods against them.
/// </summary>
public class ProtectedZoneManager
{
    private readonly List<ProtectedZone> _zones;
    private readonly Dictionary<string, Regex> _patternCache = new();

    private const string DefaultConfigPath = ".ai-code-graph/protected-zones.json";

    public ProtectedZoneManager(List<ProtectedZone>? zones = null)
    {
        _zones = zones ?? new List<ProtectedZone>();
    }

    /// <summary>
    /// Gets the configured protected zones.
    /// </summary>
    public IReadOnlyList<ProtectedZone> Zones => _zones.AsReadOnly();

    /// <summary>
    /// Loads protected zones from a JSON file.
    /// </summary>
    public static ProtectedZoneManager LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new ProtectedZoneManager();

        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    /// <summary>
    /// Loads protected zones from JSON string.
    /// </summary>
    public static ProtectedZoneManager LoadFromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var config = JsonSerializer.Deserialize<ProtectedZonesConfig>(json, options);
        if (config == null)
            return new ProtectedZoneManager();

        var zones = config.Zones.Select(z => z.ToZone()).ToList();
        return new ProtectedZoneManager(zones);
    }

    /// <summary>
    /// Tries to load protected zones from the default location relative to a project path.
    /// Returns an empty manager if not found.
    /// </summary>
    public static ProtectedZoneManager TryLoadFromProject(string projectRoot)
    {
        var configPath = Path.Combine(projectRoot, DefaultConfigPath);
        return LoadFromFile(configPath);
    }

    /// <summary>
    /// Checks if a method is in a protected zone.
    /// </summary>
    public ProtectionCheckResult CheckProtection(string methodFullName)
    {
        foreach (var zone in _zones)
        {
            if (MatchesPattern(methodFullName, zone.Pattern))
            {
                var levelText = zone.Level switch
                {
                    ProtectionLevel.DoNotModify => "DO NOT MODIFY",
                    ProtectionLevel.RequireApproval => "REQUIRES APPROVAL",
                    ProtectionLevel.Deprecated => "DEPRECATED",
                    _ => zone.Level.ToString().ToUpper()
                };

                var warning = $"⚠️ [{levelText}] {zone.Reason}";
                if (zone.OwnerContact != null)
                    warning += $" (Contact: {zone.OwnerContact})";

                return new ProtectionCheckResult(true, zone, warning);
            }
        }

        return new ProtectionCheckResult(false);
    }

    /// <summary>
    /// Gets all protected methods from the storage that match any protection zone.
    /// </summary>
    public async Task<List<(string MethodId, string FullName, ProtectedZone Zone)>> GetProtectedMethodsAsync(
        IStorageService storage,
        CancellationToken ct = default)
    {
        var results = new List<(string MethodId, string FullName, ProtectedZone Zone)>();
        var methods = await storage.GetMethodsForExportAsync(cancellationToken: ct);

        foreach (var method in methods)
        {
            foreach (var zone in _zones)
            {
                if (MatchesPattern(method.FullName, zone.Pattern))
                {
                    results.Add((method.Id, method.FullName, zone));
                    break; // Only match first zone per method
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Filters a list of method IDs to return only those in protected zones.
    /// Returns tuples of (methodId, fullName, zone) for protected methods.
    /// </summary>
    public async Task<List<(string MethodId, string FullName, ProtectedZone Zone)>> FilterProtectedAsync(
        IEnumerable<string> methodIds,
        IStorageService storage,
        CancellationToken ct = default)
    {
        var results = new List<(string MethodId, string FullName, ProtectedZone Zone)>();

        foreach (var methodId in methodIds)
        {
            var info = await storage.GetMethodInfoAsync(methodId, ct);
            if (!info.HasValue) continue;

            foreach (var zone in _zones)
            {
                if (MatchesPattern(info.Value.FullName, zone.Pattern))
                {
                    results.Add((methodId, info.Value.FullName, zone));
                    break;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Matches a method full name against a glob pattern.
    /// Supports * (any chars) and ? (single char).
    /// </summary>
    public bool MatchesPattern(string fullName, string pattern)
    {
        if (!_patternCache.TryGetValue(pattern, out var regex))
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _patternCache[pattern] = regex;
        }

        return regex.IsMatch(fullName);
    }

    /// <summary>
    /// Generates a sample protected-zones.json file content.
    /// </summary>
    public static string GenerateSampleJson()
    {
        var sample = new
        {
            zones = new object[]
            {
                new
                {
                    pattern = "*.Security.*",
                    level = "DoNotModify",
                    reason = "Security-critical authentication and authorization code",
                    owner = "security-team@company.com"
                },
                new
                {
                    pattern = "*.Payment*",
                    level = "RequireApproval",
                    reason = "PCI compliance scope - changes need security review",
                    owner = "compliance@company.com"
                },
                new
                {
                    pattern = "*.LegacyAdapter.*",
                    level = "Deprecated",
                    reason = "Scheduled for removal - don't add new dependencies"
                }
            }
        };

        return JsonSerializer.Serialize(sample, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}
