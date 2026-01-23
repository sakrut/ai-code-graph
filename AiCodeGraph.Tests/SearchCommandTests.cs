using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Storage;
using Microsoft.CodeAnalysis;
using CodeGraphTypeKind = AiCodeGraph.Core.Models.CodeGraph.TypeKind;

namespace AiCodeGraph.Tests;

public class SearchCommandTests : IAsyncDisposable
{
    private readonly StorageService _storage;
    private readonly HashEmbeddingEngine _engine = new(384);

    public SearchCommandTests()
    {
        _storage = new StorageService(":memory:");
    }

    public async ValueTask DisposeAsync()
    {
        await _storage.DisposeAsync();
    }

    private async Task SeedWithEmbeddings()
    {
        await _storage.InitializeAsync();

        var methods = new List<MethodModel>
        {
            new("method:RemoveTag", "RemoveTag", "MyApp.TagService.RemoveTag(int)", "void",
                [], "TagService.cs", 10, 25, Accessibility.Public, false, false, false, false, false),
            new("method:DeleteTag", "DeleteTag", "MyApp.TagManager.DeleteTag(int)", "void",
                [], "TagManager.cs", 30, 45, Accessibility.Public, false, false, false, false, false),
            new("method:CreateUser", "CreateUser", "MyApp.UserService.CreateUser(string)", "void",
                [], "UserService.cs", 10, 30, Accessibility.Public, false, false, false, false, false),
            new("method:ProcessPayment", "ProcessPayment", "MyApp.PaymentService.ProcessPayment(decimal)", "bool",
                [], "PaymentService.cs", 10, 50, Accessibility.Public, false, false, false, false, false)
        };

        var types = new List<TypeModel>
        {
            new("type:TagService", "TagService", "MyApp.TagService",
                CodeGraphTypeKind.Class, [methods[0]], [], Accessibility.Public,
                false, false, false, false, [], []),
            new("type:TagManager", "TagManager", "MyApp.TagManager",
                CodeGraphTypeKind.Class, [methods[1]], [], Accessibility.Public,
                false, false, false, false, [], []),
            new("type:UserService", "UserService", "MyApp.UserService",
                CodeGraphTypeKind.Class, [methods[2]], [], Accessibility.Public,
                false, false, false, false, [], []),
            new("type:PaymentService", "PaymentService", "MyApp.PaymentService",
                CodeGraphTypeKind.Class, [methods[3]], [], Accessibility.Public,
                false, false, false, false, [], [])
        };

        var ns = new NamespaceModel("ns:MyApp", "MyApp", types, []);
        var project = new ProjectModel("project:TestApp", "TestApp", "test.csproj", [ns]);
        await _storage.SaveCodeModelAsync([new ExtractionResult(project, [])]);

        // Save embeddings based on semantic payloads
        var embeddings = new List<(string, float[], string)>
        {
            ("method:RemoveTag", _engine.GenerateEmbedding("remove tag delete customer"), "hash-v1"),
            ("method:DeleteTag", _engine.GenerateEmbedding("delete tag remove customer"), "hash-v1"),
            ("method:CreateUser", _engine.GenerateEmbedding("create user new account"), "hash-v1"),
            ("method:ProcessPayment", _engine.GenerateEmbedding("process payment transaction charge"), "hash-v1")
        };
        await _storage.SaveEmbeddingsAsync(embeddings);
    }

    [Fact]
    public async Task Search_RelatedQuery_FindsSimilarMethods()
    {
        await SeedWithEmbeddings();

        var allEmbeddings = await _storage.GetEmbeddingsAsync();
        var queryVector = _engine.GenerateEmbedding("remove tag");

        var index = new VectorIndex();
        index.BuildIndex(allEmbeddings);
        var results = index.Search(queryVector, 4);

        // The top results should be tag-related methods
        Assert.True(results.Count > 0);
        // RemoveTag or DeleteTag should be among top results
        Assert.Contains(results.Take(2), r =>
            r.Id == "method:RemoveTag" || r.Id == "method:DeleteTag");
    }

    [Fact]
    public async Task Search_ThresholdFilters_LowScoreResults()
    {
        await SeedWithEmbeddings();

        var allEmbeddings = await _storage.GetEmbeddingsAsync();
        var queryVector = _engine.GenerateEmbedding("remove tag");

        var index = new VectorIndex();
        index.BuildIndex(allEmbeddings);
        var results = index.Search(queryVector, 10)
            .Where(r => r.Score >= 0.99f) // Very high threshold
            .ToList();

        // With a very high threshold, few or no results
        Assert.True(results.Count <= 1);
    }

    [Fact]
    public async Task Search_TopLimitsResults()
    {
        await SeedWithEmbeddings();

        var allEmbeddings = await _storage.GetEmbeddingsAsync();
        var queryVector = _engine.GenerateEmbedding("any query");

        var index = new VectorIndex();
        index.BuildIndex(allEmbeddings);
        var results = index.Search(queryVector, 2);

        Assert.True(results.Count <= 2);
    }

    [Fact]
    public async Task Search_EmptyDatabase_ReturnsEmpty()
    {
        await _storage.InitializeAsync();

        var allEmbeddings = await _storage.GetEmbeddingsAsync();
        Assert.Empty(allEmbeddings);
    }

    [Fact]
    public async Task Search_MethodInfoEnrichment_Works()
    {
        await SeedWithEmbeddings();

        var allEmbeddings = await _storage.GetEmbeddingsAsync();
        var queryVector = _engine.GenerateEmbedding("remove tag");

        var index = new VectorIndex();
        index.BuildIndex(allEmbeddings);
        var results = index.Search(queryVector, 1);

        Assert.Single(results);
        var info = await _storage.GetMethodInfoAsync(results[0].Id);
        Assert.NotNull(info);
        Assert.False(string.IsNullOrEmpty(info.Value.FullName));
        Assert.False(string.IsNullOrEmpty(info.Value.FilePath));
    }

    [Fact]
    public async Task Search_ResultsOrderedByScoreDesc()
    {
        await SeedWithEmbeddings();

        var allEmbeddings = await _storage.GetEmbeddingsAsync();
        var queryVector = _engine.GenerateEmbedding("tag management");

        var index = new VectorIndex();
        index.BuildIndex(allEmbeddings);
        var results = index.Search(queryVector, 10);

        for (int i = 0; i < results.Count - 1; i++)
            Assert.True(results[i].Score >= results[i + 1].Score);
    }
}
