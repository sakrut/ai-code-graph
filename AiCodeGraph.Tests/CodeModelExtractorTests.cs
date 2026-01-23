using AiCodeGraph.Core;
using AiCodeGraph.Core.Models.CodeGraph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CodeGraphTypeKind = AiCodeGraph.Core.Models.CodeGraph.TypeKind;

namespace AiCodeGraph.Tests;

public class CodeModelExtractorTests
{
    private readonly CodeModelExtractor _extractor = new();

    private static Compilation CreateCompilation(string source, string assemblyName = "TestAssembly")
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(assemblyName,
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void ExtractsClassWithMethods()
    {
        var source = @"
namespace MyApp.Services
{
    public class UserService
    {
        public void CreateUser(string name) { }
        public int GetUserCount() { return 0; }
    }
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        Assert.Equal("TestProject", result.Model.Name);
        Assert.Single(result.Model.Namespaces); // MyApp

        var myApp = result.Model.Namespaces[0];
        Assert.Equal("MyApp", myApp.FullName);
        Assert.Single(myApp.ChildNamespaces);

        var ns = myApp.ChildNamespaces[0];
        Assert.Equal("MyApp.Services", ns.FullName);
        Assert.Single(ns.Types);

        var type = ns.Types[0];
        Assert.Equal("UserService", type.Name);
        Assert.Equal(CodeGraphTypeKind.Class, type.Kind);
        Assert.Equal(2, type.Methods.Count);
        Assert.Contains(type.Methods, m => m.Name == "CreateUser");
        Assert.Contains(type.Methods, m => m.Name == "GetUserCount");
    }

    [Fact]
    public void ExtractsInterface()
    {
        var source = @"
namespace MyApp
{
    public interface IRepository<T>
    {
        T GetById(int id);
        void Save(T entity);
    }
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        var type = result.Model.Namespaces[0].Types[0];
        Assert.Equal(CodeGraphTypeKind.Interface, type.Kind);
        Assert.True(type.IsGeneric);
        Assert.Single(type.TypeParameters);
        Assert.Equal("T", type.TypeParameters[0]);
    }

    [Fact]
    public void ExtractsRecord()
    {
        var source = @"
namespace MyApp
{
    public record UserDto(string Name, int Age);
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        var type = result.Model.Namespaces[0].Types[0];
        Assert.Equal(CodeGraphTypeKind.Record, type.Kind);
    }

    [Fact]
    public void ExtractsImplementsRelationships()
    {
        var source = @"
namespace MyApp
{
    public interface IService { void Run(); }
    public class MyService : IService { public void Run() { } }
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        var implRelationships = result.Relationships
            .Where(r => r.Kind == RelationshipKind.Implements)
            .ToList();
        Assert.Single(implRelationships);
    }

    [Fact]
    public void ExtractsNestedTypes()
    {
        var source = @"
namespace MyApp
{
    public class Outer
    {
        public class Inner
        {
            public void InnerMethod() { }
        }
    }
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        var outer = result.Model.Namespaces[0].Types[0];
        Assert.Equal("Outer", outer.Name);
        Assert.Single(outer.NestedTypes);
        Assert.Equal("Inner", outer.NestedTypes[0].Name);
        Assert.Single(outer.NestedTypes[0].Methods);
    }

    [Fact]
    public void SkipsCompilerGeneratedMembers()
    {
        var source = @"
namespace MyApp
{
    public class MyClass
    {
        public string Name { get; set; }
        public void ExplicitMethod() { }
    }
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        var type = result.Model.Namespaces[0].Types[0];
        // Should only have ExplicitMethod, not get_Name/set_Name
        Assert.Single(type.Methods);
        Assert.Equal("ExplicitMethod", type.Methods[0].Name);
    }

    [Fact]
    public void ExtractsContainsRelationships()
    {
        var source = @"
namespace MyApp
{
    public class Svc
    {
        public void Do() { }
    }
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        var contains = result.Relationships.Where(r => r.Kind == RelationshipKind.Contains).ToList();
        // project->namespace, namespace->type, type->method = 3
        Assert.Equal(3, contains.Count);
    }

    [Fact]
    public void StableIdsAcrossRuns()
    {
        var source = @"
namespace MyApp { public class Svc { public void Do() { } } }";
        var c1 = CreateCompilation(source);
        var c2 = CreateCompilation(source);

        var r1 = _extractor.ExtractProject(c1, "P", "p.csproj");
        var r2 = _extractor.ExtractProject(c2, "P", "p.csproj");

        var type1 = r1.Model.Namespaces[0].Types[0];
        var type2 = r2.Model.Namespaces[0].Types[0];
        Assert.Equal(type1.Id, type2.Id);
        Assert.Equal(type1.Methods[0].Id, type2.Methods[0].Id);
    }

    [Fact]
    public void ExtractsMethodParameters()
    {
        var source = @"
namespace MyApp
{
    public class Svc
    {
        public string Process(int id, string name = ""default"") { return name; }
    }
}";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        var method = result.Model.Namespaces[0].Types[0].Methods[0];
        Assert.Equal(2, method.Parameters.Count);
        Assert.Equal("id", method.Parameters[0].Name);
        Assert.False(method.Parameters[0].IsOptional);
        Assert.Equal("name", method.Parameters[1].Name);
        Assert.True(method.Parameters[1].IsOptional);
    }

    [Fact]
    public void ExtractsNestedNamespaces()
    {
        var source = @"
namespace MyApp.Services { public class A { } }
namespace MyApp.Models { public class B { } }";
        var compilation = CreateCompilation(source);
        var result = _extractor.ExtractProject(compilation, "TestProject", "test.csproj");

        // MyApp namespace with child namespaces Services and Models
        Assert.Single(result.Model.Namespaces); // MyApp
        var myApp = result.Model.Namespaces[0];
        Assert.Equal("MyApp", myApp.FullName);
        Assert.Equal(2, myApp.ChildNamespaces.Count);
    }
}
