# Task ID: 15

**Title:** End-to-End Integration Testing and Performance Optimization

**Status:** done

**Dependencies:** 7 ✓, 8 ✓, 12 ✓, 13 ✓, 14 ✓

**Priority:** medium

**Description:** Create comprehensive integration tests running the full pipeline against a realistic test codebase, optimize performance to meet the 2-minute requirement, and ensure deterministic output.

**Details:**

1. **Test Fixture:**
   - Create a realistic test solution with 50+ classes, 200+ methods
   - Include various patterns: services, handlers, repositories, controllers
   - Include known duplicates, complex methods, interface hierarchies
   - Place in `tests/fixtures/TestSolution/`

2. **Integration Test Suite:**
   ```csharp
   [Fact]
   public async Task FullPipeline_ProducesExpectedGraph()
   {
       // Run full analyze command
       // Verify database contains expected counts
       // Verify call graph edges are correct
       // Verify metrics are computed
       // Verify clusters are formed
   }
   
   [Fact]
   public async Task SearchCommand_ReturnsRelevantResults()
   [Fact]
   public async Task DuplicatesCommand_FindsKnownClones()
   [Fact]
   public async Task DriftCommand_DetectsRegressions()
   [Fact]
   public async Task Output_IsDeterministic()
   ```
3. **Determinism Verification:**
   - Run analysis twice on same codebase
   - Compare database contents (should be identical)
   - Compare JSON outputs (should be byte-identical)

4. **Performance Optimization:**
   - Profile with `dotnet-trace` on large solution
   - Parallelize: compilation, metric computation, embedding generation
   - Use `Parallel.ForEachAsync` for independent method analysis
   - Batch SQLite inserts with transactions
   - Lazy-load embedding model (only when needed)
   - Target: complete analysis of 2000-method codebase in <2 minutes

5. **CI Integration:**
   - Add GitHub Actions workflow
   - Run tests on PR
   - Verify tool packaging works

6. **Documentation:**
   - Update README with usage examples
   - Document all CLI commands with `--help` text
   - Add architecture decision records for key choices

**Test Strategy:**

Run full integration suite against fixture solution. Measure and assert execution time <2 minutes for fixture. Run analysis twice and diff outputs for determinism. Test all CLI commands produce valid output. Test global tool installation and invocation. Run on CI to verify cross-platform compatibility.

## Subtasks

### 15.1. Create Realistic Test Fixture Solution with 50+ Classes and 200+ Methods

**Status:** pending  
**Dependencies:** None  

Build a comprehensive test solution in tests/fixtures/TestSolution/ containing realistic C# code patterns including services, handlers, repositories, controllers, interface hierarchies, known duplicate methods, and methods with varying cognitive complexity levels.

**Details:**

Create a complete .NET solution structure under tests/fixtures/TestSolution/ with:

1. **Project structure:** TestSolution.sln with at least 3 projects (Core, Services, Api)
2. **Service layer (15+ classes):** OrderService, UserService, PaymentService, NotificationService, etc. with realistic business logic methods
3. **Repository layer (10+ classes):** IOrderRepository/OrderRepository patterns with CRUD operations
4. **Controller layer (8+ classes):** REST-style controllers delegating to services
5. **Handler layer (8+ classes):** Command/query handlers (CQRS-style) like CreateOrderHandler, GetUserQueryHandler
6. **Interface hierarchies (10+ interfaces):** IRepository<T>, IService, IHandler<TRequest,TResponse> with multiple implementations
7. **Known duplicates (5+ pairs):** Intentionally duplicated methods across different classes (e.g., same validation logic in OrderService and PaymentService) for testing clone detection
8. **Complex methods (10+):** Methods with nested loops, multiple conditionals, try-catch blocks, and switch statements to produce known high cognitive complexity scores
9. **Simple methods (50+):** Properties, getters, simple CRUD delegations for baseline metrics
10. **Call graph patterns:** Ensure controller→service→repository call chains exist for call graph verification

Each class should have XML doc comments and realistic method signatures. Total target: 50+ classes, 200+ methods across the solution.

### 15.2. Implement Full Pipeline Integration Test (Analyze Command End-to-End)

**Status:** pending  
**Dependencies:** 15.1  

Create an integration test that runs the full analyze command against the test fixture solution and verifies the SQLite database contains expected counts for projects, types, methods, call graph edges, metrics, and clusters.

**Details:**

Create integration test class `FullPipelineTests` in AiCodeGraph.Tests:

