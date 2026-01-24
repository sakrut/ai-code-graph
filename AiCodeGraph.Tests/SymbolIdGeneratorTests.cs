using AiCodeGraph.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AiCodeGraph.Tests;

public class SymbolIdGeneratorTests
{
    private static IMethodSymbol GetMethodSymbol(string code, string methodName)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("Test")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);
        return model.GetDeclaredSymbol(method)!;
    }

    private static IMethodSymbol GetConstructorSymbol(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("Test")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var ctor = tree.GetRoot().DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .First();
        return model.GetDeclaredSymbol(ctor)!;
    }

    private static IMethodSymbol GetOperatorSymbol(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("Test")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var op = tree.GetRoot().DescendantNodes()
            .OfType<OperatorDeclarationSyntax>()
            .First();
        return model.GetDeclaredSymbol(op)!;
    }

    [Fact]
    public void SimpleMethod_ReturnsFullyQualifiedId()
    {
        var code = "namespace MyApp { public class UserService { public void CreateUser() { } } }";
        var symbol = GetMethodSymbol(code, "CreateUser");
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("MyApp.UserService.CreateUser()", id);
    }

    [Fact]
    public void MethodWithParameters_IncludesParameterTypes()
    {
        var code = "namespace MyApp { public class Svc { public void Save(string name, int id) { } } }";
        var symbol = GetMethodSymbol(code, "Save");
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("Save(string, int)", id);
    }

    [Fact]
    public void GenericMethod_IncludesTypeParameters()
    {
        var code = "namespace MyApp { public class Svc { public T Convert<T>(object input) { return default; } } }";
        var symbol = GetMethodSymbol(code, "Convert");
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("Convert<T>(object)", id);
    }

    [Fact]
    public void OverloadedMethods_DistinguishedByParameters()
    {
        var code = @"
            namespace MyApp {
                public class Svc {
                    public void Process(int x) { }
                    public void Process(string s) { }
                }
            }";
        var tree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create("Test")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => model.GetDeclaredSymbol(m)!)
            .ToList();

        var id1 = SymbolIdGenerator.GenerateId(methods[0]);
        var id2 = SymbolIdGenerator.GenerateId(methods[1]);

        Assert.NotEqual(id1, id2);
        Assert.Contains("Process(int)", id1);
        Assert.Contains("Process(string)", id2);
    }

    [Fact]
    public void Constructor_ProducesValidId()
    {
        var code = "namespace MyApp { public class User { public User(string name) { } } }";
        var symbol = GetConstructorSymbol(code);
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("User(string)", id);
    }

    [Fact]
    public void NestedType_IncludesOuterType()
    {
        var code = @"
            namespace MyApp {
                public class Outer {
                    public class Inner {
                        public void DoWork() { }
                    }
                }
            }";
        var symbol = GetMethodSymbol(code, "DoWork");
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("Outer.Inner.DoWork()", id);
    }

    [Fact]
    public void OperatorOverload_ProducesValidId()
    {
        var code = @"
            namespace MyApp {
                public class Vec {
                    public int X;
                    public static Vec operator +(Vec a, Vec b) { return a; }
                }
            }";
        var symbol = GetOperatorSymbol(code);
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("operator +(", id);
    }

    [Fact]
    public void MultipleParameters_OrderPreserved()
    {
        var code = "namespace MyApp { public class Svc { public void Send(string to, int count, bool urgent) { } } }";
        var symbol = GetMethodSymbol(code, "Send");
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("Send(string, int, bool)", id);
    }

    [Fact]
    public void GenerateDisplayString_IncludesParameterNames()
    {
        var code = "namespace MyApp { public class Svc { public void Save(string name, int id) { } } }";
        var symbol = GetMethodSymbol(code, "Save");
        var display = SymbolIdGenerator.GenerateDisplayString(symbol);
        Assert.Contains("name", display);
        Assert.Contains("id", display);
    }

    [Fact]
    public void ReturnType_IncludedInId()
    {
        var code = "namespace MyApp { public class Svc { public int GetCount() { return 0; } } }";
        var symbol = GetMethodSymbol(code, "GetCount");
        var id = SymbolIdGenerator.GenerateId(symbol);
        Assert.Contains("int", id);
        Assert.Contains("GetCount()", id);
    }
}
