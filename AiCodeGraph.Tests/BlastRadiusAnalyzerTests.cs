using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class BlastRadiusAnalyzerTests : TempDirectoryFixture
{
    public BlastRadiusAnalyzerTests() : base("blast-radius-test") { }

    [Fact]
    public async Task ComputeBlastRadius_LinearChain_ComputesCorrectDepthAndCallers()
    {
        // A → B → C (linear chain)
        var dbPath = Path.Combine(TempDir, "linear.db");
        var storage = new StorageService(dbPath);
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'Test', 'MyApp.Test', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('A', 'A', 'void MyApp.Test.A()', 'void', 'type1', 10, 20, 'Public'),
                    ('B', 'B', 'void MyApp.Test.B()', 'void', 'type1', 21, 30, 'Public'),
                    ('C', 'C', 'void MyApp.Test.C()', 'void', 'type1', 31, 40, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('A', 1, 10, 1), ('B', 1, 10, 1), ('C', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES
                    ('A', 'B'), ('B', 'C');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var analyzer = new BlastRadiusAnalyzer();
            var results = await analyzer.ComputeBlastRadiusAsync(storage);

            Assert.Equal(3, results.Count);

            // A is entry point, no callers
            Assert.Equal(0, results["A"].DirectCallers);
            Assert.Equal(0, results["A"].TransitiveCallers);
            Assert.Equal(0, results["A"].Depth);

            // B is called by A
            Assert.Equal(1, results["B"].DirectCallers);
            Assert.Equal(1, results["B"].TransitiveCallers);
            Assert.Equal(1, results["B"].Depth);

            // C is called by B, transitively by A
            Assert.Equal(1, results["C"].DirectCallers);
            Assert.Equal(2, results["C"].TransitiveCallers); // B and A
            Assert.Equal(2, results["C"].Depth);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadius_DiamondPattern_CountsTransitiveCallers()
    {
        // A → B → D
        // A → C → D
        var dbPath = Path.Combine(TempDir, "diamond.db");
        var storage = new StorageService(dbPath);
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'Test', 'MyApp.Test', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('A', 'A', 'void MyApp.Test.A()', 'void', 'type1', 10, 20, 'Public'),
                    ('B', 'B', 'void MyApp.Test.B()', 'void', 'type1', 21, 30, 'Public'),
                    ('C', 'C', 'void MyApp.Test.C()', 'void', 'type1', 31, 40, 'Public'),
                    ('D', 'D', 'void MyApp.Test.D()', 'void', 'type1', 41, 50, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('A', 1, 10, 1), ('B', 1, 10, 1), ('C', 1, 10, 1), ('D', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES
                    ('A', 'B'), ('A', 'C'), ('B', 'D'), ('C', 'D');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var analyzer = new BlastRadiusAnalyzer();
            var results = await analyzer.ComputeBlastRadiusAsync(storage);

            Assert.Equal(4, results.Count);

            // D has 2 direct callers (B, C) and 3 transitive (B, C, A)
            Assert.Equal(2, results["D"].DirectCallers);
            Assert.Equal(3, results["D"].TransitiveCallers);
            Assert.Equal(2, results["D"].Depth);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadius_IsolatedMethod_HasZeroBlastRadius()
    {
        var dbPath = Path.Combine(TempDir, "isolated.db");
        var storage = new StorageService(dbPath);
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'Test', 'MyApp.Test', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('isolated', 'Isolated', 'void MyApp.Test.Isolated()', 'void', 'type1', 10, 20, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('isolated', 1, 10, 1);
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var analyzer = new BlastRadiusAnalyzer();
            var results = await analyzer.ComputeBlastRadiusAsync(storage);

            Assert.Single(results);
            Assert.Equal(0, results["isolated"].DirectCallers);
            Assert.Equal(0, results["isolated"].TransitiveCallers);
            Assert.Equal(0, results["isolated"].Depth);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadius_EntryPointDetection_IdentifiesRoots()
    {
        // A → B, C → B (A and C are entry points)
        var dbPath = Path.Combine(TempDir, "entrypoints.db");
        var storage = new StorageService(dbPath);
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'Test', 'MyApp.Test', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('A', 'A', 'void MyApp.Test.A()', 'void', 'type1', 10, 20, 'Public'),
                    ('B', 'B', 'void MyApp.Test.B()', 'void', 'type1', 21, 30, 'Public'),
                    ('C', 'C', 'void MyApp.Test.C()', 'void', 'type1', 31, 40, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('A', 1, 10, 1), ('B', 1, 10, 1), ('C', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES
                    ('A', 'B'), ('C', 'B');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var analyzer = new BlastRadiusAnalyzer();
            var results = await analyzer.ComputeBlastRadiusAsync(storage);

            // B should have 2 entry points (A and C)
            Assert.Equal(2, results["B"].EntryPoints.Count);
            Assert.Contains("void MyApp.Test.A()", results["B"].EntryPoints);
            Assert.Contains("void MyApp.Test.C()", results["B"].EntryPoints);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadius_EmptyDatabase_ReturnsEmptyResults()
    {
        var dbPath = Path.Combine(TempDir, "empty.db");
        var storage = new StorageService(dbPath);
        await storage.InitializeAsync();

        try
        {
            var analyzer = new BlastRadiusAnalyzer();
            var results = await analyzer.ComputeBlastRadiusAsync(storage);

            Assert.Empty(results);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadius_LargeGraph_CompletesInReasonableTime()
    {
        var dbPath = Path.Combine(TempDir, "large.db");
        var storage = new StorageService(dbPath);
        await storage.InitializeAsync();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using (var fk = conn.CreateCommand())
        {
            fk.CommandText = "PRAGMA foreign_keys=OFF;";
            await fk.ExecuteNonQueryAsync();
        }

        // Create 1000 methods with a connected graph
        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'Test', 'MyApp.Test', 'ns1', 'Class');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        // Insert 1000 methods
        for (int i = 0; i < 1000; i++)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = $"""
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m{i}', 'M{i}', 'void MyApp.Test.M{i}()', 'void', 'type1', {i * 10}, {i * 10 + 9}, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('m{i}', 1, 10, 1);
                """;
            await ins.ExecuteNonQueryAsync();
        }

        // Create call graph: each method calls the next (chain)
        for (int i = 0; i < 999; i++)
        {
            using var ins = conn.CreateCommand();
            ins.CommandText = $"INSERT INTO MethodCalls (CallerId, CalleeId) VALUES ('m{i}', 'm{i + 1}');";
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var analyzer = new BlastRadiusAnalyzer();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await analyzer.ComputeBlastRadiusAsync(storage);
            sw.Stop();

            Assert.Equal(1000, results.Count);
            Assert.True(sw.ElapsedMilliseconds < 2000, $"Expected <2s but took {sw.ElapsedMilliseconds}ms");

            // Verify last method has max depth and callers
            Assert.Equal(999, results["m999"].TransitiveCallers);
            Assert.Equal(999, results["m999"].Depth);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }
}
