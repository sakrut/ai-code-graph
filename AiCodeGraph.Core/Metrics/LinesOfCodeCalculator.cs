using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AiCodeGraph.Core.Metrics;

public static class LinesOfCodeCalculator
{
    public static int Calculate(SyntaxNode? body)
    {
        if (body == null) return 0;

        var text = body.ToFullString();
        var lines = text.Split('\n');
        var count = 0;
        var inBlockComment = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (inBlockComment)
            {
                if (line.Contains("*/"))
                {
                    inBlockComment = false;
                    var afterComment = line.Substring(line.IndexOf("*/") + 2).Trim();
                    if (afterComment.Length > 0 && !afterComment.StartsWith("//"))
                        count++;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("//") || line.StartsWith("///"))
                continue;

            if (line.StartsWith("/*"))
            {
                if (line.Contains("*/"))
                {
                    var afterComment = line.Substring(line.IndexOf("*/") + 2).Trim();
                    if (afterComment.Length > 0 && !afterComment.StartsWith("//"))
                        count++;
                }
                else
                {
                    inBlockComment = true;
                }
                continue;
            }

            count++;
        }

        return count;
    }
}
