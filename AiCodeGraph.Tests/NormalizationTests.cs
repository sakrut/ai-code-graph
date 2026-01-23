using AiCodeGraph.Core.Models;
using AiCodeGraph.Core.Normalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Tests;

public class IdentifierTokenizerTests
{
    [Theory]
    [InlineData("GetCustomerById", new[] { "Get", "Customer", "By", "Id" })]
    [InlineData("removeTag", new[] { "remove", "Tag" })]
    [InlineData("HTTPClient", new[] { "HTTP", "Client" })]
    [InlineData("XMLParser", new[] { "XML", "Parser" })]
    [InlineData("ID", new[] { "ID" })]
    [InlineData("getURL", new[] { "get", "URL" })]
    [InlineData("get_customer_id", new[] { "get", "customer", "id" })]
    [InlineData("", new string[0])]
    public void Tokenize_SplitsCorrectly(string input, string[] expected)
    {
        var result = IdentifierTokenizer.Tokenize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TokenizeAndNormalize_ReturnsLowercase()
    {
        var result = IdentifierTokenizer.TokenizeAndNormalize("GetCustomerById");
        Assert.Equal(new[] { "get", "customer", "by", "id" }, result);
    }

    [Fact]
    public void Tokenize_HandlesNumericSegments()
    {
        var result = IdentifierTokenizer.Tokenize("Get2ndItem");
        Assert.Contains("Get", result);
        Assert.Contains("2", result);
    }

    [Fact]
    public void Tokenize_HandlesSingleChar()
    {
        var result = IdentifierTokenizer.Tokenize("x");
        Assert.Single(result);
        Assert.Equal("x", result[0]);
    }
}

public class StructuralSignatureBuilderTests
{
    private readonly StructuralSignatureBuilder _builder = new();

    private static SyntaxNode? GetMethodBody(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return (SyntaxNode?)method.Body ?? method.ExpressionBody;
    }

    [Fact]
    public void SameLogic_DifferentVarNames_SameSignature()
    {
        var body1 = GetMethodBody(@"class C { void M() { int foo = 1; int bar = foo + 2; } }");
        var body2 = GetMethodBody(@"class C { void M() { int x = 1; int y = x + 2; } }");

        var sig1 = _builder.Build(body1);
        var sig2 = _builder.Build(body2);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void ReplacesStringLiterals()
    {
        var body = GetMethodBody(@"class C { void M() { string s = ""hello""; } }");
        var sig = _builder.Build(body);

        Assert.Contains("LIT_STR", sig);
        Assert.DoesNotContain("hello", sig);
    }

    [Fact]
    public void ReplacesNumericLiterals()
    {
        var body = GetMethodBody(@"class C { void M() { int x = 42; } }");
        var sig = _builder.Build(body);

        Assert.Contains("LIT_NUM", sig);
        Assert.DoesNotContain("42", sig);
    }

    [Fact]
    public void PreservesControlFlow()
    {
        var body = GetMethodBody(@"class C { void M() { if (true) { for (;;) { } } } }");
        var sig = _builder.Build(body);

        Assert.Contains("if", sig);
        Assert.Contains("for", sig);
    }

    [Fact]
    public void NullBody_ReturnsEmpty()
    {
        Assert.Equal("", _builder.Build(null));
    }

    [Fact]
    public void IsDeterministic()
    {
        var body = GetMethodBody(@"class C { void M() { int a = 1; if (a > 0) { a++; } } }");
        var sig1 = _builder.Build(body);
        var sig2 = _builder.Build(body);
        Assert.Equal(sig1, sig2);
    }
}

public class SemanticPayloadBuilderTests
{
    private readonly SemanticPayloadBuilder _builder = new();

    private static SyntaxNode? GetMethodBody(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return (SyntaxNode?)method.Body ?? method.ExpressionBody;
    }

    [Fact]
    public void IncludesMethodNameTokens()
    {
        var payload = _builder.Build("RemoveCustomerTag", "void", [], null);
        Assert.Contains("remove", payload);
        Assert.Contains("customer", payload);
        Assert.Contains("tag", payload);
    }

    [Fact]
    public void IncludesParameterTypes()
    {
        var payload = _builder.Build("Process", "void", ["string", "CustomerModel"], null);
        Assert.Contains("string", payload);
        Assert.Contains("customer", payload);
        Assert.Contains("model", payload);
    }

    [Fact]
    public void IncludesReturnType()
    {
        var payload = _builder.Build("Get", "CustomerResult", [], null);
        Assert.Contains("customer", payload);
        Assert.Contains("result", payload);
    }

    [Fact]
    public void ExcludesVoidReturn()
    {
        var payload = _builder.Build("DoWork", "void", [], null);
        Assert.DoesNotContain("void", payload);
    }

    [Fact]
    public void IncludesCalledMethodTokens()
    {
        var body = GetMethodBody(@"class C { void M() { ValidateInput(); SaveToDatabase(); } void ValidateInput() {} void SaveToDatabase() {} }");
        var payload = _builder.Build("Process", "void", [], body);
        Assert.Contains("validate", payload);
        Assert.Contains("input", payload);
        Assert.Contains("save", payload);
        Assert.Contains("database", payload);
    }

    [Fact]
    public void IncludesStringLiterals()
    {
        var body = GetMethodBody(@"class C { void M() { string msg = ""customer not found""; } }");
        var payload = _builder.Build("Check", "void", [], body);
        Assert.Contains("customer", payload);
        Assert.Contains("not", payload);
        Assert.Contains("found", payload);
    }

    [Fact]
    public void DeduplicatesTokens()
    {
        var payload = _builder.Build("GetCustomer", "Customer", ["CustomerId"], null);
        var tokens = payload.Split(' ');
        Assert.Equal(tokens.Distinct().Count(), tokens.Length);
    }
}

public class IntentNormalizerIntegrationTests
{
    [Fact]
    public void NormalizesMethodsFromWorkspace()
    {
        var source = @"
namespace MyApp
{
    public class Service
    {
        public void ProcessCustomerOrder(string customerId)
        {
            int count = 0;
            if (customerId != null)
            {
                count++;
            }
        }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var projectId = ProjectId.CreateNewId();
        var workspace = new LoadedWorkspace(null!,
            new Dictionary<ProjectId, Compilation> { { projectId, compilation } },
            Array.Empty<WorkspaceDiagnosticInfo>());

        var normalizer = new IntentNormalizer();
        var results = normalizer.NormalizeAll(workspace);

        Assert.Single(results);
        var normalized = results[0];
        Assert.NotEmpty(normalized.StructuralSignature);
        Assert.NotEmpty(normalized.SemanticPayload);
        Assert.Contains("process", normalized.SemanticPayload);
        Assert.Contains("customer", normalized.SemanticPayload);
        Assert.Contains("order", normalized.SemanticPayload);
    }

    [Fact]
    public void NormalizationIsDeterministic()
    {
        var source = @"
namespace MyApp
{
    public class Svc
    {
        public int Calculate(int x, int y) { return x + y; }
    }
}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var projectId = ProjectId.CreateNewId();
        var workspace = new LoadedWorkspace(null!,
            new Dictionary<ProjectId, Compilation> { { projectId, compilation } },
            Array.Empty<WorkspaceDiagnosticInfo>());

        var normalizer = new IntentNormalizer();
        var r1 = normalizer.NormalizeAll(workspace);
        var r2 = normalizer.NormalizeAll(workspace);

        Assert.Equal(r1[0].StructuralSignature, r2[0].StructuralSignature);
        Assert.Equal(r1[0].SemanticPayload, r2[0].SemanticPayload);
    }
}
