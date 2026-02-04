using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Resolves method identifiers from patterns or direct IDs.
/// Precedence: --id > exact signature match > substring match (with disambiguation).
/// </summary>
public static class MethodResolver
{
    /// <summary>
    /// Resolves a method from either an explicit ID or a search pattern.
    /// </summary>
    /// <param name="storage">Storage service to query</param>
    /// <param name="methodId">Explicit method ID (takes precedence if provided)</param>
    /// <param name="pattern">Search pattern (name or partial match)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resolved method ID or null if not found/ambiguous</returns>
    public static async Task<string?> ResolveAsync(
        StorageService storage,
        string? methodId,
        string? pattern,
        CancellationToken cancellationToken = default)
    {
        // Precedence 1: Explicit --id
        if (!string.IsNullOrEmpty(methodId))
        {
            var info = await storage.GetMethodInfoAsync(methodId, cancellationToken);
            if (info == null)
            {
                Console.Error.WriteLine($"Error: Method ID not found: {methodId}");
                Environment.ExitCode = 1;
                return null;
            }
            return methodId;
        }

        // Require either --id or pattern
        if (string.IsNullOrEmpty(pattern))
        {
            Console.Error.WriteLine("Error: Specify a method pattern or --id <MethodId>");
            Environment.ExitCode = 1;
            return null;
        }

        var matches = await storage.SearchMethodsAsync(pattern, cancellationToken);

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"Error: No methods found matching '{pattern}'");
            Environment.ExitCode = 1;
            return null;
        }

        // Precedence 2: Exact signature match
        var exactMatch = matches.FirstOrDefault(m => m.FullName == pattern);
        if (exactMatch != default)
            return exactMatch.Id;

        // Precedence 3: Single substring match
        if (matches.Count == 1)
            return matches[0].Id;

        // Precedence 4: Best match by method name (Type.Method exact)
        var bestMatch = FindBestMatch(matches, pattern);
        if (bestMatch != null)
            return bestMatch;

        // Ambiguous - print suggestions
        PrintAmbiguousMatches(matches, pattern);
        return null;
    }

    private static string? FindBestMatch(List<(string Id, string FullName)> matches, string pattern)
    {
        // Try to find exact Type.Method match (ignoring namespace and params)
        var patternParts = pattern.Split('.');
        var methodName = patternParts[^1].Split('(')[0];

        var candidates = matches.Where(m =>
        {
            var fullName = m.FullName;
            var parenIdx = fullName.IndexOf('(');
            var nameOnly = parenIdx >= 0 ? fullName[..parenIdx] : fullName;
            var parts = nameOnly.Split('.');

            // Check if pattern matches end of qualified name
            if (patternParts.Length == 1)
                return parts[^1] == methodName;

            if (patternParts.Length >= 2)
            {
                var typeName = patternParts[^2];
                return parts.Length >= 2 && parts[^1] == methodName && parts[^2] == typeName;
            }
            return false;
        }).ToList();

        if (candidates.Count == 1)
            return candidates[0].Id;

        return null;
    }

    private static void PrintAmbiguousMatches(List<(string Id, string FullName)> matches, string pattern)
    {
        Console.Error.WriteLine($"Ambiguous: '{pattern}' matches {matches.Count} methods.");
        Console.Error.WriteLine("Use --id <MethodId> to select one:");
        Console.Error.WriteLine();

        foreach (var m in matches.Take(10))
        {
            // Print in compact format: MethodId (for copy-paste)
            Console.Error.WriteLine($"  --id \"{m.Id}\"");
            Console.Error.WriteLine($"      {m.FullName}");
        }

        if (matches.Count > 10)
            Console.Error.WriteLine($"  ... and {matches.Count - 10} more");

        Environment.ExitCode = 1;
    }
}