1. **Test setup:** Use a fresh temp directory for the database, point analyzer at tests/fixtures/TestSolution/
2. **FullPipeline_ProducesExpectedGraph test:**
   - Run the full analyze command programmatically (invoke CLI or call core analysis directly)
   - Open the resulting SQLite database
   - Assert project count matches fixture (3 projects)
   - Assert type count >= 50
   - Assert method count >= 200
   - Assert call graph edges exist (verify known controller→service→repository chains)
   - Assert cognitive complexity metrics are computed for all methods
   - Assert methods with known high complexity have scores > 10
   - Assert namespace groupings are correct
   - Verify interface implementation relationships are captured
3. **Test teardown:** Clean up temp database files
4. **Helper methods:** Create `AssertDatabaseContains(db, expectedTypes, expectedMethods)` utilities
5. **Timeout:** Set test timeout to 3 minutes to allow for compilation overhead

Use xUnit [Fact] attributes and configure as integration test category.

### 15.3. Implement CLI Command Integration Tests (Search, Duplicates, Drift, Callgraph, Hotspots, Tree)

**Status:** pending  
**Dependencies:** 15.1, 15.2  

Create integration tests for each CLI command (search, duplicates, drift, callgraph, hotspots, tree) verifying they produce correct output against the pre-analyzed test fixture database.

**Details:**

Create test class `CommandIntegrationTests` with tests for each CLI command:

1. **SearchCommand_ReturnsRelevantResults:**
   - Analyze fixture first (or use pre-built database from shared fixture)
   - Run search with a known method name or keyword
   - Verify results contain expected methods ranked by relevance
   - Test with semantic search terms matching known patterns

2. **DuplicatesCommand_FindsKnownClones:**
   - Run duplicates command against analyzed fixture
   - Verify known intentional duplicates are detected
   - Verify similarity scores are above threshold
   - Verify output format is correct (JSON or table)

3. **DriftCommand_DetectsRegressions:**
   - Run analyze twice (baseline + current with a known change)
   - Run drift command
   - Verify new/removed/changed methods are reported

4. **CallgraphCommand_ShowsExpectedChains:**
   - Query callgraph for a known controller method
   - Verify it shows calls to service layer
   - Verify depth parameter works

5. **HotspotsCommand_RanksComplexMethods:**
   - Run hotspots command
   - Verify methods with known high complexity appear at top
   - Verify output includes complexity scores

6. **TreeCommand_ShowsHierarchy:**
   - Run tree command
   - Verify namespace→type→method hierarchy is displayed
   - Verify filtering by project works

Use a shared class fixture (IClassFixture<AnalyzedFixture>) to avoid re-analyzing for each test.

### 15.4. Implement Determinism Verification Tests

**Status:** pending  
**Dependencies:** 15.1, 15.2  

Create tests that run the full analysis pipeline twice on the same codebase and verify that database contents and JSON outputs are byte-identical, ensuring no non-deterministic behavior from parallelism, timestamps, or random ordering.

**Details:**

Create test class `DeterminismTests`:

1. **Output_IsDeterministic test:**
   - Run full analyze command on fixture → database A + JSON output A
   - Run full analyze command on fixture again → database B + JSON output B
   - Compare all table contents from database A vs B:
     - Export each table sorted by primary key
     - Assert row counts are identical
     - Assert all column values are identical
   - Compare JSON outputs byte-for-byte using string comparison
   - If embeddings are generated, verify they produce same vectors for same input

2. **Identify and fix non-determinism sources:**
   - Ensure method IDs are based on fully-qualified names (not hash of content that could vary)
   - Ensure call graph edges are sorted consistently
   - Ensure cluster assignments are deterministic (seed random if needed)
   - Ensure parallel processing doesn't affect output ordering
   - Remove any timestamp fields or make them fixed during test

3. **Helper utilities:**
   - `CompareDatabases(pathA, pathB)` - table-by-table comparison
   - `CompareJsonOutputs(jsonA, jsonB)` - structural comparison with detailed diff on failure
   - Provide clear error messages showing first difference found

4. **Run 3+ times** in CI to catch intermittent non-determinism from race conditions.

### 15.5. Profile and Optimize Performance with Parallelization and Batched SQLite Transactions

**Status:** pending  
**Dependencies:** 15.2  

Profile the analysis pipeline on a large solution, identify bottlenecks, and optimize using Parallel.ForEachAsync for independent method analysis, batched SQLite inserts with transactions, lazy-loaded embedding model, and parallelized compilation. Target: 2000-method codebase analyzed in under 2 minutes.

**Details:**

Performance optimization strategy:

1. **Profiling (establish baseline):**
   - Use `dotnet-trace` or BenchmarkDotNet to profile against test fixture
   - Identify top time consumers: compilation, syntax walking, metric computation, DB writes, embeddings
   - Create a performance test that measures and asserts total time

