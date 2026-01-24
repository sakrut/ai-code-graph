using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.Shared;

public static class MethodBodyHelper
{
    public static SyntaxNode? GetMethodBody(BaseMethodDeclarationSyntax methodDecl)
    {
        return methodDecl switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody,
            DestructorDeclarationSyntax d => (SyntaxNode?)d.Body ?? d.ExpressionBody,
            OperatorDeclarationSyntax o => (SyntaxNode?)o.Body ?? o.ExpressionBody,
            ConversionOperatorDeclarationSyntax co => (SyntaxNode?)co.Body ?? co.ExpressionBody,
            _ => null
        };
    }
}
