using Microsoft.Extensions.DependencyInjection;
using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.DI;
using AiCodeGraph.Core.Drift;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddAiCodeGraph_RegistersAllServices()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph();
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<AiCodeGraphOptions>());
        Assert.NotNull(provider.GetService<IEmbeddingEngine>());
        Assert.NotNull(provider.GetService<MetricsEngine>());
        Assert.NotNull(provider.GetService<IntentNormalizer>());
        Assert.NotNull(provider.GetService<CallGraphBuilder>());
        Assert.NotNull(provider.GetService<StructuralCloneDetector>());
        Assert.NotNull(provider.GetService<SemanticCloneDetector>());
        Assert.NotNull(provider.GetService<HybridScorer>());
        Assert.NotNull(provider.GetService<IntentClusterer>());
        Assert.NotNull(provider.GetService<ChurnAnalyzer>());
        Assert.NotNull(provider.GetService<DriftDetector>());
    }

    [Fact]
    public void AddAiCodeGraph_DefaultOptions_UsesHashEngine()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph();
        var provider = services.BuildServiceProvider();

        var engine = provider.GetRequiredService<IEmbeddingEngine>();
        Assert.IsType<HashEmbeddingEngine>(engine);
        Assert.Equal(384, engine.Dimensions);
    }

    [Fact]
    public void AddAiCodeGraph_OpenAiWithoutKey_FallsBackToHash()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph(opts =>
        {
            opts.EmbeddingEngine = "openai";
            opts.OpenAiApiKey = null;
        });
        var provider = services.BuildServiceProvider();

        // Without API key, should fall back to hash
        var engine = provider.GetRequiredService<IEmbeddingEngine>();
        Assert.IsType<HashEmbeddingEngine>(engine);
    }

    [Fact]
    public void AddAiCodeGraph_OnnxWithoutModel_FallsBackToHash()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph(opts =>
        {
            opts.EmbeddingEngine = "onnx";
            opts.OnnxModelPath = "/nonexistent/model.onnx";
        });
        var provider = services.BuildServiceProvider();

        var engine = provider.GetRequiredService<IEmbeddingEngine>();
        Assert.IsType<HashEmbeddingEngine>(engine);
    }

    [Fact]
    public void AddAiCodeGraph_ConfigureOptions_AppliesCorrectly()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph(opts =>
        {
            opts.DatabasePath = "/custom/path.db";
            opts.EmbeddingDimensions = 768;
            opts.EmbeddingEngine = "hash";
        });
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<AiCodeGraphOptions>();
        Assert.Equal("/custom/path.db", options.DatabasePath);
        Assert.Equal(768, options.EmbeddingDimensions);
        Assert.Equal("hash", options.EmbeddingEngine);
    }

    [Fact]
    public void AddAiCodeGraph_ScopedStorage_CreatesNewInstancePerScope()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph(opts => opts.DatabasePath = ":memory:");
        var provider = services.BuildServiceProvider();

        IStorageService storage1, storage2;
        using (var scope1 = provider.CreateScope())
        {
            storage1 = scope1.ServiceProvider.GetRequiredService<IStorageService>();
        }
        using (var scope2 = provider.CreateScope())
        {
            storage2 = scope2.ServiceProvider.GetRequiredService<IStorageService>();
        }

        Assert.NotSame(storage1, storage2);
    }

    [Fact]
    public void AddAiCodeGraph_TransientAnalyzers_CreateNewInstances()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph();
        var provider = services.BuildServiceProvider();

        var analyzer1 = provider.GetRequiredService<ChurnAnalyzer>();
        var analyzer2 = provider.GetRequiredService<ChurnAnalyzer>();

        Assert.NotSame(analyzer1, analyzer2);
    }

    [Fact]
    public void AddAiCodeGraph_SingletonEngine_SameInstance()
    {
        var services = new ServiceCollection();
        services.AddAiCodeGraph();
        var provider = services.BuildServiceProvider();

        var engine1 = provider.GetRequiredService<IEmbeddingEngine>();
        var engine2 = provider.GetRequiredService<IEmbeddingEngine>();

        Assert.Same(engine1, engine2);
    }
}
