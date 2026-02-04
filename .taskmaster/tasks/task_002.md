# Task ID: 2

**Title:** Implement Roslyn Workspace Loader

**Status:** done

**Dependencies:** 1 ✓

**Priority:** high

**Description:** Build the component that loads a .NET solution file using MSBuildWorkspace, compiles all projects, and produces semantic models for analysis. This is the foundation for all subsequent analysis steps.

**Details:**

1. Create `WorkspaceLoader` class in Core project:
   ```csharp
   public class WorkspaceLoader : IDisposable
   {
       public async Task<LoadedWorkspace> LoadSolutionAsync(string solutionPath, CancellationToken ct)
       {
           MSBuildLocator.RegisterDefaults();
           var workspace = MSBuildWorkspace.Create();
           var solution = await workspace.OpenSolutionAsync(solutionPath, ct);
           // Compile all projects, collect diagnostics
           // Return LoadedWorkspace with Solution + Compilations
       }
   }
   ```
2. Create `LoadedWorkspace` record holding Solution and per-project Compilation objects
3. Handle workspace diagnostics and failed project loads gracefully (log warnings, continue)
4. Implement solution file discovery: search current directory and parent directories for `.sln` files
5. Register MSBuild instance using `MSBuildLocator.RegisterDefaults()` before any workspace operations
6. Support passing explicit solution path or auto-discovery
7. Add progress reporting for large solutions (project count, compilation status)

**Test Strategy:**

Create a minimal test solution fixture (2 projects, 3-4 classes) in the test project. Verify WorkspaceLoader can open the solution, compile projects, and return valid Compilation objects. Test error handling when solution file doesn't exist. Test auto-discovery logic.

## Subtasks

### 2.1. Create LoadedWorkspace Model and WorkspaceLoader Class Skeleton with MSBuildLocator Registration

**Status:** done  
**Dependencies:** None  

Define the LoadedWorkspace record type to hold Solution and per-project Compilation objects, and create the WorkspaceLoader class skeleton with proper MSBuildLocator.RegisterDefaults() initialization that must occur before any Roslyn/MSBuild types are loaded.

**Details:**

1. Create `LoadedWorkspace` record in the Core project:
   ```csharp
   public record LoadedWorkspace(
       Solution Solution,
       IReadOnlyDictionary<ProjectId, Compilation> Compilations,
       IReadOnlyList<WorkspaceDiagnostic> Diagnostics
   );
   ```
   Include a `WorkspaceDiagnostic` record to capture project-level warnings/errors.

