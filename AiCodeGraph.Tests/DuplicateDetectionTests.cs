using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Normalization;

namespace AiCodeGraph.Tests;

public class StructuralCloneDetectorTests
{
    private readonly StructuralCloneDetector _detector = new();

    [Fact]
    public void DetectClones_ExactDuplicates_ReturnsType1()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "void Method(int v0) { if (v0) { return v0; } }", "process data"),
            new("m2", "void Method(int v0) { if (v0) { return v0; } }", "handle data")
        };

        var clones = _detector.DetectClones(methods);
        Assert.Single(clones);
        Assert.Equal(CloneType.Type1, clones[0].Type);
        Assert.Equal(1.0f, clones[0].StructuralSimilarity);
    }

    [Fact]
    public void DetectClones_RenamedVariables_ReturnsType2()
    {
        // After normalization, renamed variables should produce same structural signature
        var methods = new List<NormalizedMethod>
        {
            new("m1", "void Method(int v0) { if (v0 > LIT_NUM) { return v0; } }", "check value"),
            new("m2", "void Method(int v0) { if (v0 > LIT_NUM) { return; } }", "validate value")
        };

        var clones = _detector.DetectClones(methods, threshold: 0.7f);
        Assert.Single(clones);
        Assert.True(clones[0].StructuralSimilarity >= 0.7f);
    }

    [Fact]
    public void DetectClones_DifferentStructure_ReturnsEmpty()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "void Method(int v0) { return v0; }", "get value"),
            new("m2", "List<string> Process(DbContext ctx) { foreach (var item in ctx.Items) { if (item.IsValid) { yield return item.Name; } } }", "process items")
        };

        var clones = _detector.DetectClones(methods);
        Assert.Empty(clones);
    }

    [Fact]
    public void DetectClones_EmptyInput_ReturnsEmpty()
    {
        var clones = _detector.DetectClones(new List<NormalizedMethod>());
        Assert.Empty(clones);
    }

    [Fact]
    public void DetectClones_SingleMethod_ReturnsEmpty()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "void Method() { }", "test")
        };
        var clones = _detector.DetectClones(methods);
        Assert.Empty(clones);
    }

    [Fact]
    public void DetectClones_MultiplePairs_FindsAll()
    {
        var sig = "void Method(int v0, string v1) { if (v0 > LIT_NUM) { Console.WriteLine(v1); } return v0; }";
        var methods = new List<NormalizedMethod>
        {
            new("m1", sig, "a"),
            new("m2", sig, "b"),
            new("m3", sig, "c"),
            new("m4", "totally different structure here", "d")
        };

        var clones = _detector.DetectClones(methods);
        // m1-m2, m1-m3, m2-m3 = 3 pairs
        Assert.Equal(3, clones.Count);
        Assert.All(clones, c => Assert.Equal(CloneType.Type1, c.Type));
    }

    [Fact]
    public void DetectClones_EmptySignature_Skipped()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "", "empty"),
            new("m2", "void Method() { }", "not empty")
        };
        var clones = _detector.DetectClones(methods);
        Assert.Empty(clones);
    }
}

public class SemanticCloneDetectorTests
{
    private readonly SemanticCloneDetector _detector = new();
    private readonly HashEmbeddingEngine _engine = new(384);

    [Fact]
    public void DetectClones_SimilarSemantics_DetectsPair()
    {
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("m1", _engine.GenerateEmbedding("get customer by id from database")),
            ("m2", _engine.GenerateEmbedding("find customer by id from database")),
            ("m3", _engine.GenerateEmbedding("delete all log entries permanently"))
        };

        var clones = _detector.DetectClones(embeddings, threshold: 0.5f);
        Assert.Contains(clones, c =>
            (c.MethodIdA == "m1" && c.MethodIdB == "m2") ||
            (c.MethodIdA == "m2" && c.MethodIdB == "m1"));
    }

    [Fact]
    public void DetectClones_DifferentSemantics_NoPairs()
    {
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("m1", _engine.GenerateEmbedding("process payment transaction")),
            ("m2", _engine.GenerateEmbedding("render user interface component"))
        };

        var clones = _detector.DetectClones(embeddings, threshold: 0.9f);
        Assert.Empty(clones);
    }

    [Fact]
    public void DetectClones_EmptyInput_ReturnsEmpty()
    {
        var clones = _detector.DetectClones(new List<(string, float[])>());
        Assert.Empty(clones);
    }

    [Fact]
    public void DetectClones_SingleMethod_ReturnsEmpty()
    {
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("m1", _engine.GenerateEmbedding("test method"))
        };
        var clones = _detector.DetectClones(embeddings);
        Assert.Empty(clones);
    }

    [Fact]
    public void DetectClones_DeduplicatesPairs()
    {
        // Use identical text to guarantee high similarity
        var vector = _engine.GenerateEmbedding("identical method content");
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("m1", vector),
            ("m2", vector)
        };

        var clones = _detector.DetectClones(embeddings, threshold: 0.5f);
        // Should have exactly one pair, not two
        Assert.Single(clones);
    }
}

