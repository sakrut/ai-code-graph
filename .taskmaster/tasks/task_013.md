# Task ID: 13

**Title:** Implement Duplicates CLI Command

**Status:** done

**Dependencies:** 8 ✓, 11 ✓

**Priority:** medium

**Description:** Add CLI `duplicates` command to query and display detected code duplicates, supporting filtering by concept/cluster and different output formats.

**Details:**

1. **`duplicates` command:**
   ```csharp
   var duplicatesCmd = new Command("duplicates", "Show detected code duplicates");
   duplicatesCmd.AddOption(new Option<string>("--concept", "Filter by intent cluster label"));
   duplicatesCmd.AddOption(new Option<float>("--threshold", () => 0.8f, "Minimum similarity"));
   duplicatesCmd.AddOption(new Option<string>("--type", () => "all", "structural|semantic|all"));
   duplicatesCmd.AddOption(new Option<int>("--top", () => 20, "Number of results"));
   duplicatesCmd.AddOption(new Option<string>("--format", () => "table", "table|json"));
   ```
2. Query pipeline:
   - Load clone pairs from SQLite
   - Filter by concept (match against intent cluster labels)
   - Filter by type (structural, semantic, or both)
   - Filter by threshold
   - Sort by hybrid score descending
3. Table output:
   ```
   Score  Type        Method A                        Method B
   0.95   structural  OrderService.Validate           CartService.Validate
   0.91   semantic    PermissionCheck.HasAccess       AuthGuard.VerifyPermission
   ```
4. JSON output includes full method details and cluster associations
5. **`export` command:**
   ```csharp
   var exportCmd = new Command("export", "Export code graph data");
   exportCmd.AddOption(new Option<string>("--concept", "Filter by concept"));
   exportCmd.AddOption(new Option<string>("--format", () => "json", "json|csv"));
   ```
   - Export filtered subsets of the code graph
   - Include methods, relationships, metrics for the specified concept
6. All output deterministic and sorted

**Test Strategy:**

Integration tests with pre-populated database containing known duplicates. Test concept filtering returns only matching clusters. Test type filtering correctly separates structural and semantic clones. Test threshold filtering. Test JSON output is valid and deterministic. Test export command produces correct subset.

## Subtasks

### 13.1. Implement duplicates command definition with System.CommandLine options

**Status:** pending  
**Dependencies:** None  

Define the `duplicates` CLI command using System.CommandLine with all required options: --concept (string, filter by intent cluster label), --threshold (float, default 0.8), --type (string, default 'all', accepts structural|semantic|all), --top (int, default 20), and --format (string, default 'table', accepts table|json). Wire the command handler to parse and validate these options before passing them to the query layer.

**Details:**

Create DuplicatesCommand.cs in AiCodeGraph.Cli/Commands/. Define the command with `new Command("duplicates", "Show detected code duplicates")`. Add each option with proper types, default values, and descriptions. Register a SetHandler that receives all option values, validates the --type value against allowed values (structural|semantic|all) and --format against (table|json), and invokes the query pipeline. Add the command to the root command in Program.cs. Follow the same pattern established by SearchCommand and other query commands (Task 12). Return exit code 2 for invalid arguments, 0 for success, 1 for runtime errors.

### 13.2. Implement query logic for SQLite clone pairs with filtering and sorting

**Status:** pending  
**Dependencies:** 13.1  

Build the query pipeline that loads clone pairs from the SQLite database (ClonePairs table) and applies concept, type, and threshold filters. Sort results by HybridScore descending and limit to the --top count. Concept filtering matches against IntentClusters labels via the MethodClusterMap join.

**Details:**

Create a DuplicatesQueryService (or add methods to an existing repository class) that accepts filter parameters (concept, type, threshold, top). Query ClonePairs table with: (1) WHERE HybridScore >= threshold for --threshold filtering, (2) WHERE CloneType = type for --type filtering (skip if 'all'), (3) JOIN MethodClusterMap and IntentClusters to filter by cluster label using LIKE '%concept%' or exact match for --concept filtering. ORDER BY HybridScore DESC, then by MethodIdA, MethodIdB for deterministic tiebreaking. LIMIT to --top results. Enrich each clone pair with method FullName from the Methods table for display. Return a list of DuplicateResult records containing: MethodAId, MethodAFullName, MethodBId, MethodBFullName, HybridScore, CloneType, and optionally cluster label.

### 13.3. Implement export command for filtered code graph subsets

**Status:** pending  
**Dependencies:** 13.2  

Add the `export` CLI command that exports filtered subsets of the code graph data. Supports --concept (filter by concept/cluster) and --format (json|csv). Exports methods, relationships, and metrics for the specified concept cluster.

**Details:**

Create ExportCommand.cs in AiCodeGraph.Cli/Commands/. Define with `new Command("export", "Export code graph data")` and options: --concept (string, optional filter), --format (string, default 'json', accepts json|csv). The handler queries methods belonging to the specified concept cluster (via MethodClusterMap + IntentClusters), then loads their call graph edges (MethodCalls where caller or callee is in the set), and their metrics from the Metrics table. For JSON output: serialize a structured object with { methods[], relationships[], metrics[], cluster } using System.Text.Json with camelCase and sorted keys. For CSV output: produce a methods CSV with columns (Id, FullName, FilePath, Line, Complexity, ClusterLabel) and optionally a relationships CSV. If --concept is omitted, export all data. Ensure deterministic ordering by method ID.

### 13.4. Implement table and JSON output formatting for duplicates and export commands

**Status:** pending  
**Dependencies:** 13.2, 13.3  

Implement the output formatting layer for both commands: table format displays a columnar view (Score, Type, Method A, Method B) for duplicates; JSON format outputs full method details with cluster associations. Ensure all output is deterministic and sorted.

**Details:**

Use the shared TableFormatter (from Task 8.5) to render duplicates results as: Score (formatted to 2 decimal places), Type (structural|semantic), Method A (FullName, truncated if needed), Method B (FullName, truncated if needed). Column widths should auto-size based on content with max constraints. For JSON output of duplicates: serialize array of objects with { score, type, methodA: { id, fullName, filePath, line }, methodB: { id, fullName, filePath, line }, cluster } using System.Text.Json with WriteIndented, camelCase naming, and alphabetically sorted properties. For export JSON: produce { methods[], relationships[], metrics[] } with consistent ordering. All arrays sorted by primary key (method ID or score descending then IDs). Ensure no floating-point formatting inconsistencies (use InvariantCulture). Write to Console.Out for pipe-friendliness.
