# Task ID: 12

**Title:** Implement Natural Language Search Command

**Status:** done

**Dependencies:** 10 ✓, 8 ✓

**Priority:** medium

**Description:** Add CLI `search` command that accepts natural language queries, generates embeddings for the query, and returns the most semantically similar methods from the codebase.

**Details:**

1. **`search` command:**
   ```csharp
   var searchCmd = new Command("search", "Search code by natural language intent");
   searchCmd.AddArgument(new Argument<string>("query", "Natural language search query"));
   searchCmd.AddOption(new Option<int>("--top", () => 10, "Number of results"));
   searchCmd.AddOption(new Option<float>("--threshold", () => 0.5f, "Minimum similarity score"));
   searchCmd.AddOption(new Option<string>("--format", () => "table", "table|json"));
   ```
2. Search pipeline:
   ```csharp
   async Task<int> ExecuteSearch(string query, int top, float threshold, string format)
   {
       // 1. Load embedding model
       // 2. Generate embedding for query text
       // 3. Load vector index from disk
       // 4. Perform kNN search
       // 5. Filter by threshold
       // 6. Enrich results with method metadata from SQLite
       // 7. Display results
   }
   ```
3. Result display (table format):
   ```
   Score  Method                              File:Line
   0.92   CustomerService.RemoveTag           Services/Customer.cs:45
   0.87   TagManager.DeleteCustomerTag        Managers/TagManager.cs:112
   0.81   CustomerTagHandler.Handle           Handlers/CustomerTag.cs:23
   ```
4. JSON format includes: methodId, fullName, score, filePath, line, cognitiveComplexity
5. Handle case where vector index doesn't exist (prompt user to run `analyze` first)
6. Support quoted exact phrases for boosting

**Test Strategy:**

Test with known queries against pre-built index: 'remove customer tag' should rank tag-removal methods highest. Test threshold filtering excludes low-similarity results. Test JSON output format is valid and contains all required fields. Test error case when index doesn't exist. Test with empty results.

## Subtasks

### 12.1. Implement search command definition with System.CommandLine

**Status:** pending  
**Dependencies:** None  

Define the `search` command using System.CommandLine with a required query argument and options for --top (default 10), --threshold (default 0.5f), and --format (default "table"). Wire the command handler to invoke the search pipeline. Include validation that the vector index exists on disk before proceeding, displaying a helpful error message prompting the user to run `analyze` first if missing.

**Details:**

Create the search command in the CLI entry point using System.CommandLine:
- Add `Argument<string>("query", "Natural language search query")`
- Add `Option<int>("--top", () => 10, "Number of results")`
- Add `Option<float>("--threshold", () => 0.5f, "Minimum similarity score")`
- Add `Option<string>("--format", () => "table", "table|json")`
- In the handler, check if the vector index file exists on disk before proceeding. If not, print an error: "No vector index found. Run 'analyze' first to build embeddings." and return exit code 1.
- Pass parsed arguments to the ExecuteSearch pipeline method.

### 12.2. Implement search pipeline: embed query and perform kNN search

**Status:** pending  
**Dependencies:** 12.1  

Implement the core search pipeline that loads the embedding model, generates an embedding vector for the user's query text, loads the persisted vector index from disk, performs kNN search to find the top-N most similar method embeddings, and filters results by the similarity threshold.

**Details:**

Implement the `ExecuteSearch` method's core logic:
1. Load the EmbeddingEngine (from Task 10) - instantiate or reuse the ONNX-based embedding model.
2. Call `GenerateEmbedding(query)` to get the query vector (float[] of 384 dimensions for MiniLM).
3. Load the persisted vector index from the expected disk path (the index built during the `analyze` command).
4. Perform kNN search with the query vector, requesting `top` results.
5. Filter the returned results by the `threshold` parameter, removing any entries with similarity score below the threshold.
6. Return the list of (methodId, score) pairs for downstream enrichment.

This is a linear pipeline that composes existing components from Task 10's EmbeddingEngine and vector index.

### 12.3. Implement result enrichment from SQLite metadata

**Status:** pending  
**Dependencies:** 12.2  

Join the vector search results (methodId + score pairs) with the SQLite database to enrich each result with full method metadata including qualified name, file path, line number, and cognitive complexity score.

**Details:**

After the kNN search returns a list of (methodId, score) pairs:
1. Open the SQLite database (from Task 4/8 infrastructure).
2. Query method metadata for each methodId - retrieve: fullName, filePath, lineNumber, cognitiveComplexity, returnType, and any other relevant fields.
3. Build enriched result objects containing: methodId, fullName, score, filePath, line, cognitiveComplexity.
4. Sort results by score descending (deterministic ordering - for equal scores, use fullName as tiebreaker).
5. Return the enriched result list for formatting.

Use a single batch query (WHERE id IN (...)) rather than N individual queries for efficiency.

### 12.4. Implement table and JSON output formatters

**Status:** pending  
**Dependencies:** 12.3  

Implement the two output formats for search results: a human-readable table format showing Score, Method, and File:Line columns, and a machine-readable JSON format containing all enriched fields. Selection is controlled by the --format option.

**Details:**

Implement output formatting based on the --format option value:

**Table format (default):**
- Print header: `Score  Method                              File:Line`
- For each result, print: `{score:F2}   {fullName padded}   {filePath}:{line}`
- Right-align scores, left-align method names with consistent column widths.
- If no results after filtering, print: "No results found above threshold {threshold}."

**JSON format:**
- Serialize the result list as a JSON array where each element contains: methodId, fullName, score, filePath, line, cognitiveComplexity.
- Use System.Text.Json with indented formatting.
- Ensure deterministic ordering (same as table: score desc, fullName tiebreaker).

Both formats should write to stdout. Return exit code 0 on success.