public class HybridScorerTests
{
    [Fact]
    public void Merge_CombinesScores_CorrectFormula()
    {
        var scorer = new HybridScorer(alpha: 0.4f, threshold: 0f);

        var structural = new List<ClonePair>
        {
            new("m1", "m2", 0.9f, 0f, 0f, CloneType.Type2)
        };
        var semantic = new List<ClonePair>
        {
            new("m1", "m2", 0f, 0.8f, 0f, CloneType.Semantic)
        };

        var merged = scorer.Merge(structural, semantic);
        Assert.Single(merged);
        // 0.4 * 0.9 + 0.6 * 0.8 = 0.36 + 0.48 = 0.84
        Assert.InRange(merged[0].HybridScore, 0.83f, 0.85f);
    }

    [Fact]
    public void Merge_PureStructural_AlphaOne()
    {
        var scorer = new HybridScorer(alpha: 1.0f, threshold: 0f);

        var structural = new List<ClonePair>
        {
            new("m1", "m2", 0.9f, 0f, 0f, CloneType.Type2)
        };

        var merged = scorer.Merge(structural, new List<ClonePair>());
        Assert.Single(merged);
        Assert.InRange(merged[0].HybridScore, 0.89f, 0.91f);
    }

    [Fact]
    public void Merge_PureSemantic_AlphaZero()
    {
        var scorer = new HybridScorer(alpha: 0f, threshold: 0f);

        var semantic = new List<ClonePair>
        {
            new("m1", "m2", 0f, 0.85f, 0f, CloneType.Semantic)
        };

        var merged = scorer.Merge(new List<ClonePair>(), semantic);
        Assert.Single(merged);
        Assert.InRange(merged[0].HybridScore, 0.84f, 0.86f);
    }

    [Fact]
    public void Merge_ThresholdFilters()
    {
        var scorer = new HybridScorer(alpha: 0.4f, threshold: 0.7f);

        var structural = new List<ClonePair>
        {
            new("m1", "m2", 0.5f, 0f, 0f, CloneType.Type2) // 0.4*0.5 = 0.2, below threshold
        };

        var merged = scorer.Merge(structural, new List<ClonePair>());
        Assert.Empty(merged);
    }

    [Fact]
    public void Merge_ClassifiesType1()
    {
        var scorer = new HybridScorer(alpha: 0.4f, threshold: 0f);

        var structural = new List<ClonePair>
        {
            new("m1", "m2", 0.98f, 0f, 0f, CloneType.Type1)
        };

        var merged = scorer.Merge(structural, new List<ClonePair>());
        Assert.Equal(CloneType.Type1, merged[0].Type);
    }

    [Fact]
    public void Merge_OrdersByHybridScoreDescending()
    {
        var scorer = new HybridScorer(alpha: 0.5f, threshold: 0f);

        var structural = new List<ClonePair>
        {
            new("m1", "m2", 0.5f, 0f, 0f, CloneType.Type2),
            new("m3", "m4", 0.9f, 0f, 0f, CloneType.Type2)
        };

        var merged = scorer.Merge(structural, new List<ClonePair>());
        Assert.True(merged[0].HybridScore >= merged[1].HybridScore);
    }

    [Fact]
    public void Merge_NormalizesKeyOrder()
    {
        var scorer = new HybridScorer(alpha: 0.4f, threshold: 0f);

        // Structural has (m2, m1), semantic has (m1, m2) - should merge
        var structural = new List<ClonePair>
        {
            new("m2", "m1", 0.9f, 0f, 0f, CloneType.Type2)
        };
        var semantic = new List<ClonePair>
        {
            new("m1", "m2", 0f, 0.8f, 0f, CloneType.Semantic)
        };

        var merged = scorer.Merge(structural, semantic);
        Assert.Single(merged);
        Assert.InRange(merged[0].HybridScore, 0.83f, 0.85f);
    }
}

public class IntentClustererTests
{
    private readonly HashEmbeddingEngine _engine = new(384);

