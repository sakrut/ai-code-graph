using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.Normalization;

public class StructuralSignatureBuilder
{
    public string Build(SyntaxNode? methodBody)
    {
        if (methodBody == null) return "";

        var rewriter = new NormalizingRewriter();
        var normalized = rewriter.Visit(methodBody);
        return NormalizeWhitespace(normalized.ToFullString());
    }

    private static string NormalizeWhitespace(string text)
    {
        var sb = new StringBuilder();
        bool lastWasSpace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private class NormalizingRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _variableMap = new();
        private int _varCounter;

        public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            var replacement = node.Kind() switch
            {
                SyntaxKind.StringLiteralExpression => "LIT_STR",
                SyntaxKind.NumericLiteralExpression => "LIT_NUM",
                SyntaxKind.TrueLiteralExpression => "LIT_BOOL",
                SyntaxKind.FalseLiteralExpression => "LIT_BOOL",
                SyntaxKind.NullLiteralExpression => "LIT_NULL",
                SyntaxKind.CharacterLiteralExpression => "LIT_CHAR",
                _ => null
            };

            if (replacement != null)
            {
                return SyntaxFactory.IdentifierName(replacement)
                    .WithTriviaFrom(node);
            }

            return base.VisitLiteralExpression(node);
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var originalName = node.Identifier.Text;
            var positionalName = GetPositionalName(originalName);

            var newNode = node.WithIdentifier(
                SyntaxFactory.Identifier(positionalName).WithTriviaFrom(node.Identifier));

            if (node.Initializer != null)
            {
                var newInit = (EqualsValueClauseSyntax?)Visit(node.Initializer);
                newNode = newNode.WithInitializer(newInit);
            }

            return newNode;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Replace local variable references with positional names
            if (_variableMap.TryGetValue(node.Identifier.Text, out var mapped))
            {
                return node.WithIdentifier(
                    SyntaxFactory.Identifier(mapped).WithTriviaFrom(node.Identifier));
            }

            // Normalize 'this' receiver
            if (node.Identifier.Text == "this")
            {
                return node.WithIdentifier(
                    SyntaxFactory.Identifier("recv").WithTriviaFrom(node.Identifier));
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var newExpression = (ExpressionSyntax)Visit(node.Expression)!;
            // Keep the member name as-is (method/property names are semantically important)
            return node.WithExpression(newExpression);
        }

        private string GetPositionalName(string original)
        {
            if (!_variableMap.ContainsKey(original))
            {
                _variableMap[original] = $"v{_varCounter++}";
            }
            return _variableMap[original];
        }
    }
}