2. Create `WorkspaceLoader` class implementing `IDisposable`:
   - Add a static initializer or guard that calls `MSBuildLocator.RegisterDefaults()` exactly once before any MSBuild/Roslyn workspace types are referenced.
   - Use a static bool flag (`_msbuildRegistered`) to prevent double-registration.
   - CRITICAL: The MSBuildLocator call must happen in a method that does NOT reference any `Microsoft.CodeAnalysis.MSBuild` types directly, to avoid assembly load failures. Use a separate initialization method or static constructor pattern.
   - Add NuGet references: `Microsoft.Build.Locator`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.CodeAnalysis.CSharp`.

3. Define the public API surface:
   ```csharp
   public class WorkspaceLoader : IDisposable
   {
       private MSBuildWorkspace? _workspace;
       public async Task<LoadedWorkspace> LoadSolutionAsync(string solutionPath, CancellationToken ct = default);
       public void Dispose();
   }
   ```

### 2.2. Implement Solution Loading with MSBuildWorkspace and Project Compilation

**Status:** done  
**Dependencies:** 2.1  

Implement the core LoadSolutionAsync method that opens a solution file using MSBuildWorkspace.OpenSolutionAsync, iterates all projects, compiles each one, and populates the LoadedWorkspace with Solution and Compilation objects.

**Details:**

1. Implement `LoadSolutionAsync`:
   ```csharp
   public async Task<LoadedWorkspace> LoadSolutionAsync(string solutionPath, CancellationToken ct)
   {
       EnsureMSBuildRegistered();
       _workspace = MSBuildWorkspace.Create();
       var solution = await _workspace.OpenSolutionAsync(solutionPath, ct);
       
       var compilations = new Dictionary<ProjectId, Compilation>();
       var diagnostics = new List<WorkspaceDiagnostic>();
       
       foreach (var project in solution.Projects)
       {
           ct.ThrowIfCancellationRequested();
           var compilation = await project.GetCompilationAsync(ct);
           if (compilation != null)
               compilations[project.Id] = compilation;
       }
       
       return new LoadedWorkspace(solution, compilations.AsReadOnly(), diagnostics.AsReadOnly());
   }
   ```

2. Handle the workspace's `WorkspaceFailed` event to capture MSBuild diagnostic messages during solution load.

3. Validate the solution path exists before attempting to open (throw `FileNotFoundException` with clear message).

4. Ensure proper disposal of MSBuildWorkspace in the `Dispose` method.

5. Consider topological ordering of projects for compilation (respecting project references), though Roslyn generally handles this internally via `GetCompilationAsync`.

### 2.3. Implement Diagnostics Handling and Graceful Error Recovery for Failed Project Loads

**Status:** done  
**Dependencies:** 2.2  

Add comprehensive diagnostics collection from MSBuildWorkspace events and handle failed project compilations gracefully by logging warnings and continuing with available projects rather than throwing.

**Details:**

1. Subscribe to `MSBuildWorkspace.WorkspaceFailed` event before opening the solution:
   ```csharp
   _workspace.WorkspaceFailed += (sender, args) =>
   {
       diagnostics.Add(new WorkspaceDiagnostic(
           args.Diagnostic.Kind,
           args.Diagnostic.Message,
           projectName: null
       ));
   };
   ```

2. Wrap individual project compilation in try-catch:
   ```csharp
   foreach (var project in solution.Projects)
   {
       try
       {
           var compilation = await project.GetCompilationAsync(ct);
           if (compilation != null)
           {
               // Check for critical compilation errors
               var errors = compilation.GetDiagnostics()
                   .Where(d => d.Severity == DiagnosticSeverity.Error)
                   .ToList();
               if (errors.Any())
                   diagnostics.Add(new WorkspaceDiagnostic(...));
               compilations[project.Id] = compilation;
           }
       }
       catch (Exception ex)
       {
           diagnostics.Add(new WorkspaceDiagnostic(
               WorkspaceDiagnosticKind.Failure,
               $"Failed to compile {project.Name}: {ex.Message}",
               project.Name
           ));
       }
   }
   ```

3. Define `WorkspaceDiagnostic` record:
   ```csharp
   public record WorkspaceDiagnostic(
       WorkspaceDiagnosticKind Kind,
       string Message,
       string? ProjectName
   );
   ```

4. Add a `HasErrors` property on LoadedWorkspace that returns true if any projects failed to compile.

5. Log diagnostic summary to ILogger (inject via constructor) at appropriate levels (Warning for recoverable, Error for failures).

### 2.4. Implement Solution File Auto-Discovery Logic

**Status:** done  
**Dependencies:** 2.1  

Implement the logic to automatically discover .sln files by searching the current directory and walking up parent directories, supporting both explicit path specification and auto-discovery with clear error messages when no solution is found.

**Details:**

1. Create a `SolutionDiscovery` static class or method:
   ```csharp
   public static class SolutionDiscovery
   {
       public static string FindSolutionFile(string? explicitPath = null, string? startDirectory = null)
       {
           if (!string.IsNullOrEmpty(explicitPath))
           {
               if (!File.Exists(explicitPath))
                   throw new FileNotFoundException($"Solution file not found: {explicitPath}");
               return Path.GetFullPath(explicitPath);
           }
           
           var searchDir = startDirectory ?? Directory.GetCurrentDirectory();
           return SearchForSolution(searchDir)
               ?? throw new FileNotFoundException(
                   $"No .sln file found in {searchDir} or any parent directory.");
       }
       
       private static string? SearchForSolution(string startDir)
       {
           var dir = new DirectoryInfo(startDir);
           while (dir != null)
           {
               var slnFiles = dir.GetFiles("*.sln");
               if (slnFiles.Length == 1)
                   return slnFiles[0].FullName;
               if (slnFiles.Length > 1)
                   throw new InvalidOperationException(
                       $"Multiple .sln files found in {dir.FullName}. Please specify one explicitly.");
               dir = dir.Parent;
           }
           return null;
       }
   }
   ```

2. Handle edge cases:
   - Multiple .sln files in same directory: throw with helpful message listing found files
   - Traversal stops at filesystem root
   - Symlinks and junctions should be followed normally

3. Integrate with WorkspaceLoader: add an overload or modify `LoadSolutionAsync` to accept nullable path and use auto-discovery when null.

### 2.5. Add Progress Reporting and Cancellation Token Support

**Status:** done  
**Dependencies:** 2.2, 2.3  

Implement IProgress<T> based progress reporting for solution loading and compilation phases, and ensure CancellationToken is properly threaded through all async operations to support responsive cancellation of long-running solution loads.

**Details:**

1. Define a progress reporting model:
   ```csharp
   public record WorkspaceLoadProgress(
       string Phase,          // "Loading", "Compiling", "Complete"
       string? ProjectName,
       int CurrentProject,
       int TotalProjects
   );
   ```

2. Add `IProgress<WorkspaceLoadProgress>?` parameter to `LoadSolutionAsync`:
   ```csharp
   public async Task<LoadedWorkspace> LoadSolutionAsync(
       string solutionPath,
       IProgress<WorkspaceLoadProgress>? progress = null,
       CancellationToken ct = default)
   ```

3. Report progress at key points:
   - After solution is opened: report total project count
   - Before each project compilation: report project name and index
   - After all compilations: report completion
   ```csharp
   progress?.Report(new WorkspaceLoadProgress(
       "Compiling", project.Name, index + 1, totalProjects));
   ```

4. Ensure CancellationToken is passed to:
   - `OpenSolutionAsync(solutionPath, ct)`
   - `project.GetCompilationAsync(ct)`
   - Checked between project iterations: `ct.ThrowIfCancellationRequested()`

5. Add a console-friendly progress reporter implementation for CLI usage:
   ```csharp
   public class ConsoleProgressReporter : IProgress<WorkspaceLoadProgress>
   {
       public void Report(WorkspaceLoadProgress value)
       {
           Console.WriteLine($"[{value.CurrentProject}/{value.TotalProjects}] {value.Phase}: {value.ProjectName}");
       }
   }
   ```
