using System.Diagnostics;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class CliCommandTests : IDisposable
{
    private static readonly string CliDll = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AiCodeGraph.Cli", "bin", "Debug", "net8.0", "AiCodeGraph.Cli.dll"));

    private readonly string _tempDir;

    public CliCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCliAsync(string args, int timeoutMs = 10000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{CliDll} {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
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
        var dbPath = Path.Combine(_tempDir, "graph.db");
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
        var fakePath = Path.Combine(_tempDir, "nonexistent.db");
        var (_, _, error) = await RunCliAsync($"{commandName} --db {fakePath}");
        Assert.Contains("Error", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallgraphCommand_MissingDb_WritesErrorToStderr()
    {
        var fakePath = Path.Combine(_tempDir, "nonexistent.db");
        var (_, _, error) = await RunCliAsync($"callgraph TestMethod --db {fakePath}");
        Assert.Contains("Error", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImpactCommand_MissingDb_WritesErrorToStderr()
    {
        var fakePath = Path.Combine(_tempDir, "nonexistent.db");
        var (_, _, error) = await RunCliAsync($"impact TestMethod --db {fakePath}");
        Assert.Contains("Error", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DriftCommand_MissingBaseline_WritesErrorToStderr()
    {
        var dbPath = await CreateTestDbAsync();
        var fakeBaseline = Path.Combine(_tempDir, "baseline.db");
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
        Assert.Contains("\"hotspots\"", output);
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
        // JSON format with top 1 should only have 1 entry
        var occurrences = output.Split("\"complexity\"").Length - 1;
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
}
