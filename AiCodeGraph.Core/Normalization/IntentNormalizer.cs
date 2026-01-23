using AiCodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.Normalization;

public class IntentNormalizer
{
    private readonly StructuralSignatureBuilder _structuralBuilder = new();
    private readonly SemanticPayloadBuilder _semanticBuilder = new();

    public List<NormalizedMethod> NormalizeAll(LoadedWorkspace workspace)
    {
        var results = new List<NormalizedMethod>();

        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var methodDecl in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
                    if (symbol == null) continue;

                    var methodId = SymbolIdGenerator.GenerateId(symbol);
                    var body = GetMethodBody(methodDecl);

                    var methodName = symbol.Name;
                    var returnType = symbol.ReturnType.ToDisplayString();
                    var paramTypes = symbol.Parameters
                        .Select(p => p.Type.ToDisplayString())
                        .ToList();

                    var structural = _structuralBuilder.Build(body);
                    var semantic = _semanticBuilder.Build(methodName, returnType, paramTypes, body);

                    results.Add(new NormalizedMethod(methodId, structural, semantic));
                }
            }
        }

        return results;
    }

    private static SyntaxNode? GetMethodBody(BaseMethodDeclarationSyntax methodDecl)
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
