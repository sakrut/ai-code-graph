# Task ID: 50

**Title:** Full Dependency Injection Container Setup

**Status:** done

**Dependencies:** 27 ✓, 42 ✓, 43 ✓, 44 ✓

**Priority:** medium

**Description:** Wire all services using Microsoft.Extensions.DependencyInjection (already imported but unused), with configurable IEmbeddingEngine registration and service resolution in command actions.

**Details:**

Create new file: AiCodeGraph.Core/DI/ServiceRegistration.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using AiCodeGraph.Core.Storage;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Metrics;
using AiCodeGraph.Core.CallGraph;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Drift;

namespace AiCodeGraph.Core.DI;

public static class ServiceRegistration
{
    public static IServiceCollection AddAiCodeGraph(this IServiceCollection services, Action<AiCodeGraphOptions>? configure = null)
    {
        var options = new AiCodeGraphOptions();
        configure?.Invoke(options);
        
        // Storage - scoped (one per command invocation)
        services.AddScoped<IStorageService>(sp =>
        {
            var storage = new StorageService(options.DatabasePath);
            return storage;
        });
        
        // Embedding engine - configurable
        services.AddSingleton<IEmbeddingEngine>(sp =>
        {
            return options.EmbeddingEngine switch
            {
                "openai" => new OpenAiEmbeddingEngine(
                    options.OpenAiApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
                    options.EmbeddingModel ?? "text-embedding-3-small",
                    options.EmbeddingDimensions),
                "onnx" => new OnnxEmbeddingEngine(
                    options.OnnxModelPath ?? "./models/all-MiniLM-L6-v2.onnx",
                    options.EmbeddingDimensions),
                _ => new HashEmbeddingEngine()
            };
        });
        
        // Analysis services - transient
        services.AddTransient<MetricsEngine>();
        services.AddTransient<CallGraphBuilder>();
        services.AddTransient<IntentNormalizer>();
        services.AddTransient<IntentClusterer>();
        services.AddTransient<StructuralCloneDetector>();
        services.AddTransient<SemanticCloneDetector>();
        services.AddTransient<HybridScorer>();
        services.AddTransient<DriftDetector>();
        
        return services;
    }
}

public class AiCodeGraphOptions
{
    public string? DatabasePath { get; set; } = "./ai-code-graph/graph.db";
    public string EmbeddingEngine { get; set; } = "hash";
    public string? EmbeddingModel { get; set; }
    public int EmbeddingDimensions { get; set; } = 384;
    public string? OpenAiApiKey { get; set; }
    public string? OnnxModelPath { get; set; }
}
```

Modify Program.cs to use DI:
```csharp
var services = new ServiceCollection();
services.AddAiCodeGraph(opts =>
{
    opts.DatabasePath = dbPath;
    opts.EmbeddingEngine = engineType;
});
var provider = services.BuildServiceProvider();

