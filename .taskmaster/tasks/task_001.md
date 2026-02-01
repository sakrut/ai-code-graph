# Task ID: 1

**Title:** Initialize .NET Solution and Project Structure

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Create the .NET solution with a CLI tool project (global tool), a core library project for analysis logic, and a test project. Set up the foundational project structure following .NET conventions.

**Details:**

1. Create solution: `dotnet new sln -n AiCodeGraph`
2. Create projects:
   - `dotnet new console -n AiCodeGraph.Cli` (the global tool)
   - `dotnet new classlib -n AiCodeGraph.Core` (analysis engine)
   - `dotnet new xunit -n AiCodeGraph.Tests` (unit tests)
3. Add projects to solution
4. Configure `AiCodeGraph.Cli.csproj` as a .NET Global Tool:
   ```xml
   <PackAsTool>true</PackAsTool>
   <ToolCommandName>ai-code-graph</ToolCommandName>
   ```
5. Add NuGet references to Core project:
   - `Microsoft.CodeAnalysis.CSharp.Workspaces` (Roslyn)
   - `Microsoft.CodeAnalysis.Workspaces.MSBuild` (MSBuild workspace)
   - `Microsoft.Build.Locator` (MSBuild discovery)
   - `Microsoft.Data.Sqlite` (SQLite storage)
   - `System.CommandLine` (CLI parsing)
6. Set up dependency injection in CLI project
7. Create output directory convention: `./ai-code-graph/`
8. Add `.gitignore` entries for build artifacts and `ai-code-graph/` output directory

**Test Strategy:**

Verify solution builds successfully with `dotnet build`. Verify the CLI project can be packed as a global tool with `dotnet pack`. Run `dotnet test` to confirm test project executes. Verify all NuGet packages resolve correctly.

## Subtasks

### 1.1. Create Solution and Projects with dotnet CLI

**Status:** done  
**Dependencies:** None  

Create the AiCodeGraph solution file and three projects (console CLI, class library Core, xunit Tests) using dotnet CLI templates, then add all projects to the solution.

**Details:**

Run the following commands in sequence:
1. `dotnet new sln -n AiCodeGraph` to create the solution file
2. `dotnet new console -n AiCodeGraph.Cli` to create the CLI console project
3. `dotnet new classlib -n AiCodeGraph.Core` to create the core analysis library
4. `dotnet new xunit -n AiCodeGraph.Tests` to create the test project
5. `dotnet sln AiCodeGraph.sln add AiCodeGraph.Cli/AiCodeGraph.Cli.csproj`
6. `dotnet sln AiCodeGraph.sln add AiCodeGraph.Core/AiCodeGraph.Core.csproj`
7. `dotnet sln AiCodeGraph.sln add AiCodeGraph.Tests/AiCodeGraph.Tests.csproj`
8. Add project references: CLI references Core, Tests references Core
   - `dotnet add AiCodeGraph.Cli reference AiCodeGraph.Core`
   - `dotnet add AiCodeGraph.Tests reference AiCodeGraph.Core`
9. Verify the solution builds with `dotnet build AiCodeGraph.sln`

### 1.2. Configure CLI Project as .NET Global Tool

**Status:** done  
**Dependencies:** 1.1  

Modify AiCodeGraph.Cli.csproj to configure it as a packable .NET global tool with the command name 'ai-code-graph'.

**Details:**

Edit `AiCodeGraph.Cli/AiCodeGraph.Cli.csproj` to add the following properties inside the main `<PropertyGroup>`:
```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>ai-code-graph</ToolCommandName>
<PackageOutputPath>./nupkg</PackageOutputPath>
```
Also set a reasonable `<Version>` (e.g., `0.1.0`), `<Description>`, and `<Authors>` for NuGet packaging metadata. Ensure the `<OutputType>Exe</OutputType>` is present (should be from the console template). Verify the tool can be packed with `dotnet pack AiCodeGraph.Cli/AiCodeGraph.Cli.csproj`.

### 1.3. Add NuGet Package References to Core Project

**Status:** done  
**Dependencies:** 1.1  

Add all required NuGet package references to AiCodeGraph.Core for Roslyn analysis, MSBuild workspace loading, SQLite storage, and CLI command parsing.

**Details:**

Add the following NuGet packages to `AiCodeGraph.Core/AiCodeGraph.Core.csproj`:
```bash
dotnet add AiCodeGraph.Core package Microsoft.CodeAnalysis.CSharp.Workspaces
dotnet add AiCodeGraph.Core package Microsoft.CodeAnalysis.Workspaces.MSBuild
dotnet add AiCodeGraph.Core package Microsoft.Build.Locator
dotnet add AiCodeGraph.Core package Microsoft.Data.Sqlite
dotnet add AiCodeGraph.Cli package System.CommandLine
```
Note: `System.CommandLine` goes in the CLI project since it handles CLI parsing. Ensure version compatibility between the Roslyn packages (use the same major version for all Microsoft.CodeAnalysis.* packages). Also add `Microsoft.Extensions.DependencyInjection` to Core for DI abstractions. Run `dotnet restore` to verify all packages resolve correctly.

### 1.4. Set Up Dependency Injection and Program.cs Structure

**Status:** done  
**Dependencies:** 1.2, 1.3  

Configure dependency injection in the CLI project's Program.cs with a service collection, register core services, and set up the System.CommandLine root command structure.

**Details:**

1. Add `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Hosting` packages to the CLI project.
2. Create the initial `Program.cs` in AiCodeGraph.Cli with:
   - A `ServiceCollection` setup registering core services (placeholder registrations for now)
   - A `RootCommand` from System.CommandLine with description
   - Basic command structure with an `analyze` subcommand accepting a solution path argument
   - Wire up DI container to command handlers
3. Create placeholder service interfaces in Core:
   - `IWorkspaceLoader` 
   - `ICodeGraphStorage`
4. Register services in the DI container
5. Ensure `Program.cs` follows the pattern:
```csharp
var services = new ServiceCollection();
// Register services
var serviceProvider = services.BuildServiceProvider();
var rootCommand = new RootCommand("AI Code Graph - Static analysis tool");
// Add commands
await rootCommand.InvokeAsync(args);
```

### 1.5. Update .gitignore and Create Output Directory Convention

**Status:** done  
**Dependencies:** 1.1  

Fix the existing .gitignore to not exclude .sln files, add entries for build artifacts and the ai-code-graph output directory, and establish the output directory convention.

**Details:**

1. **Fix .gitignore**: The existing `.gitignore` contains `*.sln` which incorrectly excludes the solution file. Remove the `*.sln` line or replace it with more specific exclusions (e.g., `*.suo`, `*.user`).
2. **Add .NET build artifact exclusions** (if not already present):
   ```
   bin/
   obj/
   *.user
   *.suo
   .vs/
   nupkg/
   ```
3. **Add output directory exclusion**:
   ```
   ai-code-graph/
   ```
4. **Create output directory convention**: Add a brief comment or note in the project that the default output path is `./ai-code-graph/` relative to the analyzed solution. This directory will hold the SQLite database and any generated reports.
5. Ensure the `.sln` file is properly tracked by git after fixing .gitignore (`git add AiCodeGraph.sln`).
