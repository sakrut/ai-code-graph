# Task ID: 45

**Title:** Semantic Search with LLM Embeddings

**Status:** done

**Dependencies:** 44 ✓

**Priority:** medium

**Description:** Enhance search to use the same embedding engine that was used during analysis for true semantic matching, with appropriate warnings for hash-based embeddings.

**Details:**

File: AiCodeGraph.Cli/Program.cs (token-search or new semantic-search command)

Add a new `semantic-search` command that requires LLM embeddings:

```csharp
var semanticSearchCommand = new Command("semantic-search", "Search code by semantic meaning (requires LLM embeddings)");
var ssQueryOption = new Option<string>("--query", "Natural language search query") { IsRequired = true };
var ssTopOption = new Option<int>("--top", () => 10, "Number of results");
var ssDbOption = new Option<string>("--db", () => "./ai-code-graph/graph.db", "Database path");
var ssFormatOption = new Option<string>("--format", () => "table", "Output format: table|json");

semanticSearchCommand.SetAction(async (parseResult, ct) =>
{
    var dbPath = parseResult.GetValue(ssDbOption)!;
    var query = parseResult.GetValue(ssQueryOption)!;
    
    using var storage = new StorageService(dbPath);
    await storage.OpenAsync(ct);
    
    // Check what engine was used
    var engineType = await storage.GetMetadataAsync("embedding_engine", ct);
    
    if (engineType == null || engineType == "hash")
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine("Warning: Database uses hash-based embeddings. Results may not be semantically meaningful.");
        Console.Error.WriteLine("Re-analyze with --embedding-engine openai for true semantic search.");
        Console.ResetColor();
        // Fall through to hash-based search anyway
    }
    
    // Create matching engine for query embedding
    IEmbeddingEngine engine = engineType switch
    {
        "openai" => CreateOpenAiEngine(storage, ct),
        "onnx" => CreateOnnxEngine(storage, ct),
        _ => new HashEmbeddingEngine()
    };
    
    using (engine)
    {
        var queryVector = engine.GenerateEmbedding(query);
        var embeddings = await storage.GetEmbeddingsAsync(ct);
        
        var vectorIndex = GetOrBuildVectorIndex(dbPath, embeddings);
        var results = vectorIndex.Search(queryVector, parseResult.GetValue(ssTopOption));
        
        // Display results...
    }
});
```

Add corresponding MCP tool `cg_semantic_search` and slash command.

**Test Strategy:**

Test with hash-based DB shows warning. Test with mocked OpenAI engine produces semantic results. Test query embedding uses same engine as stored embeddings. Test dimension mismatch handling. Verify results are sorted by relevance score.

## Subtasks

### 45.1. Create semantic-search CLI command with options and metadata-based engine detection

**Status:** pending  
**Dependencies:** None  

Add a new `semantic-search` command to Program.cs with --query (required), --top, --db, and --format options. The command handler should open the database, read the `embedding_engine` metadata key from the Metadata table (added by task 44), and use it to determine which embedding engine was used during analysis. Follow the existing search command pattern (lines 716-808) for structure and output formatting.

**Details:**

1. Define the command and options in Program.cs following the existing search command pattern:
   - `new Command("semantic-search", "Search code by semantic meaning (requires LLM embeddings)")`
   - `--query` (string, required): Natural language search query
   - `--top` (int, default 10): Number of results
   - `--db` (string, default "./ai-code-graph/graph.db"): Database path
   - `--format` (string, default "table"): Output format (table|json)
2. In the command handler, open the database with StorageService.OpenAsync()
3. Read the embedding engine type via `storage.GetMetadataAsync("embedding_engine", ct)` to detect which engine was used during analysis
4. Add the command to the root command in Program.cs
5. Format and display search results using the same table/json output pattern as the existing search command, including method ID, similarity score, and method name columns

### 45.2. Implement engine recreation from metadata and query embedding generation

**Status:** pending  
**Dependencies:** 45.1  

Based on the embedding_engine metadata value detected in subtask 1, recreate the matching IEmbeddingEngine instance (HashEmbeddingEngine, OpenAI, or ONNX) to generate query embeddings that are compatible with the stored embeddings. Use VectorIndex for similarity search against stored embeddings. Include warning logic when hash-based embeddings are detected.

**Details:**

1. Implement engine factory logic using a switch expression on the metadata value:
   - `"hash"` or null → `new HashEmbeddingEngine()` (with yellow console warning about non-semantic results)
   - `"openai"` → Create OpenAI embedding engine matching stored configuration (read API key, model name from metadata)
   - `"onnx"` → Create ONNX embedding engine matching stored configuration (read model path from metadata)
2. Add warning output for hash-based engines:
   - Set `Console.ForegroundColor = ConsoleColor.Yellow`
   - Write to stderr: "Warning: Database uses hash-based embeddings. Results may not be semantically meaningful."
   - Write to stderr: "Re-analyze with --embedding-engine openai for true semantic search."
   - Reset color, but still proceed with search
3. Generate query embedding: `engine.GenerateEmbedding(query)`
4. Load all stored embeddings via `storage.GetEmbeddingsAsync(ct)`
5. Build or reuse VectorIndex (follow the existing pattern with `index.BuildIndex(embeddings)` and `index.Search(queryVector, top)`)
6. Handle dimension mismatch between query embedding and stored embeddings gracefully with an error message
7. Return results sorted by descending similarity score

### 45.3. Add MCP tool, slash command, and tests for semantic-search

**Status:** pending  
**Dependencies:** 45.1, 45.2  

Register a `cg_semantic_search` tool in McpServer.cs following the existing cg_search_code pattern, create a slash command file at .claude/commands/cg:semantic-search.md, and add unit/integration tests for the new command covering hash-based warnings, engine detection, and result formatting.

**Details:**

1. **MCP Tool** (McpServer.cs):
   - Add `cg_semantic_search` tool definition in `HandleToolsList()` with parameters: query (string, required), top (integer, optional, default 5)
   - Add handler case in `HandleToolCall()` that replicates the CLI semantic-search logic
   - Include the hash-based warning in the MCP response text when applicable
   - Return results as formatted text table matching other tool outputs

2. **Slash Command** (.claude/commands/cg:semantic-search.md):
   - Create command file following existing slash command patterns
   - Include description: "Search code by semantic meaning using LLM embeddings"
   - Document that it requires LLM embeddings for best results
   - Include usage steps and example invocation

3. **Tests** (AiCodeGraph.Tests/):
   - Test semantic-search with hash-based DB: verify warning message appears in stderr
   - Test semantic-search with mocked OpenAI engine metadata: verify no warning, correct engine instantiation
   - Test that query embedding uses the same engine type as stored embeddings
   - Test dimension mismatch handling returns appropriate error
   - Test JSON output format contains required fields (methodId, score, name)
   - Test table output format is properly aligned
   - Follow existing test patterns (e.g., SearchCommandTests naming convention)
