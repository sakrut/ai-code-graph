using System.Text;

namespace AiCodeGraph.Core.Normalization;

public static class IdentifierTokenizer
{
    public static IReadOnlyList<string> Tokenize(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return Array.Empty<string>();

        // First split on underscores
        var segments = identifier.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>();

        foreach (var segment in segments)
        {
            tokens.AddRange(SplitCamelCase(segment));
        }

        return tokens;
    }

    public static IReadOnlyList<string> TokenizeAndNormalize(string identifier)
    {
        return Tokenize(identifier).Select(t => t.ToLowerInvariant()).ToList();
    }

    private static IEnumerable<string> SplitCamelCase(string segment)
    {
        if (segment.Length == 0) yield break;

        var current = new StringBuilder();
        current.Append(segment[0]);

        for (int i = 1; i < segment.Length; i++)
        {
            var ch = segment[i];
            var prev = segment[i - 1];

            if (char.IsUpper(ch))
            {
                if (char.IsLower(prev))
                {
                    // lowercase→uppercase transition: camelCase boundary
                    yield return current.ToString();
                    current.Clear();
                }
                else if (i + 1 < segment.Length && char.IsLower(segment[i + 1]))
                {
                    // uppercase→uppercase+lowercase: end of acronym
                    // e.g., "HTTPClient" at 'C': yield "HTTP", start "C"
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Clear();
                    }
                }
            }
            else if (char.IsDigit(ch) && !char.IsDigit(prev))
            {
                // transition to digit
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else if (!char.IsDigit(ch) && char.IsDigit(prev))
            {
                // transition from digit
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }
}
