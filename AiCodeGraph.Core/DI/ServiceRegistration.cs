using Microsoft.Extensions.DependencyInjection;
using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Drift;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.DI;

public class AiCodeGraphOptions
{
    public string DatabasePath { get; set; } = "./ai-code-graph/graph.db";
    public string EmbeddingEngine { get; set; } = "hash";
    public string? EmbeddingModel { get; set; }
    public int EmbeddingDimensions { get; set; } = 384;
    public string? OpenAiApiKey { get; set; }
    public string? OnnxModelPath { get; set; }
}

public static class ServiceRegistration
{
    public static IServiceCollection AddAiCodeGraph(this IServiceCollection services, Action<AiCodeGraphOptions>? configure = null)
    {
        var options = new AiCodeGraphOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Storage - scoped (one per command invocation)
        services.AddScoped<IStorageService>(sp =>
        {
            var opts = sp.GetRequiredService<AiCodeGraphOptions>();
            return new StorageService(opts.DatabasePath);
        });

        // Embedding engine - singleton (reuse across operations)
        services.AddSingleton<IEmbeddingEngine>(sp =>
        {
            var opts = sp.GetRequiredService<AiCodeGraphOptions>();
            return CreateEmbeddingEngine(opts);
        });

        // Analysis services - transient
        services.AddTransient<MetricsEngine>();
        services.AddTransient<IntentNormalizer>();
        services.AddTransient<CallGraphBuilder>();
        services.AddTransient<StructuralCloneDetector>();
        services.AddTransient<SemanticCloneDetector>();
        services.AddTransient<HybridScorer>();
        services.AddTransient<IntentClusterer>();
        services.AddTransient<ChurnAnalyzer>();
        services.AddTransient<DriftDetector>();

        return services;
    }

    private static IEmbeddingEngine CreateEmbeddingEngine(AiCodeGraphOptions options)
    {
        switch (options.EmbeddingEngine.ToLower())
        {
            case "openai":
                var apiKey = options.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                    return new HashEmbeddingEngine();
                return new OpenAiEmbeddingEngine(apiKey, options.EmbeddingModel ?? "text-embedding-3-small", options.EmbeddingDimensions);

            case "onnx":
                var modelPath = options.OnnxModelPath ?? options.EmbeddingModel ?? "./models/all-MiniLM-L6-v2.onnx";
                if (!File.Exists(modelPath))
                    return new HashEmbeddingEngine();
                return new OnnxEmbeddingEngine(modelPath, options.EmbeddingDimensions);

            default:
                return new HashEmbeddingEngine();
        }
    }
}
