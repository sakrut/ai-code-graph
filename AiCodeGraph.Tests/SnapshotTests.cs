using System.Diagnostics;
using System.Text.RegularExpressions;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

/// <summary>
/// Snapshot (golden file) tests for CLI command outputs.
/// Run with UPDATE_SNAPSHOTS=1 to regenerate golden files.
/// </summary>
public class SnapshotTests : TempDirectoryFixture
{
#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif

    private static readonly string CliDll = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AiCodeGraph.Cli", "bin", BuildConfiguration, "net8.0", "AiCodeGraph.Cli.dll"));

    private static readonly string SnapshotDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots"));

    public SnapshotTests() : base("snapshot-test") { }

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

    private async Task<string> CreateSnapshotDbAsync()
    {
        var dbPath = Path.Combine(TempDir, "graph.db");
        await using var storage = new StorageService(dbPath);
        await storage.InitializeAsync();

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
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'OrderService', 'TestNs.OrderService', 'ns1', 'Class'),
                    ('type2', 'UserService', 'TestNs.UserService', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility, FilePath) VALUES
                    ('TestNs.OrderService.ProcessOrder(String)', 'ProcessOrder', 'TestNs.OrderService.ProcessOrder(String)', 'bool', 'type1', 10, 60, 'Public', '/test/OrderService.cs'),
                    ('TestNs.OrderService.ValidateOrder(Int32)', 'ValidateOrder', 'TestNs.OrderService.ValidateOrder(Int32)', 'bool', 'type1', 70, 90, 'Public', '/test/OrderService.cs'),
                    ('TestNs.OrderService.SaveOrder()', 'SaveOrder', 'TestNs.OrderService.SaveOrder()', 'void', 'type1', 100, 120, 'Private', '/test/OrderService.cs'),
                    ('TestNs.UserService.GetUser(Int32)', 'GetUser', 'TestNs.UserService.GetUser(Int32)', 'User', 'type2', 10, 30, 'Public', '/test/UserService.cs'),
                    ('TestNs.UserService.DeadMethod()', 'DeadMethod', 'TestNs.UserService.DeadMethod()', 'void', 'type2', 40, 50, 'Public', '/test/UserService.cs');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        await storage.SaveMetricsAsync(new List<(string MethodId, int CognitiveComplexity, int LinesOfCode, int NestingDepth)>
        {
            ("TestNs.OrderService.ProcessOrder(String)", 25, 50, 5),
            ("TestNs.OrderService.ValidateOrder(Int32)", 8, 20, 2),
            ("TestNs.OrderService.SaveOrder()", 3, 20, 1),
            ("TestNs.UserService.GetUser(Int32)", 5, 20, 2),
            ("TestNs.UserService.DeadMethod()", 2, 10, 1)
        });

        await storage.SaveCallGraphAsync(new List<(string CallerId, string CalleeId)>
        {
            ("TestNs.OrderService.ProcessOrder(String)", "TestNs.OrderService.ValidateOrder(Int32)"),
            ("TestNs.OrderService.ProcessOrder(String)", "TestNs.OrderService.SaveOrder()"),
            ("TestNs.OrderService.ProcessOrder(String)", "TestNs.UserService.GetUser(Int32)"),
            ("TestNs.OrderService.ValidateOrder(Int32)", "TestNs.UserService.GetUser(Int32)")
        });

        return dbPath;
    }

    private static string NormalizeOutput(string output)
    {
        // Normalize line endings
        output = output.Replace("\r\n", "\n").Trim();
        // Remove trailing whitespace from lines
        output = string.Join("\n", output.Split('\n').Select(l => l.TrimEnd()));
        return output;
    }

    private async Task AssertMatchesSnapshotAsync(string snapshotName, string actual)
    {
        var snapshotPath = Path.Combine(SnapshotDir, $"{snapshotName}.txt");
        actual = NormalizeOutput(actual);

        var updateSnapshots = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

        if (updateSnapshots || !File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(SnapshotDir);
            await File.WriteAllTextAsync(snapshotPath, actual);
            if (!updateSnapshots)
                Assert.Fail($"Snapshot '{snapshotName}' created. Re-run tests to verify.");
            return;
        }

        var expected = NormalizeOutput(await File.ReadAllTextAsync(snapshotPath));

        if (expected != actual)
        {
            var diffPath = Path.Combine(TempDir, $"{snapshotName}.actual.txt");
            await File.WriteAllTextAsync(diffPath, actual);
            Assert.Fail(
                $"Snapshot mismatch for '{snapshotName}'.\n" +
                $"Expected:\n{expected}\n\n" +
                $"Actual:\n{actual}\n\n" +
                $"To update, run: UPDATE_SNAPSHOTS=1 dotnet test --filter {snapshotName}");
        }
    }

    // --- Hotspots ---

    [Fact]
    public async Task Hotspots_Compact_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"hotspots --db {dbPath} --top 5");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("hotspots_compact", output);
    }

    [Fact]
    public async Task Hotspots_Json_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"hotspots --db {dbPath} --top 5 --format json");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("hotspots_json", output);
    }

    // --- Dead Code ---

    [Fact]
    public async Task DeadCode_Compact_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"dead-code --db {dbPath}");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("deadcode_compact", output);
    }

    [Fact]
    public async Task DeadCode_Json_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"dead-code --db {dbPath} --format json");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("deadcode_json", output);
    }

    // --- Callgraph ---

    [Fact]
    public async Task Callgraph_Compact_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"callgraph ProcessOrder --db {dbPath} --depth 2");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("callgraph_compact", output);
    }

    [Fact]
    public async Task Callgraph_Json_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"callgraph ProcessOrder --db {dbPath} --depth 2 --format json");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("callgraph_json", output);
    }

    // --- Impact ---

    [Fact]
    public async Task Impact_Compact_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"impact GetUser --db {dbPath}");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("impact_compact", output);
    }

    [Fact]
    public async Task Impact_Json_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"impact GetUser --db {dbPath} --format json");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("impact_json", output);
    }

    // --- Coupling ---

    [Fact]
    public async Task Coupling_Compact_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"coupling --db {dbPath}");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("coupling_compact", output);
    }

    [Fact]
    public async Task Coupling_Json_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"coupling --db {dbPath} --format json");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("coupling_json", output);
    }

    // --- Context ---

    [Fact]
    public async Task Context_Compact_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"context ProcessOrder --db {dbPath}");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("context_compact", output);
    }

    [Fact]
    public async Task Context_Json_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"context ProcessOrder --db {dbPath} --format json");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("context_json", output);
    }

    // --- Tree ---

    [Fact]
    public async Task Tree_Compact_MatchesSnapshot()
    {
        var dbPath = await CreateSnapshotDbAsync();
        var (exitCode, output, _) = await RunCliAsync($"tree --db {dbPath}");
        Assert.Equal(0, exitCode);
        await AssertMatchesSnapshotAsync("tree_compact", output);
    }
}