    [Fact]
    public void ClusterMethods_SimilarMethods_GroupsTogether()
    {
        // Use identical embeddings to guarantee clustering
        var sharedVector1 = _engine.GenerateEmbedding("permission check user access");
        var sharedVector2 = _engine.GenerateEmbedding("database log entry");

        var methods = new List<NormalizedMethod>
        {
            new("m1", "sig1", "permission check user access"),
            new("m2", "sig2", "permission check user access"),
            new("m3", "sig3", "permission check user access"),
            new("m4", "sig4", "database log entry"),
            new("m5", "sig5", "database log entry"),
            new("m6", "sig6", "database log entry")
        };

        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("m1", sharedVector1),
            ("m2", sharedVector1),
            ("m3", sharedVector1),
            ("m4", sharedVector2),
            ("m5", sharedVector2),
            ("m6", sharedVector2)
        };

        var clusterer = new IntentClusterer(epsilon: 0.1f, minPoints: 2);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        Assert.True(clusters.Count >= 1);
    }

    [Fact]
    public void ClusterMethods_TooFewMethods_ReturnsEmpty()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "sig1", "test")
        };
        var embeddings = methods.Select(m =>
            (m.MethodId, _engine.GenerateEmbedding(m.SemanticPayload))).ToList();

        var clusterer = new IntentClusterer(epsilon: 0.3f, minPoints: 3);
        var clusters = clusterer.ClusterMethods(methods, embeddings);
        Assert.Empty(clusters);
    }

    [Fact]
    public void ClusterMethods_AllDifferent_NoCluster()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "sig1", "alpha beta gamma"),
            new("m2", "sig2", "delta epsilon zeta"),
            new("m3", "sig3", "eta theta iota")
        };
        var embeddings = methods.Select(m =>
            (m.MethodId, _engine.GenerateEmbedding(m.SemanticPayload))).ToList();

        // Very tight clustering
        var clusterer = new IntentClusterer(epsilon: 0.05f, minPoints: 3);
        var clusters = clusterer.ClusterMethods(methods, embeddings);
        Assert.Empty(clusters);
    }

    [Fact]
    public void ClusterMethods_CohesionInRange()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "sig1", "customer order process"),
            new("m2", "sig2", "customer order validate"),
            new("m3", "sig3", "customer order submit")
        };
        var embeddings = methods.Select(m =>
            (m.MethodId, _engine.GenerateEmbedding(m.SemanticPayload))).ToList();

        var clusterer = new IntentClusterer(epsilon: 0.6f, minPoints: 2);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        foreach (var cluster in clusters)
        {
            Assert.InRange(cluster.Cohesion, 0f, 1f);
        }
    }

    [Fact]
    public void ClusterMethods_GeneratesLabels()
    {
        var methods = new List<NormalizedMethod>
        {
            new("m1", "sig1", "permission check user admin"),
            new("m2", "sig2", "permission validate user role"),
            new("m3", "sig3", "permission verify user access")
        };
        var embeddings = methods.Select(m =>
            (m.MethodId, _engine.GenerateEmbedding(m.SemanticPayload))).ToList();

        var clusterer = new IntentClusterer(epsilon: 0.6f, minPoints: 2);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        foreach (var cluster in clusters)
        {
            Assert.False(string.IsNullOrWhiteSpace(cluster.Label));
            Assert.False(string.IsNullOrWhiteSpace(cluster.Description));
        }
    }

    [Fact]
    public void ClusterMethods_ClusterIdsAreUnique()
    {
        var methods = new List<NormalizedMethod>();
        for (int i = 0; i < 10; i++)
            methods.Add(new($"m{i}", $"sig{i}", "identical content for clustering test purpose"));

        var embeddings = methods.Select(m =>
            (m.MethodId, _engine.GenerateEmbedding(m.SemanticPayload))).ToList();

        var clusterer = new IntentClusterer(epsilon: 0.5f, minPoints: 2);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        var ids = clusters.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ClusterMethods_VerbNounPair_ProducesDescriptiveLabel()
    {
        var sharedVector = _engine.GenerateEmbedding("save user data");
        var methods = new List<NormalizedMethod>
        {
            new("MyApp.UserService.SaveUser()", "sig1", "save user"),
            new("MyApp.UserService.SaveOrder()", "sig2", "save order"),
            new("MyApp.UserService.SaveProfile()", "sig3", "save profile")
        };
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("MyApp.UserService.SaveUser()", sharedVector),
            ("MyApp.UserService.SaveOrder()", sharedVector),
            ("MyApp.UserService.SaveProfile()", sharedVector)
        };

        var clusterer = new IntentClusterer(epsilon: 0.1f, minPoints: 2);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        Assert.NotEmpty(clusters);
        var label = clusters[0].Label;
        Assert.Contains("save", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/", label); // No old format
    }

    [Fact]
    public void ClusterMethods_TestMethods_ProducesTestLabel()
    {
        var sharedVector = _engine.GenerateEmbedding("test user validation");
        var methods = new List<NormalizedMethod>
        {
            new("AiCodeGraph.Tests.UserTests.ValidateInput_ReturnsTrue()", "sig1", "test"),
            new("AiCodeGraph.Tests.UserTests.ValidateEmail_ReturnsTrue()", "sig2", "test"),
            new("AiCodeGraph.Tests.UserTests.ValidateAddress_ReturnsTrue()", "sig3", "test")
        };
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("AiCodeGraph.Tests.UserTests.ValidateInput_ReturnsTrue()", sharedVector),
            ("AiCodeGraph.Tests.UserTests.ValidateEmail_ReturnsTrue()", sharedVector),
            ("AiCodeGraph.Tests.UserTests.ValidateAddress_ReturnsTrue()", sharedVector)
        };

        var clusterer = new IntentClusterer(epsilon: 0.1f, minPoints: 2);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        Assert.NotEmpty(clusters);
        Assert.Contains("unit tests", clusters[0].Label);
        Assert.Contains("User", clusters[0].Label);
    }

    [Fact]
    public void ClusterMethods_NamespaceContext_IncludesClassName()
    {
        var sharedVector = _engine.GenerateEmbedding("storage operations");
        var methods = new List<NormalizedMethod>
        {
            new("MyApp.StorageService.SaveAsync()", "sig1", "storage save"),
            new("MyApp.StorageService.LoadAsync()", "sig2", "storage load"),
            new("MyApp.StorageService.DeleteAsync()", "sig3", "storage delete")
        };
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("MyApp.StorageService.SaveAsync()", sharedVector),
            ("MyApp.StorageService.LoadAsync()", sharedVector),
            ("MyApp.StorageService.DeleteAsync()", sharedVector)
        };

        var clusterer = new IntentClusterer(epsilon: 0.1f, minPoints: 2);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        Assert.NotEmpty(clusters);
        Assert.Contains("StorageService", clusters[0].Label);
    }
}