// In command actions:
using var scope = provider.CreateScope();
var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
```

**Test Strategy:**

Create ServiceRegistrationTests.cs. (1) Verify DI container resolves all services. (2) Verify IStorageService resolves to StorageService. (3) Verify IEmbeddingEngine resolves correctly for each engine option. (4) Verify scoped storage creates new instance per scope. (5) Verify transient analyzers are new per resolution. (6) Integration test: full analyze pipeline via DI.

## Subtasks

### 50.1. Create AiCodeGraphOptions class and ServiceRegistration.cs with AddAiCodeGraph extension method

**Status:** pending  
**Dependencies:** None  

Create the AiCodeGraph.Core/DI/ directory and implement ServiceRegistration.cs containing the AiCodeGraphOptions configuration class and the AddAiCodeGraph IServiceCollection extension method that wires up all service registrations.

**Details:**

Create AiCodeGraph.Core/DI/ServiceRegistration.cs with:
1. AiCodeGraphOptions class with properties: DatabasePath (string?, default ./ai-code-graph/graph.db), EmbeddingEngine (string, default 'hash'), EmbeddingModel (string?), EmbeddingDimensions (int, default 384), OpenAiApiKey (string?), OnnxModelPath (string?).
2. Static class ServiceRegistration with AddAiCodeGraph extension method accepting Action<AiCodeGraphOptions>? configure parameter.
3. The extension method creates options, invokes configure callback, then registers all services on the IServiceCollection.
4. Requires IStorageService interface from task 27 to be completed first for proper interface-based registration.
5. Uses Microsoft.Extensions.DependencyInjection (already referenced v10.0.2 in Core project).

### 50.2. Register services with correct lifetimes: scoped StorageService, singleton IEmbeddingEngine factory, transient analyzers

**Status:** pending  
**Dependencies:** 50.1  

Within the AddAiCodeGraph method, register IStorageService as scoped with factory using DatabasePath, IEmbeddingEngine as singleton with engine-type factory switch (hash/openai/onnx), and all analysis services (MetricsEngine, CallGraphBuilder, IntentNormalizer, IntentClusterer, StructuralCloneDetector, SemanticCloneDetector, HybridScorer, DriftDetector) as transient.

**Details:**

Service lifetime registrations:
1. IStorageService - AddScoped with factory: creates new StorageService(options.DatabasePath). Scoped ensures one instance per command invocation/scope.
2. IEmbeddingEngine - AddSingleton with factory switch: 'hash' -> HashEmbeddingEngine(options.EmbeddingDimensions), 'openai' -> OpenAiEmbeddingEngine (with API key from options or env var fallback), 'onnx' -> OnnxEmbeddingEngine (with model path). Default to HashEmbeddingEngine.
3. Transient services: MetricsEngine, CallGraphBuilder, IntentNormalizer, IntentClusterer, StructuralCloneDetector, SemanticCloneDetector, HybridScorer, DriftDetector - all parameterless constructors, registered with AddTransient<T>().
4. Note: VectorIndex and CodeModelExtractor are lightweight and may remain directly instantiated in commands rather than registered.

### 50.3. Modify Program.cs to build ServiceProvider and create scopes in all command actions

**Status:** pending  
**Dependencies:** 50.1, 50.2  

Refactor Program.cs to create a ServiceCollection at startup, call AddAiCodeGraph with options from command-line arguments, build the ServiceProvider, and replace direct service instantiation in all 11+ command actions with scope.ServiceProvider.GetRequiredService<T>() calls.

**Details:**

Modifications to AiCodeGraph.Cli/Program.cs:
1. At top level (before command definitions), create ServiceCollection and configure with AddAiCodeGraph.
2. Build ServiceProvider after parsing options (dbPath, embeddingEngine type, dimensions).
3. In the analyze command action: create scope, resolve IStorageService, IEmbeddingEngine, MetricsEngine, CallGraphBuilder, IntentNormalizer, clone detectors, IntentClusterer via GetRequiredService<T>().
4. In read-only commands (callgraph, hotspots, tree, similar, duplicates, clusters, search, export, drift, context): create scope, resolve IStorageService via GetRequiredService, call OpenAsync().
5. Ensure scopes are disposed properly (using var scope = provider.CreateScope()).
6. Handle the challenge that options like dbPath come from command-line parsing - may need to configure DI per-command or use a shared options pattern.
7. WorkspaceLoader and CodeModelExtractor can remain directly instantiated as they have special lifecycle needs (MSBuild locator).

### 50.4. Verify all commands work through DI resolution with integration testing

**Status:** pending  
**Dependencies:** 50.3  

Run the full test suite and manually verify all 11+ CLI commands work correctly through DI-resolved services, ensuring no regressions in behavior from the refactor.

**Details:**

Verification steps:
1. Run dotnet test - all 178 existing tests must pass (some tests instantiate services directly and should still work).
2. Run dotnet build to ensure no compilation errors.
3. Test the analyze command against tests/fixtures/TestSolution/ - verify it produces a valid graph.db.
4. Test read-only commands against the generated graph.db: hotspots, tree, callgraph <method>, similar <method>, search <query>, duplicates, clusters, export, drift (with two DBs), context <method>.
5. Verify embedding engine selection works: --embedding-engine hash (default), and that the factory correctly falls back to HashEmbeddingEngine for unknown types.
6. Check that scoped StorageService is properly disposed after each command (no locked database files).
7. Verify the MCP server (McpServer.cs) still works if it was updated to use DI.

### 50.5. Write ServiceRegistrationTests verifying resolution of all registered services

**Status:** pending  
**Dependencies:** 50.1, 50.2  

Create AiCodeGraph.Tests/ServiceRegistrationTests.cs with comprehensive tests for DI container configuration: verifying all services resolve, lifetime behaviors are correct, embedding engine factory logic works for all engine types, and options configuration is applied properly.

**Details:**

Create AiCodeGraph.Tests/ServiceRegistrationTests.cs with tests:
1. Test_DefaultOptions_ResolvesAllServices - build container with defaults, resolve each registered type.
2. Test_StorageService_ScopedLifetime - verify two scopes produce different IStorageService instances.
3. Test_EmbeddingEngine_HashDefault - verify default config resolves HashEmbeddingEngine.
4. Test_EmbeddingEngine_FactorySwitch - verify 'hash', 'openai', 'onnx' options create correct types (openai/onnx may need mocking or skip if API key/model file missing).
5. Test_EmbeddingEngine_SingletonLifetime - verify same instance returned across multiple resolutions.
6. Test_TransientServices_NewInstanceEachResolve - verify MetricsEngine, CallGraphBuilder etc. are new instances each time.
7. Test_OptionsConfiguration_Applied - verify custom DatabasePath, EmbeddingDimensions are passed through.
8. Test_EmbeddingEngine_FallbackToHash_UnknownType - verify unknown engine type defaults to HashEmbeddingEngine.
9. Follow existing test patterns: xUnit, IAsyncDisposable for cleanup.
