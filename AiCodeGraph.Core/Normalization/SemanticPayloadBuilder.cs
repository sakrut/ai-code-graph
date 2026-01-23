using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.Normalization;

public class SemanticPayloadBuilder
{
    public string Build(string methodName, string returnType, IReadOnlyList<string> parameterTypes, SyntaxNode? methodBody)
    {
        var tokens = new List<string>();

        // Method name tokens
        var methodTokens = IdentifierTokenizer.TokenizeAndNormalize(methodName);
        tokens.AddRange(methodTokens);

        // Parameter type tokens
        foreach (var paramType in parameterTypes)
        {
            var typeTokens = IdentifierTokenizer.TokenizeAndNormalize(CleanTypeName(paramType));
            tokens.AddRange(typeTokens);
        }

        // Return type tokens
        if (returnType != "void")
        {
            var returnTokens = IdentifierTokenizer.TokenizeAndNormalize(CleanTypeName(returnType));
            tokens.AddRange(returnTokens);
        }

        // Called method names from the body
        if (methodBody != null)
        {
            var callTokens = ExtractCalledMethodTokens(methodBody);
            tokens.AddRange(callTokens);

            var literals = ExtractStringLiterals(methodBody);
            tokens.AddRange(literals);
        }

        // Deduplicate while preserving order
        var seen = new HashSet<string>();
        var deduplicated = new List<string>();
        foreach (var token in tokens)
        {
            if (!string.IsNullOrWhiteSpace(token) && seen.Add(token))
                deduplicated.Add(token);
        }

        return string.Join(" ", deduplicated);
    }

    private static IEnumerable<string> ExtractCalledMethodTokens(SyntaxNode body)
    {
        var tokens = new List<string>();
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                _ => null
            };

            if (methodName != null)
            {
                tokens.AddRange(IdentifierTokenizer.TokenizeAndNormalize(methodName));
            }
        }
        return tokens;
    }

    private static IEnumerable<string> ExtractStringLiterals(SyntaxNode body)
    {
        var tokens = new List<string>();
        foreach (var literal in body.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var text = literal.Token.ValueText;
                if (!string.IsNullOrWhiteSpace(text) && text.Length <= 100)
                {
                    // Split on non-alphanumeric and add individual words
                    var words = text.Split(new[] { ' ', '.', ',', '-', '_', '/', '\\', ':', ';' },
                        StringSplitOptions.RemoveEmptyEntries);
                    foreach (var word in words)
                    {
                        if (word.Length >= 2 && word.All(c => char.IsLetterOrDigit(c)))
                            tokens.Add(word.ToLowerInvariant());
                    }
                }
            }
        }
        return tokens;
    }

    private static string CleanTypeName(string typeName)
    {
        // Remove generic markers and array brackets
        var cleaned = typeName
            .Replace("[]", "")
            .Replace("?", "");

        // Extract just the type name from generic types like List<T>
        var angleBracket = cleaned.IndexOf('<');
        if (angleBracket >= 0)
            cleaned = cleaned[..angleBracket];

        // Remove namespace prefix if present
        var lastDot = cleaned.LastIndexOf('.');
        if (lastDot >= 0)
            cleaned = cleaned[(lastDot + 1)..];

        return cleaned;
    }
}
