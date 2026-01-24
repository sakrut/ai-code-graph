using AiCodeGraph.Core.Models;
using AiCodeGraph.Core.Models.CodeGraph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Tests;

public static class TestHelpers
{
    public static SyntaxNode? GetMethodBody(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return (SyntaxNode?)method.Body ?? method.ExpressionBody;
    }

    public static SyntaxNode GetMethodBodyRequired(string source)
    {
        return GetMethodBody(source) ?? throw new InvalidOperationException("Method has no body");
    }

    public static LoadedWorkspace CreateWorkspace(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var projectId = ProjectId.CreateNewId();
        var compilations = new Dictionary<ProjectId, Compilation> { { projectId, compilation } };
        return new LoadedWorkspace(null!, compilations, Array.Empty<WorkspaceDiagnosticInfo>());
    }

    public static int CountMethodsInType(TypeModel type)
    {
        return type.Methods.Count + type.NestedTypes.Sum(CountMethodsInType);
    }
}