public class DuplicateStorageTests
{
    [Fact]
    public async Task SaveAndGetClonePairs_RoundTrips()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        var pairs = new List<ClonePair>
        {
            new("m1", "m2", 0.9f, 0.8f, 0.84f, CloneType.Type2),
            new("m3", "m4", 0.1f, 0.95f, 0.61f, CloneType.Semantic)
        };

        await storage.SaveClonePairsAsync(pairs);
        var loaded = await storage.GetClonePairsAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("m1", loaded[0].MethodIdA);
        Assert.InRange(loaded[0].HybridScore, 0.83f, 0.85f);
    }

    [Fact]
    public async Task GetClonePairs_ThresholdFilter()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        var pairs = new List<ClonePair>
        {
            new("m1", "m2", 0.9f, 0.8f, 0.84f, CloneType.Type2),
            new("m3", "m4", 0.1f, 0.3f, 0.2f, CloneType.Semantic)
        };

        await storage.SaveClonePairsAsync(pairs);
        var loaded = await storage.GetClonePairsAsync(minThreshold: 0.5f);

        Assert.Single(loaded);
        Assert.Equal("m1", loaded[0].MethodIdA);
    }

    [Fact]
    public async Task GetClonePairs_TypeFilter()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        var pairs = new List<ClonePair>
        {
            new("m1", "m2", 0.9f, 0.8f, 0.84f, CloneType.Type2),
            new("m3", "m4", 0.1f, 0.95f, 0.61f, CloneType.Semantic)
        };

        await storage.SaveClonePairsAsync(pairs);
        var loaded = await storage.GetClonePairsAsync(typeFilter: CloneType.Semantic);

        Assert.Single(loaded);
        Assert.Equal(CloneType.Semantic, loaded[0].Type);
    }

    [Fact]
    public async Task SaveAndGetClusters_RoundTrips()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        var clusters = new List<IntentCluster>
        {
            new("cluster-0", "permission check", "permission check (3 methods, cohesion: 0.85)",
                new List<string> { "m1", "m2", "m3" }, 0.85f),
            new("cluster-1", "data access", "data access (2 methods, cohesion: 0.9)",
                new List<string> { "m4", "m5" }, 0.9f)
        };

        await storage.SaveClustersAsync(clusters);
        var loaded = await storage.GetClustersAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("permission check", loaded[0].Label);
        Assert.Equal(3, loaded[0].MethodIds.Count);
        Assert.InRange(loaded[0].Cohesion, 0.84f, 0.86f);
    }

    [Fact]
    public async Task GetClonePairs_ConceptFilter()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        // Set up clusters
        var clusters = new List<IntentCluster>
        {
            new("cluster-0", "permission check", "desc", new List<string> { "m1", "m2" }, 0.8f)
        };
        await storage.SaveClustersAsync(clusters);

        // Set up clone pairs
        var pairs = new List<ClonePair>
        {
            new("m1", "m2", 0.9f, 0.8f, 0.84f, CloneType.Type2),
            new("m3", "m4", 0.5f, 0.9f, 0.74f, CloneType.Semantic) // not in any cluster
        };
        await storage.SaveClonePairsAsync(pairs);

        var filtered = await storage.GetClonePairsAsync(conceptFilter: "permission");
        Assert.Single(filtered);
        Assert.Equal("m1", filtered[0].MethodIdA);
    }

    [Fact]
    public async Task GetMethodsForExport_AllMethods()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        // Seed a method
        var method = new AiCodeGraph.Core.Models.CodeGraph.MethodModel(
            "m1", "GetUser", "App.UserService.GetUser(int)", "User",
            [], "UserService.cs", 10, 20, Microsoft.CodeAnalysis.Accessibility.Public,
            false, false, false, false, false);
        var type = new AiCodeGraph.Core.Models.CodeGraph.TypeModel(
            "t1", "UserService", "App.UserService",
            AiCodeGraph.Core.Models.CodeGraph.TypeKind.Class,
            [method], [], Microsoft.CodeAnalysis.Accessibility.Public,
            false, false, false, false, [], []);
        var ns = new AiCodeGraph.Core.Models.CodeGraph.NamespaceModel("ns1", "App", [type], []);
        var project = new AiCodeGraph.Core.Models.CodeGraph.ProjectModel("p1", "TestApp", "test.csproj", [ns]);
        await storage.SaveCodeModelAsync([new AiCodeGraph.Core.Models.CodeGraph.ExtractionResult(project, [])]);

        var results = await storage.GetMethodsForExportAsync();
        Assert.Single(results);
        Assert.Equal("m1", results[0].Id);
        Assert.Equal("App.UserService.GetUser(int)", results[0].FullName);
    }

    [Fact]
    public async Task GetMethodsForExport_ConceptFilter()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        // Seed methods
        var methods = new List<AiCodeGraph.Core.Models.CodeGraph.MethodModel>
        {
            new("m1", "GetUser", "App.GetUser()", "void", [], null, 1, 5,
                Microsoft.CodeAnalysis.Accessibility.Public, false, false, false, false, false),
            new("m2", "DeleteUser", "App.DeleteUser()", "void", [], null, 6, 10,
                Microsoft.CodeAnalysis.Accessibility.Public, false, false, false, false, false)
        };
        var type = new AiCodeGraph.Core.Models.CodeGraph.TypeModel(
            "t1", "Svc", "App.Svc", AiCodeGraph.Core.Models.CodeGraph.TypeKind.Class,
            methods, [], Microsoft.CodeAnalysis.Accessibility.Public,
            false, false, false, false, [], []);
        var ns = new AiCodeGraph.Core.Models.CodeGraph.NamespaceModel("ns1", "App", [type], []);
        var project = new AiCodeGraph.Core.Models.CodeGraph.ProjectModel("p1", "App", "app.csproj", [ns]);
        await storage.SaveCodeModelAsync([new AiCodeGraph.Core.Models.CodeGraph.ExtractionResult(project, [])]);

        // Add cluster for m1 only
        var clusters = new List<IntentCluster>
        {
            new("c1", "user access", "desc", new List<string> { "m1" }, 0.8f)
        };
        await storage.SaveClustersAsync(clusters);

        var filtered = await storage.GetMethodsForExportAsync("user access");
        Assert.Single(filtered);
        Assert.Equal("m1", filtered[0].Id);
    }

    [Fact]
    public async Task SaveClusters_OverwritesExisting()
    {
        await using var storage = new AiCodeGraph.Core.Storage.StorageService(":memory:");
        await storage.InitializeAsync();

        var clusters1 = new List<IntentCluster>
        {
            new("cluster-0", "old", "old desc", new List<string> { "m1" }, 0.5f)
        };
        await storage.SaveClustersAsync(clusters1);

        var clusters2 = new List<IntentCluster>
        {
            new("cluster-0", "new", "new desc", new List<string> { "m2", "m3" }, 0.8f)
        };
        await storage.SaveClustersAsync(clusters2);

        var loaded = await storage.GetClustersAsync();
        Assert.Single(loaded);
        Assert.Equal("new", loaded[0].Label);
        Assert.Equal(2, loaded[0].MethodIds.Count);
    }
}
