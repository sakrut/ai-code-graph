using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Storage;
using Microsoft.CodeAnalysis;
using CodeGraphTypeKind = AiCodeGraph.Core.Models.CodeGraph.TypeKind;

namespace AiCodeGraph.Tests;

public class StorageServiceTests : IAsyncDisposable
{
    private readonly StorageService _storage;

    public StorageServiceTests()
    {
        _storage = new StorageService(":memory:");
    }

    public async ValueTask DisposeAsync()
    {
        await _storage.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_CreatesAllTables()
    {
        await _storage.InitializeAsync();

        // Verify tables exist by querying sqlite_master
        var tables = await GetTableNames();
        Assert.Contains("Projects", tables);
        Assert.Contains("Namespaces", tables);
        Assert.Contains("Types", tables);
        Assert.Contains("Methods", tables);
        Assert.Contains("MethodCalls", tables);
        Assert.Contains("TypeImplements", tables);
        Assert.Contains("Metrics", tables);
        Assert.Contains("NormalizedMethods", tables);
        Assert.Contains("Embeddings", tables);
        Assert.Contains("IntentClusters", tables);
        Assert.Contains("MethodClusterMap", tables);
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_RecreatesTables()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        // Reinitialize should drop all data
        await _storage.InitializeAsync();

        var methods = await _storage.SearchMethodsAsync("Create");
        Assert.Empty(methods);
    }

    [Fact]
    public async Task SaveCodeModelAsync_InsertsAllHierarchy()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        var results = await _storage.SearchMethodsAsync("User");
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task SaveCallGraphAsync_InsertsEdges()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        var calls = new List<(string, string)>
        {
            ("method:CreateUser", "method:ValidateUser")
        };
        await _storage.SaveCallGraphAsync(calls);

        var callees = await _storage.GetCalleesAsync("method:CreateUser");
        Assert.Single(callees);
        Assert.Equal("method:ValidateUser", callees[0]);
    }

    [Fact]
    public async Task SaveCallGraphAsync_DuplicateEdges_IgnoredGracefully()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        var calls = new List<(string, string)>
        {
            ("method:CreateUser", "method:ValidateUser"),
            ("method:CreateUser", "method:ValidateUser") // duplicate
        };
        await _storage.SaveCallGraphAsync(calls);

        var callees = await _storage.GetCalleesAsync("method:CreateUser");
        Assert.Single(callees);
    }

    [Fact]
    public async Task GetCallersAsync_ReturnsCorrectCallers()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        var calls = new List<(string, string)>
        {
            ("method:CreateUser", "method:ValidateUser"),
            ("method:UpdateUser", "method:ValidateUser")
        };
        await _storage.SaveCallGraphAsync(calls);

        var callers = await _storage.GetCallersAsync("method:ValidateUser");
        Assert.Equal(2, callers.Count);
    }

    [Fact]
    public async Task SaveMetricsAsync_InsertsAndQueries()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        var metrics = new List<(string, int, int, int)>
        {
            ("method:CreateUser", 15, 30, 4),
            ("method:ValidateUser", 5, 10, 2),
            ("method:UpdateUser", 20, 50, 5)
        };
        await _storage.SaveMetricsAsync(metrics);

        var hotspots = await _storage.GetHotspotsAsync(2);
        Assert.Equal(2, hotspots.Count);
        Assert.Equal("method:UpdateUser", hotspots[0].Id); // highest complexity first
    }

    [Fact]
    public async Task SearchMethodsAsync_FindsByPartialName()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        var results = await _storage.SearchMethodsAsync("Create");
        Assert.Single(results);

        results = await _storage.SearchMethodsAsync("User");
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task SearchMethodsAsync_NoMatch_ReturnsEmpty()
    {
        await _storage.InitializeAsync();
        await SaveTestModel();

        var results = await _storage.SearchMethodsAsync("NonExistent");
        Assert.Empty(results);
    }

    private async Task SaveTestModel()
    {
        var methods = new List<MethodModel>
        {
            new("method:CreateUser", "CreateUser", "MyApp.UserService.CreateUser(string)", "void",
                [], null, 10, 20, Accessibility.Public, false, false, false, false, false),
            new("method:ValidateUser", "ValidateUser", "MyApp.UserService.ValidateUser(string)", "bool",
                [], null, 22, 30, Accessibility.Private, false, false, false, false, false),
            new("method:UpdateUser", "UpdateUser", "MyApp.UserService.UpdateUser(int, string)", "void",
                [], null, 32, 50, Accessibility.Public, false, true, false, false, false)
        };

        var type = new TypeModel(
            "type:UserService", "UserService", "MyApp.UserService",
            CodeGraphTypeKind.Class, methods, [], Accessibility.Public,
            false, false, false, false, [], []);

        var ns = new NamespaceModel("ns:MyApp", "MyApp", [type], []);
        var project = new ProjectModel("project:TestApp", "TestApp", "test.csproj", [ns]);
        var result = new ExtractionResult(project, []);

        await _storage.SaveCodeModelAsync([result]);
    }

    private async Task<List<string>> GetTableNames()
    {
        // Access internal connection via reflection for testing
        var field = typeof(StorageService).GetField("_connection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var conn = (Microsoft.Data.Sqlite.SqliteConnection?)field?.GetValue(_storage);

        var tables = new List<string>();
        using var cmd = conn!.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }
}
