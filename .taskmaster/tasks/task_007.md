# Task ID: 7

**Title:** Implement CLI Framework and Analyze Command

**Status:** done

**Dependencies:** 2 ✓, 3 ✓, 4 ✓, 5 ✓, 6 ✓

**Priority:** high

**Description:** Set up the CLI framework using System.CommandLine and implement the primary `analyze` command that orchestrates the full analysis pipeline: load workspace, extract model, build call graph, compute metrics, and store results.

**Details:**

1. Set up CLI with `System.CommandLine`:
   ```csharp
   var rootCommand = new RootCommand("AI Code Graph - Semantic code analysis for .NET");
   
   var analyzeCommand = new Command("analyze", "Analyze the codebase and build the code graph");
   analyzeCommand.AddOption(new Option<string>("--solution", "Path to .sln file"));
   analyzeCommand.AddOption(new Option<string>("--output", () => "./ai-code-graph", "Output directory"));
   analyzeCommand.AddOption(new Option<bool>("--verbose", "Enable verbose output"));
   ```
2. Create `AnalyzeCommand` handler that orchestrates:
   ```csharp
   async Task<int> ExecuteAnalyze(string? solutionPath, string output, bool verbose)
   {
       // 1. Discover/validate solution
       // 2. Load workspace (WorkspaceLoader)
       // 3. Extract code model (CodeModelExtractor)
       // 4. Build call graph (CallGraphBuilder)
       // 5. Compute metrics (MetricsEngine)
       // 6. Store results (StorageService)
       // 7. Report summary statistics
   }
   ```
3. Add progress reporting with elapsed time
4. Add summary output:
   ```
   Analysis complete:
     Projects: 5
     Types: 234
     Methods: 1,456
     Call edges: 3,892
     Avg complexity: 4.2
     Duration: 12.3s
   ```
5. Handle errors gracefully with user-friendly messages
6. Create output directory if it doesn't exist
7. Return exit code 0 on success, non-zero on failure

**Test Strategy:**

Integration test: run analyze command against the test fixture solution, verify database is created with correct data. Test solution auto-discovery. Test --verbose flag produces additional output. Test error cases: missing solution, invalid path, compilation errors in target. Verify exit codes.

## Subtasks

### 7.1. Set up System.CommandLine with RootCommand and CLI infrastructure

**Status:** done  
**Dependencies:** None  

Install the System.CommandLine NuGet package and create the basic CLI infrastructure including RootCommand with description, version info, and help text generation. Set up Program.cs as the entry point that builds and invokes the command tree.

**Details:**

Add System.CommandLine NuGet package to the CLI project. Create Program.cs with a RootCommand configured with description 'AI Code Graph - Semantic code analysis for .NET'. Configure the command builder to include automatic help (--help) and version (--version) options. Set up the async Main method to invoke rootCommand.InvokeAsync(args). Ensure the CLI project references the Core project for access to services.

### 7.2. Implement analyze command definition with options

**Status:** done  
**Dependencies:** 7.1  

Define the 'analyze' command and register its options: --solution (path to .sln file), --output (output directory with default './ai-code-graph'), and --verbose (enable verbose output). Add the command to the root command tree.

**Details:**

Create the analyze Command instance with description 'Analyze the codebase and build the code graph'. Add Option<string>('--solution', 'Path to .sln file') as optional. Add Option<string>('--output', default './ai-code-graph', 'Output directory'). Add Option<bool>('--verbose', 'Enable verbose output'). Register the command with rootCommand.AddCommand(analyzeCommand). Set up the command handler binding to wire options to the handler method parameters using SetHandler.

### 7.3. Implement AnalyzeCommand handler orchestrating the full pipeline

**Status:** done  
**Dependencies:** 7.2  

Create the ExecuteAnalyze handler method that orchestrates the complete analysis pipeline: discover/validate solution, load workspace via WorkspaceLoader, extract code model via CodeModelExtractor, build call graph via CallGraphBuilder, compute metrics via MetricsEngine, and store results via StorageService.

**Details:**

Implement async Task<int> ExecuteAnalyze(string? solutionPath, string output, bool verbose) method. Step 1: If solutionPath is null, auto-discover .sln file in current directory. Validate the solution file exists. Step 2: Create WorkspaceLoader and call LoadSolutionAsync. Step 3: Create CodeModelExtractor and extract the code model from the loaded workspace. Step 4: Create CallGraphBuilder and build the call graph from the code model. Step 5: Create MetricsEngine and compute metrics. Step 6: Create output directory if not exists, then use StorageService to persist results. Wire up DI or manual instantiation of each service. Pass CancellationToken through the pipeline for graceful cancellation support.

### 7.4. Add progress reporting and summary statistics output

**Status:** done  
**Dependencies:** 7.3  

Implement progress reporting during pipeline execution showing elapsed time for each stage, and display a formatted summary upon completion including counts of projects, types, methods, call edges, average complexity, and total duration.

**Details:**

Use a Stopwatch to track total elapsed time and per-stage timing. Print progress messages to Console during each pipeline stage (e.g., 'Loading workspace...', 'Extracting code model...', 'Building call graph...', 'Computing metrics...', 'Storing results...'). When verbose mode is enabled, print additional detail such as per-project compilation status. After pipeline completion, print formatted summary: 'Analysis complete:\n  Projects: {count}\n  Types: {count}\n  Methods: {count}\n  Call edges: {count}\n  Avg complexity: {value:F1}\n  Duration: {elapsed:F1}s'. Extract counts from the code model and metrics results.

### 7.5. Implement error handling, exit codes, and output directory management

**Status:** done  
**Dependencies:** 7.3  

Add comprehensive error handling throughout the pipeline with user-friendly error messages, proper exit codes (0 for success, non-zero for failure), and automatic creation of the output directory if it doesn't exist.

**Details:**

Wrap the pipeline execution in try-catch blocks to handle common failure modes: FileNotFoundException when solution doesn't exist, InvalidOperationException for workspace loading failures, and general exceptions. Print user-friendly error messages to Console.Error (e.g., 'Error: Solution file not found: {path}'). In verbose mode, include stack traces. Return exit code 0 on success, 1 for general errors, 2 for invalid arguments. Before storing results, call Directory.CreateDirectory(output) to ensure the output directory exists. Handle the case where the output path is invalid or inaccessible. Add a top-level exception handler in Program.cs to catch unhandled exceptions and return a non-zero exit code with a message.
