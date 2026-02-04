using System.Text.RegularExpressions;

namespace AiCodeGraph.Core.Query;

public record FilterConfig(
    string[]? IncludeNamespaces = null,
    string[]? ExcludeNamespaces = null,
    string[]? IncludeTypes = null,
    string[]? ExcludeTypes = null,
    string[]? IncludeAccessibility = null,
    bool ExcludeGeneratedCode = true)
{
    public bool Matches(string fullName, string? accessibility)
    {
        // Namespace filtering
        if (IncludeNamespaces is { Length: > 0 })
        {
            var matchesAny = IncludeNamespaces.Any(p => MatchesPattern(fullName, p));
            if (!matchesAny) return false;
        }

        if (ExcludeNamespaces is { Length: > 0 })
        {
            var matchesAny = ExcludeNamespaces.Any(p => MatchesPattern(fullName, p));
            if (matchesAny) return false;
        }

        // Type filtering (match against full name which includes type)
        if (IncludeTypes is { Length: > 0 })
        {
            var matchesAny = IncludeTypes.Any(p => MatchesPattern(fullName, p));
            if (!matchesAny) return false;
        }

        if (ExcludeTypes is { Length: > 0 })
        {
            var matchesAny = ExcludeTypes.Any(p => MatchesPattern(fullName, p));
            if (matchesAny) return false;
        }

        // Accessibility filtering
        if (IncludeAccessibility is { Length: > 0 } && accessibility != null)
        {
            if (!IncludeAccessibility.Contains(accessibility, StringComparer.OrdinalIgnoreCase))
                return false;
        }

        // Generated code filtering
        if (ExcludeGeneratedCode)
        {
            if (fullName.Contains(".g.") || fullName.Contains("GeneratedCode") || fullName.Contains("<"))
                return false;
        }

        return true;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        // Support wildcards: * = any chars, ? = single char
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
    }
}
