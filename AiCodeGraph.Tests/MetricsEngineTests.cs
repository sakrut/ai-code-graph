using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Tests;

public class CognitiveComplexityTests
{
    private readonly CognitiveComplexityCalculator _calc = new();

    private static SyntaxNode GetMethodBody(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return (SyntaxNode?)method.Body ?? method.ExpressionBody!;
    }

    [Fact]
    public void EmptyMethod_ReturnsZero()
    {
        var body = GetMethodBody("class C { void M() { } }");
        Assert.Equal(0, _calc.Calculate(body));
    }

    [Fact]
    public void SingleIf_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { if (true) { } } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void IfElse_ReturnsTwo()
    {
        var body = GetMethodBody("class C { void M() { if (true) { } else { } } }");
        Assert.Equal(2, _calc.Calculate(body));
    }

    [Fact]
    public void IfElseIfElse_ReturnsThree()
    {
        var body = GetMethodBody("class C { void M() { if (true) { } else if (false) { } else { } } }");
        Assert.Equal(3, _calc.Calculate(body));
    }

    [Fact]
    public void NestedIf_ReturnsThree()
    {
        var body = GetMethodBody(@"class C { void M() { if (true) { if (true) { } } } }");
        // outer if: +1 (nesting 0)
        // inner if: +1 + 1 (nesting 1) = +2
        // total = 3
        Assert.Equal(3, _calc.Calculate(body));
    }

