# Task ID: 44

**Title:** Embedding Engine Selection in Analyze Command

**Status:** done

**Dependencies:** 42 ✓, 43 ✓

**Priority:** high

**Description:** Add --embedding-engine, --embedding-model, and --embedding-dimensions options to the analyze command, with engine factory logic and metadata persistence.

**Details:**

File: AiCodeGraph.Cli/Program.cs analyze command

Add new options:
```csharp
var embeddingEngineOption = new Option<string>("--embedding-engine", () => "hash", "Embedding engine: hash|openai|onnx");
var embeddingModelOption = new Option<string?>("--embedding-model", "Model name (e.g., text-embedding-3-small)");
var embeddingDimensionsOption = new Option<int>("--embedding-dimensions", () => 384, "Embedding vector dimensions");
analyzeCommand.AddOption(embeddingEngineOption);
analyzeCommand.AddOption(embeddingModelOption);
analyzeCommand.AddOption(embeddingDimensionsOption);
```

Add engine factory in the analyze action:
```csharp
IEmbeddingEngine CreateEmbeddingEngine(string engine, string? model, int dimensions)
{
    switch (engine.ToLower())
    {
        case "openai":
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine("Warning: OPENAI_API_KEY not set, falling back to hash engine");
                Console.ResetColor();
                return new HashEmbeddingEngine();
            }
            return new OpenAiEmbeddingEngine(apiKey, model ?? "text-embedding-3-small", dimensions);
        
        case "onnx":
            var modelPath = model ?? "./models/all-MiniLM-L6-v2.onnx";
            if (!File.Exists(modelPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"Warning: ONNX model not found at {modelPath}, falling back to hash engine");
                Console.ResetColor();
                return new HashEmbeddingEngine();
            }
            return new OnnxEmbeddingEngine(modelPath, dimensions);
        
        default: // "hash"
            return new HashEmbeddingEngine();
    }
}
```

Persist engine type in a Metadata table:
```csharp
// Add to SchemaDefinition or StorageService
CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT);

// After analysis, save:
await storage.SaveMetadataAsync("embedding_engine", engineType, ct);
await storage.SaveMetadataAsync("embedding_model", modelName, ct);
await storage.SaveMetadataAsync("embedding_dimensions", dimensions.ToString(), ct);
```

**Test Strategy:**

Test analyze with --embedding-engine hash (default, existing behavior). Test with --embedding-engine openai without API key (verify fallback warning). Test metadata is persisted correctly. Verify embeddings table has correct vector dimensions. Test with --embedding-engine onnx without model file (fallback). Integration test with each engine type using mocks.

## Subtasks

### 44.1. Add embedding options to analyze command and implement engine factory

**Status:** pending  
**Dependencies:** None  

Add --embedding-engine, --embedding-model, and --embedding-dimensions options to the analyze command in Program.cs. Implement the CreateEmbeddingEngine factory method with switch-based selection (hash/openai/onnx) and graceful fallback logic when API keys or model files are missing.

**Details:**

In AiCodeGraph.Cli/Program.cs, add three new Option<T> declarations for the analyze command: Option<string>("--embedding-engine", () => "hash", ...), Option<string?>("--embedding-model", ...), and Option<int>("--embedding-dimensions", () => 384, ...). Add all three to analyzeCommand. Inside the analyze action, implement CreateEmbeddingEngine(string engine, string? model, int dimensions) that returns IEmbeddingEngine. The switch handles 'openai' (checks OPENAI_API_KEY env var, falls back to HashEmbeddingEngine with yellow warning), 'onnx' (checks File.Exists for model path, falls back with warning), and default 'hash' (returns HashEmbeddingEngine). Wire the factory result into the existing embedding stage replacing the hardcoded HashEmbeddingEngine instantiation.

### 44.2. Add Metadata table to schema and persistence methods to StorageService

**Status:** pending  
**Dependencies:** None  

Add a Metadata table (Key TEXT PRIMARY KEY, Value TEXT) to SchemaDefinition and implement SaveMetadataAsync and GetMetadataAsync methods in StorageService for persisting and retrieving key-value metadata.

**Details:**

In AiCodeGraph.Core/Storage/SchemaDefinition.cs, add the DDL statement: CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT); to the schema initialization. In AiCodeGraph.Core/Storage/StorageService.cs, add two new public async methods: SaveMetadataAsync(string key, string value, CancellationToken ct) which uses INSERT OR REPLACE INTO Metadata (Key, Value) VALUES (@key, @value), and GetMetadataAsync(string key, CancellationToken ct) which returns string? using SELECT Value FROM Metadata WHERE Key = @key. Both methods should use the existing _connection field and follow the same patterns as other StorageService methods.

### 44.3. Integrate engine selection into analyze pipeline and persist metadata

**Status:** pending  
**Dependencies:** 44.1, 44.2  

Wire the embedding engine factory into the analyze pipeline so the selected engine is used for generating embeddings, and persist the engine type, model, and dimensions as metadata after analysis completes.

**Details:**

In the analyze command action in Program.cs, after creating the embedding engine via CreateEmbeddingEngine, pass it to the embedding generation stage (replacing any hardcoded HashEmbeddingEngine usage around lines 123-129). After the analysis pipeline completes successfully, call await storage.SaveMetadataAsync("embedding_engine", engineType, ct), await storage.SaveMetadataAsync("embedding_model", modelName ?? "", ct), and await storage.SaveMetadataAsync("embedding_dimensions", dimensions.ToString(), ct). Ensure the dimensions option value is passed through to the engine and that the embeddings table stores vectors of the correct dimensionality. Add appropriate console output indicating which engine is being used.
