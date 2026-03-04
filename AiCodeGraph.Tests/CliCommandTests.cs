using System.Diagnostics;
using System.Text.Json.Nodes;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class CliCommandTests : TempDirectoryFixture
{
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private static readonly string CliDll = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AiCodeGraph.Cli", "bin", BuildConfiguration, "net8.0", "AiCodeGraph.Cli.dll"));

    public CliCommandTests() : base("cli-test") { }

    private async Task<(int ExitCode, string Output, string Error)> RunCliAsync(string args, int timeoutMs = 10000, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{CliDll} {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        using var process = Process.Start(psi)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var completed = process.WaitForExit(timeoutMs);
        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"CLI command timed out: {args}");
        }

        var output = await outputTask;
        var error = await errorTask;
        return (process.ExitCode, output, error);
    }

    private async Task<string> CreateTestDbAsync()
    {
        var dbPath = Path.Combine(TempDir, "graph.db");
        await using var storage = new StorageService(dbPath);
        await storage.InitializeAsync();

        // Insert parent records and methods directly
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using (var fk = conn.CreateCommand())
        {
            fk.CommandText = "PRAGMA foreign_keys=OFF;";
            await fk.ExecuteNonQueryAsync();
        }
        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'TestNs', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'TestClass', 'TestNs.TestClass', 'ns1', 'class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine) VALUES
                ('TestNs.TestClass.HighComplexity()', 'HighComplexity', 'TestNs.TestClass.HighComplexity()', 'void', 'type1', 10, 60),
                ('TestNs.TestClass.LowComplexity()', 'LowComplexity', 'TestNs.TestClass.LowComplexity()', 'void', 'type1', 70, 80),
                ('TestNs.TestClass.MedComplexity()', 'MedComplexity', 'TestNs.TestClass.MedComplexity()', 'void', 'type1', 90, 120);
                """;
            await ins.ExecuteNonQueryAsync();
        }

        await storage.SaveMetricsAsync(new List<(string MethodId, int CognitiveComplexity, int LinesOfCode, int NestingDepth)>
        {
            ("TestNs.TestClass.HighComplexity()", 25, 50, 5),
            ("TestNs.TestClass.LowComplexity()", 2, 10, 1),
            ("TestNs.TestClass.MedComplexity()", 12, 30, 3)
        });

        await storage.SaveCallGraphAsync(new List<(string CallerId, string CalleeId)>
        {
            ("TestNs.TestClass.HighComplexity()", "TestNs.TestClass.LowComplexity()"),
            ("TestNs.TestClass.MedComplexity()", "TestNs.TestClass.LowComplexity()")
        });

        return dbPath;
    }

    // --- Help text tests ---

    [Theory]
    [InlineData("hotspots")]
    [InlineData("tree")]
    [InlineData("callgraph")]
    [InlineData("similar")]
    [InlineData("duplicates")]
    [InlineData("clusters")]
    [InlineData("export")]
    [InlineData("drift")]
    [InlineData("context")]
    [InlineData("impact")]
    [InlineData("dead-code")]
    [InlineData("analyze")]
    public async Task Command_Help_ShowsDescription(string commandName)
    {
        var (exitCode, output, _) = await RunCliAsync($"{commandName} --help");
        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output);
        Assert.Contains("Description:", output);
    }

    [Fact]
    public async Task RootCommand_Help_ListsAllCommands()
    {
        var (exitCode, output, _) = await RunCliAsync("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("hotspots", output);
        Assert.Contains("tree", output);
        Assert.Contains("callgraph", output);
        Assert.Contains("analyze", output);
    }

    // --- Missing DB error tests ---

    [Theory]
    [InlineData("hotspots")]
    [InlineData("tree")]
    [InlineData("duplicates")]
    [InlineData("clusters")]
    public async Task Command_MissingDb_WritesErrorToStderr(string commandName)
    {
        var fakePath = Path.Combine(TempDir, "nonexistent.db");
        var (_, _, error) = await RunCliAsync($"{commandName} --db {fakePath}");
        Assert.Contains("Error", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallgraphCommand_MissingDb_WritesErrorToStderr()
    {
        var fakePath = Path.Combine(TempDir, "nonexistent.db");
        var (_, _, error) = await RunCliAsync($"callgraph TestMethod --db {fakePath}");
        Assert.Contains("Error", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImpactCommand_MissingDb_WritesErrorToStderr()
    {
        var fakePath = Path.Combine(TempDir, "nonexistent.db");
        var (_, _, error) = await RunCliAsync($"impact TestMethod --db {fakePath}");
        Assert.Contains("Error", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DriftCommand_MissingBaseline_WritesErrorToStderr()
    {
        var dbPath = await CreateTestDbAsync();
        var fakeBaseline = Path.Combine(TempDir, "baseline.db");
        var (_, _, error) = await RunCliAsync($"drift --db {dbPath} --vs {fakeBaseline}");
        Assert.Contains("Error", error, StringComparison.OrdinalIgnoreCase);
    }

    // --- Valid DB tests ---

    [Fact]
    public async Task HotspotsCommand_WithValidDb_ShowsResults()
    {
        var dbPath = await CreateTestDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"hotspots --db {dbPath}");
        Assert.Equal(0, exitCode);
        Assert.Contains("HighComplexity", output);
    }

    [Fact]
    public async Task HotspotsCommand_JsonFormat_ReturnsValidJson()
    {
        var dbPath = await CreateTestDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"hotspots --db {dbPath} --format json");
        Assert.Equal(0, exitCode);
        Assert.Contains("\"items\"", output);
        Assert.Contains("\"complexity\"", output);
    }

    [Fact]
    public async Task HotspotsCommand_WithThreshold_FiltersResults()
    {
        var dbPath = await CreateTestDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"hotspots --db {dbPath} --threshold 20");
        Assert.Equal(0, exitCode);
        Assert.Contains("HighComplexity", output);
        Assert.DoesNotContain("LowComplexity", output);
    }

    [Fact]
    public async Task HotspotsCommand_TopOption_LimitsResults()
    {
        var dbPath = await CreateTestDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"hotspots --db {dbPath} --top 1 --format json");
        Assert.Equal(0, exitCode);
        Assert.Contains("\"complexity\": 25", output);
        // JSON format with top 1 should only have 1 item (count methodId occurrences, not complexity which appears in metadata too)
        var occurrences = output.Split("\"methodId\"").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task TreeCommand_WithValidDb_Succeeds()
    {
        var dbPath = await CreateTestDbAsync();
        var (exitCode, _, _) = await RunCliAsync($"tree --db {dbPath}");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task DeadCodeCommand_WithValidDb_Succeeds()
    {
        var dbPath = await CreateTestDbAsync();
        var (exitCode, _, _) = await RunCliAsync($"dead-code --db {dbPath}");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExportCommand_JsonFormat_ProducesOutput()
    {
        var dbPath = await CreateTestDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"export --db {dbPath} --format json");
        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output);
    }

    // --- Unrecognized option tests ---

    [Fact]
    public async Task Command_UnrecognizedOption_ReturnsNonZero()
    {
        var (exitCode, _, error) = await RunCliAsync("hotspots --nonexistent-option");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("Unrecognized", error);
    }

    [Fact]
    public async Task SetupCursorCommand_WithExistingServer_UpdatesDbPath()
    {
        var workspace = Path.Combine(TempDir, "workspace-update");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "rules"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph"));

        var mcpPath = Path.Combine(workspace, ".cursor", "mcp.json");
        await File.WriteAllTextAsync(
            mcpPath,
            """
            {
              "mcpServers": {
                "ai-code-graph": {
                  "type": "stdio",
                  "command": "ai-code-graph",
                  "args": ["mcp", "--db", "./old/graph.db"]
                }
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "rules", "ai-code-graph.mdc"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph", "SKILL.md"), "existing");

        var (exitCode, output, error) = await RunCliAsync("setup-cursor --db ./new/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains($"{Path.Combine(".cursor", "mcp.json")} (updated)", output);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mcpPath))!.AsObject();
        var args = root["mcpServers"]!["ai-code-graph"]!["args"]!.AsArray();
        Assert.Equal("mcp", args[0]!.GetValue<string>());
        Assert.Equal("--db", args[1]!.GetValue<string>());
        Assert.Equal("./new/graph.db", args[2]!.GetValue<string>());
    }

    [Fact]
    public async Task SetupCursorCommand_WithMatchingServer_NoChanges()
    {
        var workspace = Path.Combine(TempDir, "workspace-noop");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "rules"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph"));

        var mcpPath = Path.Combine(workspace, ".cursor", "mcp.json");
        await File.WriteAllTextAsync(
            mcpPath,
            """
            {
              "mcpServers": {
                "ai-code-graph": {
                  "type": "stdio",
                  "command": "ai-code-graph",
                  "args": ["mcp", "--db", "./same/graph.db"]
                }
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "rules", "ai-code-graph.mdc"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph", "SKILL.md"), "existing");

        var before = await File.ReadAllTextAsync(mcpPath);
        var (exitCode, output, error) = await RunCliAsync("setup-cursor --db ./same/graph.db", workingDirectory: workspace);
        var after = await File.ReadAllTextAsync(mcpPath);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains("Nothing to do.", output);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task SetupCursorCommand_WithMissingServer_AddsServerEntry()
    {
        var workspace = Path.Combine(TempDir, "workspace-insert");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "rules"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph"));

        var mcpPath = Path.Combine(workspace, ".cursor", "mcp.json");
        await File.WriteAllTextAsync(
            mcpPath,
            """
            {
              "mcpServers": {
                "other-server": {
                  "type": "stdio",
                  "command": "other-tool",
                  "args": ["run"]
                }
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "rules", "ai-code-graph.mdc"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph", "SKILL.md"), "existing");

        var (exitCode, output, error) = await RunCliAsync("setup-cursor --db ./inserted/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains($"{Path.Combine(".cursor", "mcp.json")} (updated)", output);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mcpPath))!.AsObject();
        var servers = root["mcpServers"]!.AsObject();
        Assert.NotNull(servers["other-server"]);
        Assert.NotNull(servers["ai-code-graph"]);
        Assert.Equal("./inserted/graph.db", servers["ai-code-graph"]!["args"]!.AsArray()[2]!.GetValue<string>());
    }

    [Fact]
    public async Task SetupCursorCommand_WithCustomServerFields_PreservesFieldsWhenUpdatingDbPath()
    {
        var workspace = Path.Combine(TempDir, "workspace-cursor-custom-fields");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "rules"));
        Directory.CreateDirectory(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph"));

        var mcpPath = Path.Combine(workspace, ".cursor", "mcp.json");
        await File.WriteAllTextAsync(
            mcpPath,
            """
            {
              "mcpServers": {
                "ai-code-graph": {
                  "type": "stdio",
                  "command": "ai-code-graph",
                  "args": ["mcp", "--db", "./old/graph.db", "--trace"],
                  "env": { "ASPNETCORE_ENVIRONMENT": "Development" },
                  "cwd": "./tools",
                  "wrapperOptions": { "retry": 3 }
                }
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "rules", "ai-code-graph.mdc"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".cursor", "skills", "ai-code-graph", "SKILL.md"), "existing");

        var (exitCode, output, error) = await RunCliAsync("setup-cursor --db ./new/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains($"{Path.Combine(".cursor", "mcp.json")} (updated)", output);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mcpPath))!.AsObject();
        var server = root["mcpServers"]!["ai-code-graph"]!.AsObject();
        var args = server["args"]!.AsArray();

        Assert.Equal("./new/graph.db", args[2]!.GetValue<string>());
        Assert.Equal("--trace", args[3]!.GetValue<string>());
        Assert.Equal("Development", server["env"]!["ASPNETCORE_ENVIRONMENT"]!.GetValue<string>());
        Assert.Equal("./tools", server["cwd"]!.GetValue<string>());
        Assert.Equal(3, server["wrapperOptions"]!["retry"]!.GetValue<int>());
    }

    [Fact]
    public async Task SetupCodexCommand_CleanWorkspace_CreatesScaffoldFiles()
    {
        var workspace = Path.Combine(TempDir, "workspace-codex-create");
        Directory.CreateDirectory(workspace);

        var (exitCode, output, error) = await RunCliAsync("setup-codex --db ./codex/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains(Path.Combine(".codex", "skills", "ai-code-graph", "SKILL.md"), output);
        Assert.Contains(Path.Combine(".codex", "skills", "ai-code-graph", "agents", "openai.yaml"), output);
        Assert.Contains(".mcp.json", output);
        Assert.Contains("AGENTS.md", output);

        var skillPath = Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "SKILL.md");
        var metadataPath = Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents", "openai.yaml");
        var mcpPath = Path.Combine(workspace, ".mcp.json");
        var agentsPath = Path.Combine(workspace, "AGENTS.md");

        Assert.True(File.Exists(skillPath));
        Assert.True(File.Exists(metadataPath));
        Assert.True(File.Exists(mcpPath));
        Assert.True(File.Exists(agentsPath));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mcpPath))!.AsObject();
        var args = root["mcpServers"]!["ai-code-graph"]!["args"]!.AsArray();
        Assert.Equal("mcp", args[0]!.GetValue<string>());
        Assert.Equal("--db", args[1]!.GetValue<string>());
        Assert.Equal("./codex/graph.db", args[2]!.GetValue<string>());

        var agentsContent = await File.ReadAllTextAsync(agentsPath);
        Assert.Contains("Auto-Context: Code Graph Integration", agentsContent);
        Assert.Contains("./codex/graph.db", agentsContent);
    }

    [Fact]
    public async Task SetupCodexCommand_WithExistingServer_UpdatesDbPath()
    {
        var workspace = Path.Combine(TempDir, "workspace-codex-update");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents"));

        var mcpPath = Path.Combine(workspace, ".mcp.json");
        await File.WriteAllTextAsync(
            mcpPath,
            """
            {
              "mcpServers": {
                "ai-code-graph": {
                  "type": "stdio",
                  "command": "ai-code-graph",
                  "args": ["mcp", "--db", "./old/graph.db"]
                }
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "SKILL.md"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents", "openai.yaml"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), "# Agent Instructions\n");

        var (exitCode, output, error) = await RunCliAsync("setup-codex --db ./new/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains(".mcp.json (updated)", output);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mcpPath))!.AsObject();
        var args = root["mcpServers"]!["ai-code-graph"]!["args"]!.AsArray();
        Assert.Equal("mcp", args[0]!.GetValue<string>());
        Assert.Equal("--db", args[1]!.GetValue<string>());
        Assert.Equal("./new/graph.db", args[2]!.GetValue<string>());
    }

    [Fact]
    public async Task SetupCodexCommand_WithMatchingInputs_NoChanges()
    {
        var workspace = Path.Combine(TempDir, "workspace-codex-noop");
        Directory.CreateDirectory(workspace);
        var firstRun = await RunCliAsync("setup-codex --db ./same/graph.db", workingDirectory: workspace);
        Assert.Equal(0, firstRun.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(firstRun.Error));

        var mcpPath = Path.Combine(workspace, ".mcp.json");
        var mcpBefore = await File.ReadAllTextAsync(mcpPath);
        var agentsBefore = await File.ReadAllTextAsync(Path.Combine(workspace, "AGENTS.md"));

        var (exitCode, output, error) = await RunCliAsync("setup-codex --db ./same/graph.db", workingDirectory: workspace);

        var mcpAfter = await File.ReadAllTextAsync(mcpPath);
        var agentsAfter = await File.ReadAllTextAsync(Path.Combine(workspace, "AGENTS.md"));

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains("Nothing to do.", output);
        Assert.Equal(mcpBefore, mcpAfter);
        Assert.Equal(agentsBefore, agentsAfter);
    }

    [Fact]
    public async Task SetupCodexCommand_WithCustomServerFields_PreservesFieldsWhenUpdatingDbPath()
    {
        var workspace = Path.Combine(TempDir, "workspace-codex-custom-fields");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents"));

        var mcpPath = Path.Combine(workspace, ".mcp.json");
        await File.WriteAllTextAsync(
            mcpPath,
            """
            {
              "mcpServers": {
                "ai-code-graph": {
                  "type": "stdio",
                  "command": "ai-code-graph",
                  "args": ["mcp", "--db", "./old/graph.db", "--trace"],
                  "env": { "ASPNETCORE_ENVIRONMENT": "Development" },
                  "cwd": "./tools",
                  "wrapperOptions": { "retry": 3 }
                }
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "SKILL.md"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents", "openai.yaml"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), "# Agent Instructions\n");

        var (exitCode, output, error) = await RunCliAsync("setup-codex --db ./new/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains(".mcp.json (updated)", output);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mcpPath))!.AsObject();
        var server = root["mcpServers"]!["ai-code-graph"]!.AsObject();
        var args = server["args"]!.AsArray();

        Assert.Equal("./new/graph.db", args[2]!.GetValue<string>());
        Assert.Equal("--trace", args[3]!.GetValue<string>());
        Assert.Equal("Development", server["env"]!["ASPNETCORE_ENVIRONMENT"]!.GetValue<string>());
        Assert.Equal("./tools", server["cwd"]!.GetValue<string>());
        Assert.Equal(3, server["wrapperOptions"]!["retry"]!.GetValue<int>());
    }

    [Fact]
    public async Task SetupCodexCommand_WithExistingAutoContextSection_UpdatesDbPath()
    {
        var workspace = Path.Combine(TempDir, "workspace-codex-agents-update");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents"));

        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "SKILL.md"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents", "openai.yaml"), "existing");
        await File.WriteAllTextAsync(
            Path.Combine(workspace, ".mcp.json"),
            """
            {
              "mcpServers": {
                "ai-code-graph": {
                  "type": "stdio",
                  "command": "ai-code-graph",
                  "args": ["mcp", "--db", "./new/graph.db"]
                }
              }
            }
            """);

        var agentsPath = Path.Combine(workspace, "AGENTS.md");
        await File.WriteAllTextAsync(
            agentsPath,
            """
            # Agent Instructions

            ## Auto-Context: Code Graph Integration

            When modifying methods in this codebase, run the context command first if `./old/graph.db` exists:

            ```bash
            ai-code-graph context "MethodName" --db ./old/graph.db
            ```

            ## OtherSection
            keep this
            """);

        var (exitCode, output, error) = await RunCliAsync("setup-codex --db ./new/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains("AGENTS.md (updated)", output);

        var updated = await File.ReadAllTextAsync(agentsPath);
        Assert.DoesNotContain("./old/graph.db", updated);
        Assert.Contains("./new/graph.db", updated);
        Assert.Contains("## OtherSection", updated);
        var sectionCount = updated.Split("## Auto-Context: Code Graph Integration").Length - 1;
        Assert.Equal(1, sectionCount);
    }

    [Fact]
    public async Task SetupCodexCommand_WithOtherServers_PreservesAndAddsAiCodeGraph()
    {
        var workspace = Path.Combine(TempDir, "workspace-codex-merge");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents"));

        var mcpPath = Path.Combine(workspace, ".mcp.json");
        await File.WriteAllTextAsync(
            mcpPath,
            """
            {
              "mcpServers": {
                "other-server": {
                  "type": "stdio",
                  "command": "other-tool",
                  "args": ["run"]
                }
              }
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "SKILL.md"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, ".codex", "skills", "ai-code-graph", "agents", "openai.yaml"), "existing");
        await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), "# Agent Instructions\n");

        var (exitCode, output, error) = await RunCliAsync("setup-codex --db ./merged/graph.db", workingDirectory: workspace);

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Contains(".mcp.json (updated)", output);
        Assert.Contains("AGENTS.md (appended)", output);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mcpPath))!.AsObject();
        var servers = root["mcpServers"]!.AsObject();
        Assert.NotNull(servers["other-server"]);
        Assert.NotNull(servers["ai-code-graph"]);
        Assert.Equal("./merged/graph.db", servers["ai-code-graph"]!["args"]!.AsArray()[2]!.GetValue<string>());
    }
}
