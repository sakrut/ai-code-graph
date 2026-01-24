using AiCodeGraph.Core.Models;
using AiCodeGraph.Core.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Core.Metrics;

public class MetricsEngine
{
    public List<MethodMetrics> ComputeMetrics(LoadedWorkspace workspace)
    {
        var results = new List<MethodMetrics>();
        var complexityCalc = new CognitiveComplexityCalculator();
        var nestingCalc = new MaxNestingDepthCalculator();

        foreach (var (projectId, compilation) in workspace.Compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var methodDecl in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                    if (symbol == null) continue;

                    var methodId = SymbolIdGenerator.GenerateId(symbol);
                    var body = MethodBodyHelper.GetMethodBody(methodDecl);
                    if (body == null) continue;

                    var complexity = complexityCalc.Calculate(body);
                    var loc = LinesOfCodeCalculator.Calculate(body);
                    var nesting = nestingCalc.Calculate(body);

                    results.Add(new MethodMetrics(methodId, complexity, loc, nesting));
                }

                foreach (var localFunc in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
                {
                    var symbol = semanticModel.GetDeclaredSymbol(localFunc);
                    if (symbol == null) continue;

                    var methodId = SymbolIdGenerator.GenerateId(symbol);
                    SyntaxNode? body = (SyntaxNode?)localFunc.Body ?? localFunc.ExpressionBody;
                    if (body == null) continue;

                    var complexity = complexityCalc.Calculate(body);
                    var loc = LinesOfCodeCalculator.Calculate(body);
                    var nesting = nestingCalc.Calculate(body);

                    results.Add(new MethodMetrics(methodId, complexity, loc, nesting));
                }
            }
        }

        return results;
    }

}
