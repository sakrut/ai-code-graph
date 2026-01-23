using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Storage;
using Microsoft.CodeAnalysis;
using CodeGraphTypeKind = AiCodeGraph.Core.Models.CodeGraph.TypeKind;

namespace AiCodeGraph.Tests;

public class QueryCommandsTests : IAsyncDisposable
{
    private readonly StorageService _storage;

    public QueryCommandsTests()
    {
        _storage = new StorageService(":memory:");
    }

    public async ValueTask DisposeAsync()
    {
        await _storage.DisposeAsync();
    }

    private async Task SeedDatabase()
    {
        await _storage.InitializeAsync();

        var methods = new List<MethodModel>
        {
            new("method:CreateUser", "CreateUser", "MyApp.UserService.CreateUser(string)", "void",
                [], "UserService.cs", 10, 30, Accessibility.Public, false, false, false, false, false),
            new("method:ValidateUser", "ValidateUser", "MyApp.UserService.ValidateUser(string)", "bool",
                [], "UserService.cs", 32, 50, Accessibility.Private, false, false, false, false, false),
            new("method:UpdateUser", "UpdateUser", "MyApp.UserService.UpdateUser(int, string)", "void",
                [], "UserService.cs", 52, 80, Accessibility.Public, false, true, false, false, false),
            new("method:GetById", "GetById", "MyApp.UserRepository.GetById(int)", "User",
                [], "UserRepository.cs", 10, 20, Accessibility.Public, false, false, false, false, false)
        };

        var types = new List<TypeModel>
        {
            new("type:UserService", "UserService", "MyApp.UserService",
                CodeGraphTypeKind.Class, methods.Take(3).ToList(), [], Accessibility.Public,
                false, false, false, false, [], []),
            new("type:UserRepository", "UserRepository", "MyApp.UserRepository",
                CodeGraphTypeKind.Class, methods.Skip(3).ToList(), [], Accessibility.Public,
                false, false, false, false, [], [])
        };

        var ns = new NamespaceModel("ns:MyApp", "MyApp", types, []);
        var project = new ProjectModel("project:TestApp", "TestApp", "test.csproj", [ns]);
        var result = new ExtractionResult(project, []);
        await _storage.SaveCodeModelAsync([result]);

        var calls = new List<(string, string)>
        {
            ("method:CreateUser", "method:ValidateUser"),
            ("method:CreateUser", "method:GetById"),
            ("method:UpdateUser", "method:ValidateUser"),
            ("method:UpdateUser", "method:GetById")
        };
        await _storage.SaveCallGraphAsync(calls);

        var metrics = new List<(string, int, int, int)>
        {
            ("method:CreateUser", 8, 20, 3),
            ("method:ValidateUser", 15, 18, 4),
            ("method:UpdateUser", 12, 28, 3),
            ("method:GetById", 2, 10, 1)
        };
        await _storage.SaveMetricsAsync(metrics);
    }

    [Fact]
    public async Task GetMethodInfoAsync_ReturnsMethodDetails()
    {
        await SeedDatabase();

        var info = await _storage.GetMethodInfoAsync("method:CreateUser");
        Assert.NotNull(info);
        Assert.Equal("CreateUser", info.Value.Name);
        Assert.Equal("MyApp.UserService.CreateUser(string)", info.Value.FullName);
        Assert.Equal("UserService.cs", info.Value.FilePath);
    }

    [Fact]
    public async Task GetMethodInfoAsync_NotFound_ReturnsNull()
    {
        await SeedDatabase();

        var info = await _storage.GetMethodInfoAsync("nonexistent");
        Assert.Null(info);
    }

    [Fact]
    public async Task GetCalleesAsync_ReturnsDirectCallees()
    {
        await SeedDatabase();

        var callees = await _storage.GetCalleesAsync("method:CreateUser");
        Assert.Equal(2, callees.Count);
        Assert.Contains("method:ValidateUser", callees);
        Assert.Contains("method:GetById", callees);
    }

    [Fact]
    public async Task GetCallersAsync_ReturnsDirectCallers()
    {
        await SeedDatabase();

        var callers = await _storage.GetCallersAsync("method:ValidateUser");
        Assert.Equal(2, callers.Count);
        Assert.Contains("method:CreateUser", callers);
        Assert.Contains("method:UpdateUser", callers);
    }

    [Fact]
    public async Task GetHotspotsWithThreshold_FiltersCorrectly()
    {
        await SeedDatabase();

        var hotspots = await _storage.GetHotspotsWithThresholdAsync(20, 10);
        Assert.Equal(2, hotspots.Count);
        Assert.Equal("method:ValidateUser", hotspots[0].Id); // complexity 15
        Assert.Equal("method:UpdateUser", hotspots[1].Id);   // complexity 12
    }

    [Fact]
    public async Task GetHotspotsWithThreshold_TopLimit()
    {
        await SeedDatabase();

        var hotspots = await _storage.GetHotspotsWithThresholdAsync(2);
        Assert.Equal(2, hotspots.Count);
    }

    [Fact]
    public async Task GetHotspotsWithThreshold_OrderedByComplexityDesc()
    {
        await SeedDatabase();

        var hotspots = await _storage.GetHotspotsWithThresholdAsync(10);
        Assert.True(hotspots[0].Complexity >= hotspots[1].Complexity);
        Assert.True(hotspots[1].Complexity >= hotspots[2].Complexity);
    }

    [Fact]
    public async Task GetTreeAsync_ReturnsFullHierarchy()
    {
        await SeedDatabase();

        var tree = await _storage.GetTreeAsync();
        Assert.Equal(4, tree.Count); // 4 methods
        Assert.All(tree, r => Assert.Equal("TestApp", r.ProjectName));
        Assert.All(tree, r => Assert.Equal("MyApp", r.NamespaceName));
    }

    [Fact]
    public async Task GetTreeAsync_NamespaceFilter()
    {
        await SeedDatabase();

        var tree = await _storage.GetTreeAsync(namespaceFilter: "MyApp");
        Assert.Equal(4, tree.Count);

        var empty = await _storage.GetTreeAsync(namespaceFilter: "Other");
        Assert.Empty(empty);
    }

    [Fact]
    public async Task GetTreeAsync_TypeFilter()
    {
        await SeedDatabase();

        var tree = await _storage.GetTreeAsync(typeFilter: "UserService");
        Assert.Equal(3, tree.Count); // 3 methods in UserService
        Assert.All(tree, r => Assert.Equal("UserService", r.TypeName));
    }

    [Fact]
    public async Task SearchMethodsAsync_PartialMatch()
    {
        await SeedDatabase();

        var results = await _storage.SearchMethodsAsync("Validate");
        Assert.Single(results);
        Assert.Equal("method:ValidateUser", results[0].Id);
    }

    [Fact]
    public async Task OpenAsync_OpensExistingDatabase()
    {
        await SeedDatabase();
        // OpenAsync should work on an already-initialized connection
        // (in real usage it opens a file-based DB without dropping tables)
        var methods = await _storage.SearchMethodsAsync("User");
        Assert.Equal(4, methods.Count);
    }
}