    [Fact]
    public void TripleNestedStructures()
    {
        var body = GetMethodBody(@"
class C { void M() {
    if (true) {         // +1 (nesting 0)
        for (;;) {      // +2 (nesting 1)
            while (true) { } // +3 (nesting 2)
        }
    }
} }");
        Assert.Equal(6, _calc.Calculate(body));
    }

    [Fact]
    public void ForLoop_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { for (int i = 0; i < 10; i++) { } } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void WhileLoop_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { while (true) { } } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void DoWhile_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { do { } while (true); } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void Switch_ReturnsOne()
    {
        var body = GetMethodBody(@"class C { void M() { switch (0) { case 0: break; } } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void TryCatch_ReturnsOneForCatch()
    {
        var body = GetMethodBody(@"class C { void M() { try { } catch { } } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void LogicalAnd_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { if (true && false) { } } }");
        // if: +1, &&: +1 = 2
        Assert.Equal(2, _calc.Calculate(body));
    }

    [Fact]
    public void LogicalAndSequence_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { if (true && false && true) { } } }");
        // if: +1, && sequence: +1 = 2
        Assert.Equal(2, _calc.Calculate(body));
    }

    [Fact]
    public void MixedLogicalOperators_IncrementsOnChange()
    {
        var body = GetMethodBody("class C { void M() { if (true && false || true) { } } }");
        // if: +1, &&: +1, ||: +1 = 3
        Assert.Equal(3, _calc.Calculate(body));
    }

    [Fact]
    public void TernaryExpression_ReturnsOne()
    {
        var body = GetMethodBody("class C { int M() { return true ? 1 : 0; } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void Goto_ReturnsOne()
    {
        var body = GetMethodBody(@"class C { void M() { goto end; end: return; } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void LambdaIncrementsNesting()
    {
        var body = GetMethodBody(@"
class C { void M() {
    System.Action a = () => {
        if (true) { }   // +2 (nesting 1 from lambda)
    };
} }");
        Assert.Equal(2, _calc.Calculate(body));
    }

    [Fact]
    public void ElseIfChainDoesNotCompoundNesting()
    {
        var body = GetMethodBody(@"
class C { void M() {
    if (true) { }       // +1
    else if (true) { }  // +1
    else if (true) { }  // +1
    else { }            // +1
} }");
        Assert.Equal(4, _calc.Calculate(body));
    }

    [Fact]
    public void CanBeReused()
    {
        var body1 = GetMethodBody("class C { void M() { if (true) { } } }");
        var body2 = GetMethodBody("class C { void M() { } }");

        Assert.Equal(1, _calc.Calculate(body1));
        Assert.Equal(0, _calc.Calculate(body2));
    }

    [Fact]
    public void NullCoalescing_ReturnsOne()
    {
        var body = GetMethodBody("class C { object M(object? x) { return x ?? new object(); } }");
        Assert.Equal(1, _calc.Calculate(body));
    }
}

public class LinesOfCodeTests
{
    private static SyntaxNode? GetMethodBody(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return (SyntaxNode?)method.Body ?? method.ExpressionBody;
    }

    [Fact]
    public void EmptyBody_ReturnsBraceLines()
    {
        var body = GetMethodBody("class C { void M() { } }");
        // "{ }" on one line = 1 line with content (the braces)
        var loc = LinesOfCodeCalculator.Calculate(body);
        Assert.True(loc >= 1);
    }

    [Fact]
    public void MethodWithStatements()
    {
        var body = GetMethodBody(@"
class C { void M() {
    int a = 1;
    int b = 2;
    int c = a + b;
} }");
        var loc = LinesOfCodeCalculator.Calculate(body);
        // { + 3 statements + } = 5
        Assert.Equal(5, loc);
    }

    [Fact]
    public void ExcludesBlankLines()
    {
        var body = GetMethodBody(@"
class C { void M() {
    int a = 1;

    int b = 2;

} }");
        var loc = LinesOfCodeCalculator.Calculate(body);
        // { + 2 statements + } = 4
        Assert.Equal(4, loc);
    }

    [Fact]
    public void ExcludesSingleLineComments()
    {
        var body = GetMethodBody(@"
class C { void M() {
    // this is a comment
    int a = 1;
    // another comment
    int b = 2;
} }");
        var loc = LinesOfCodeCalculator.Calculate(body);
        // { + 2 statements + } = 4
        Assert.Equal(4, loc);
    }

    [Fact]
    public void ExcludesBlockComments()
    {
        var body = GetMethodBody(@"
class C { void M() {
    /* block comment
       spanning multiple
       lines */
    int a = 1;
} }");
        var loc = LinesOfCodeCalculator.Calculate(body);
        // { + 1 statement + } = 3
        Assert.Equal(3, loc);
    }

    [Fact]
    public void NullBody_ReturnsZero()
    {
        Assert.Equal(0, LinesOfCodeCalculator.Calculate(null));
    }
}

public class MaxNestingDepthTests
{
    private readonly MaxNestingDepthCalculator _calc = new();

    private static SyntaxNode? GetMethodBody(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return (SyntaxNode?)method.Body ?? method.ExpressionBody;
    }

    [Fact]
    public void FlatMethod_ReturnsZero()
    {
        var body = GetMethodBody("class C { void M() { int a = 1; } }");
        Assert.Equal(0, _calc.Calculate(body));
    }

    [Fact]
    public void SingleIf_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { if (true) { } } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void NestedIfIf_ReturnsTwo()
    {
        var body = GetMethodBody("class C { void M() { if (true) { if (true) { } } } }");
        Assert.Equal(2, _calc.Calculate(body));
    }

    [Fact]
    public void ForWhileIf_ReturnsThree()
    {
        var body = GetMethodBody(@"
class C { void M() {
    for (;;) {
        while (true) {
            if (true) { }
        }
    }
} }");
        Assert.Equal(3, _calc.Calculate(body));
    }

    [Fact]
    public void TryCatch_ReturnsOne()
    {
        var body = GetMethodBody("class C { void M() { try { } catch { } } }");
        Assert.Equal(1, _calc.Calculate(body));
    }

    [Fact]
    public void NullBody_ReturnsZero()
    {
        Assert.Equal(0, _calc.Calculate(null));
    }
}

public class MetricsEngineIntegrationTests
{
    private static LoadedWorkspace CreateWorkspace(string source)
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

    [Fact]
    public void ComputesMetricsForSimpleMethod()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public void Simple()
        {
            int x = 1;
            int y = 2;
        }
    }
}";
        var engine = new MetricsEngine();
        var metrics = engine.ComputeMetrics(CreateWorkspace(source));

        Assert.Single(metrics);
        Assert.Equal(0, metrics[0].CognitiveComplexity);
        Assert.True(metrics[0].LinesOfCode > 0);
        Assert.Equal(0, metrics[0].MaxNestingDepth);
    }

    [Fact]
    public void ComputesMetricsForComplexMethod()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public int Complex(int x)
        {
            if (x > 0)
            {
                for (int i = 0; i < x; i++)
                {
                    if (i > 5)
                    {
                        return i;
                    }
                }
            }
            return 0;
        }
    }
}";
        var engine = new MetricsEngine();
        var metrics = engine.ComputeMetrics(CreateWorkspace(source));

        Assert.Single(metrics);
        Assert.True(metrics[0].CognitiveComplexity > 0);
        Assert.Equal(3, metrics[0].MaxNestingDepth);
    }

    [Fact]
    public void ComputesMetricsForMultipleMethods()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public void A() { }
        public void B() { if (true) { } }
        public void C() { for (;;) { } }
    }
}";
        var engine = new MetricsEngine();
        var metrics = engine.ComputeMetrics(CreateWorkspace(source));

        Assert.Equal(3, metrics.Count);
    }

    [Fact]
    public void HandlesExpressionBodiedMember()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public int Get() => true ? 1 : 0;
    }
}";
        var engine = new MetricsEngine();
        var metrics = engine.ComputeMetrics(CreateWorkspace(source));

        Assert.Single(metrics);
        Assert.Equal(1, metrics[0].CognitiveComplexity); // ternary
    }
}