2. **Parallelize compilation:**
   - Use `Parallel.ForEachAsync` to compile multiple projects concurrently
   - Respect `MaxDegreeOfParallelism` (default to Environment.ProcessorCount)

3. **Parallelize method analysis:**
   - After compilation, process methods independently with `Parallel.ForEachAsync`
   - Each method's complexity calculation is independent
   - Use `ConcurrentBag<MethodMetrics>` or channels to collect results
   - Ensure Roslyn SemanticModel access is thread-safe (it is per-compilation)

4. **Batch SQLite inserts:**
   - Wrap inserts in transactions (BEGIN/COMMIT every 500-1000 rows)
   - Use parameterized prepared statements for bulk inserts
   - Consider WAL mode for concurrent reads during writes
   - Target: 5000 method inserts in <1 second

5. **Lazy-load embedding model:**
   - Only initialize ML model when search/embeddings actually requested
   - Use `Lazy<T>` or explicit initialization gate
   - Skip embedding generation during basic analyze if not needed

6. **Performance assertion test:**
   - `[Fact] public async Task FullAnalysis_CompletesWithin2Minutes()`
   - Use Stopwatch, assert elapsed < TimeSpan.FromMinutes(2)
   - Run against fixture solution (scale up if needed to 2000 methods)

### 15.6. Add CI Pipeline with GitHub Actions Workflow

**Status:** pending  
**Dependencies:** 15.2, 15.3, 15.4  

Create a GitHub Actions workflow that builds the solution, runs all tests (unit + integration), verifies global tool packaging, and runs determinism checks on every PR and push to main.

**Details:**

Create `.github/workflows/ci.yml`:

1. **Workflow triggers:**
   - `push` to main/master branches
   - `pull_request` to main/master branches

2. **Build job:**
   ```yaml
   - uses: actions/checkout@v4
   - uses: actions/setup-dotnet@v4
     with:
       dotnet-version: '8.0.x'
   - run: dotnet restore
   - run: dotnet build --no-restore --configuration Release
   ```

3. **Test job (depends on build):**
   - Run unit tests: `dotnet test --filter Category!=Integration`
   - Run integration tests: `dotnet test --filter Category=Integration`
   - Set timeout to 10 minutes for integration tests
   - Upload test results as artifacts

4. **Packaging verification job:**
   - `dotnet pack AiCodeGraph.Cli --configuration Release`
   - Install as global tool: `dotnet tool install --global --add-source ./nupkg AiCodeGraph.Cli`
   - Run `ai-code-graph --help` to verify it starts
   - Run `ai-code-graph --version` to verify version

5. **Matrix strategy:**
   - Test on ubuntu-latest, windows-latest, macos-latest
   - .NET 8.0

6. **Caching:**
   - Cache NuGet packages
   - Cache dotnet tools

7. **Status badge:** Add workflow status badge to README

### 15.7. Add CLI Help Text Documentation and Global Tool Packaging Verification

**Status:** pending  
**Dependencies:** 15.3  

Add comprehensive --help text for all CLI commands with usage examples, verify global tool packaging works correctly, and ensure the tool can be installed and invoked as a .NET global tool.

**Details:**

1. **CLI help text for each command:**
   - `ai-code-graph --help` - Overview of all commands, global options
   - `ai-code-graph analyze --help` - Solution path argument, options (--output, --skip-embeddings, --parallel)
   - `ai-code-graph search --help` - Query argument, options (--top, --threshold)
   - `ai-code-graph duplicates --help` - Options (--threshold, --min-lines, --format)
   - `ai-code-graph drift --help` - Options (--baseline, --format)
   - `ai-code-graph callgraph --help` - Method argument, options (--depth, --direction)
   - `ai-code-graph hotspots --help` - Options (--top, --sort-by, --min-complexity)
   - `ai-code-graph tree --help` - Options (--project, --namespace, --depth)

2. **Help text format (per command):**
   ```
   Description: <what the command does>
   Usage: ai-code-graph <command> [arguments] [options]
   Arguments:
     <arg>    Description
   Options:
     --opt    Description [default: value]
   Examples:
     ai-code-graph analyze ./MySolution.sln
     ai-code-graph hotspots --top 20 --sort-by complexity
   ```

3. **Global tool packaging verification:**
   - Ensure .csproj has PackAsTool=true, ToolCommandName=ai-code-graph
   - Test: `dotnet pack` → `dotnet tool install --global --add-source ./nupkg`
   - Verify: `ai-code-graph --version` outputs correct version
   - Verify: `ai-code-graph --help` outputs command list
   - Test uninstall: `dotnet tool uninstall -g ai-code-graph`

4. **Integration test for help text:**
   - Verify each command's --help exits with code 0
   - Verify output contains expected sections (Description, Usage, Options)
