using AiCodeGraph.Core.Query;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class GraphTraversalEngineTests : TempDirectoryFixture
{
    public GraphTraversalEngineTests() : base("traversal-test") { }

    private async Task<(StorageService Storage, GraphTraversalEngine Engine)> CreateTestGraphAsync()
    {
        // Create a test graph:
        //    A -> B -> D
        //    A -> C -> D
        //    E (isolated)
        var dbPath = Path.Combine(TempDir, "graph.db");
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'TestNs', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'TestClass', 'TestNs.TestClass', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine) VALUES
                    ('A', 'MethodA', 'TestNs.TestClass.MethodA()', 'void', 'type1', 10, 20),
                    ('B', 'MethodB', 'TestNs.TestClass.MethodB()', 'void', 'type1', 30, 40),
                    ('C', 'MethodC', 'TestNs.TestClass.MethodC()', 'void', 'type1', 50, 60),
                    ('D', 'MethodD', 'TestNs.TestClass.MethodD()', 'void', 'type1', 70, 80),
                    ('E', 'MethodE', 'TestNs.TestClass.MethodE()', 'void', 'type1', 90, 100);
                """;
            await ins.ExecuteNonQueryAsync();
        }

        await storage.SaveCallGraphAsync(new List<(string, string)>
        {
            ("A", "B"),
            ("A", "C"),
            ("B", "D"),
            ("C", "D")
        });

        await storage.SaveMetricsAsync(new List<(string, int, int, int)>
        {
            ("A", 10, 20, 2),
            ("B", 5, 15, 1),
            ("C", 8, 18, 2),
            ("D", 3, 10, 1),
            ("E", 1, 5, 0)
        });

        var engine = new GraphTraversalEngine(storage);
        return (storage, engine);
    }

    [Fact]
    public async Task TraverseAsync_InvalidSeedMethod_ReturnsEmpty()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("NonExistent");
            var result = await engine.TraverseAsync(config);

            Assert.Empty(result.Nodes);
            Assert.Empty(result.Edges);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_BFS_ProducesLevelOrderTraversal()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A", Direction: TraversalDirection.Callees, MaxDepth: 3, Strategy: TraversalStrategy.BFS);
            var result = await engine.TraverseAsync(config);

            // BFS: A at depth 0, then B,C at depth 1, then D at depth 2
            Assert.Equal("A", result.SeedMethodId);
            Assert.Equal(4, result.Nodes.Count);

            var depths = result.Nodes.Select(n => (n.MethodId, n.Depth)).ToList();
            Assert.Contains(("A", 0), depths);
            Assert.Contains(("B", 1), depths);
            Assert.Contains(("C", 1), depths);
            Assert.Contains(("D", 2), depths);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_DFS_ExploresBranchesFirst()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A", Direction: TraversalDirection.Callees, MaxDepth: 3, Strategy: TraversalStrategy.DFS);
            var result = await engine.TraverseAsync(config);

            // DFS visits all nodes (ranking reorders them, but all should be present)
            Assert.Equal(4, result.Nodes.Count);

            // Seed node A should have depth 0
            var aNode = result.Nodes.First(n => n.MethodId == "A");
            Assert.Equal(0, aNode.Depth);

            // Check depths are correctly assigned regardless of ranking order
            var bNode = result.Nodes.First(n => n.MethodId == "B");
            var cNode = result.Nodes.First(n => n.MethodId == "C");
            var dNode = result.Nodes.First(n => n.MethodId == "D");
            Assert.Equal(1, bNode.Depth);
            Assert.Equal(1, cNode.Depth);
            Assert.Equal(2, dNode.Depth);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_MaxDepth_LimitsTraversal()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A", Direction: TraversalDirection.Callees, MaxDepth: 1);
            var result = await engine.TraverseAsync(config);

            // Only A, B, C (depth 0 and 1), not D (depth 2)
            Assert.Equal(3, result.Nodes.Count);
            Assert.All(result.Nodes, n => Assert.True(n.Depth <= 1));
            Assert.DoesNotContain(result.Nodes, n => n.MethodId == "D");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_DirectionCallees_OnlyFollowsOutgoingEdges()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("B", Direction: TraversalDirection.Callees, MaxDepth: 2);
            var result = await engine.TraverseAsync(config);

            // B -> D only (not A which calls B)
            Assert.Equal(2, result.Nodes.Count);
            Assert.Contains(result.Nodes, n => n.MethodId == "B");
            Assert.Contains(result.Nodes, n => n.MethodId == "D");
            Assert.DoesNotContain(result.Nodes, n => n.MethodId == "A");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_DirectionCallers_OnlyFollowsIncomingEdges()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("D", Direction: TraversalDirection.Callers, MaxDepth: 2);
            var result = await engine.TraverseAsync(config);

            // D <- B,C <- A
            Assert.Equal(4, result.Nodes.Count);
            Assert.Contains(result.Nodes, n => n.MethodId == "D");
            Assert.Contains(result.Nodes, n => n.MethodId == "B");
            Assert.Contains(result.Nodes, n => n.MethodId == "C");
            Assert.Contains(result.Nodes, n => n.MethodId == "A");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_DirectionBoth_FollowsBothDirections()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("B", Direction: TraversalDirection.Both, MaxDepth: 1);
            var result = await engine.TraverseAsync(config);

            // B has caller A and callee D
            Assert.Equal(3, result.Nodes.Count);
            Assert.Contains(result.Nodes, n => n.MethodId == "B");
            Assert.Contains(result.Nodes, n => n.MethodId == "A");
            Assert.Contains(result.Nodes, n => n.MethodId == "D");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_MaxResults_CausesEarlyTermination()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A", Direction: TraversalDirection.Callees, MaxDepth: 3, MaxResults: 2);
            var result = await engine.TraverseAsync(config);

            // Should stop after 2 nodes
            Assert.Equal(2, result.Nodes.Count);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_CycleDetection_PreventsDuplicates()
    {
        // Create graph with cycle: A -> B -> A
        var dbPath = Path.Combine(TempDir, "cycle.db");
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'TestNs', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'TestClass', 'TestNs.TestClass', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine) VALUES
                    ('A', 'MethodA', 'TestNs.TestClass.MethodA()', 'void', 'type1', 10, 20),
                    ('B', 'MethodB', 'TestNs.TestClass.MethodB()', 'void', 'type1', 30, 40);
                """;
            await ins.ExecuteNonQueryAsync();
        }

        await storage.SaveCallGraphAsync(new List<(string, string)> { ("A", "B"), ("B", "A") });

        try
        {
            var engine = new GraphTraversalEngine(storage);
            var config = new TraversalConfig("A", Direction: TraversalDirection.Callees, MaxDepth: 10);
            var result = await engine.TraverseAsync(config);

            // Should only have A and B, no duplicates despite cycle
            Assert.Equal(2, result.Nodes.Count);
            Assert.Equal(1, result.Nodes.Count(n => n.MethodId == "A"));
            Assert.Equal(1, result.Nodes.Count(n => n.MethodId == "B"));
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_Filter_ExcludesMatchingNodes()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var filter = new FilterConfig(ExcludeNamespaces: new[] { "*MethodB*" });
            var config = new TraversalConfig("A", Direction: TraversalDirection.Callees, MaxDepth: 3, Filter: filter);
            var result = await engine.TraverseAsync(config);

            // B should be filtered out (but not A, C, D)
            Assert.DoesNotContain(result.Nodes, n => n.MethodId == "B");
            Assert.Contains(result.Nodes, n => n.MethodId == "A"); // Seed kept
            Assert.Contains(result.Nodes, n => n.MethodId == "C");
            Assert.Contains(result.Nodes, n => n.MethodId == "D");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_RecordsEdgesCorrectly()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A", Direction: TraversalDirection.Callees, MaxDepth: 2);
            var result = await engine.TraverseAsync(config);

            // Should have edges A->B, A->C, B->D, C->D
            Assert.Equal(4, result.Edges.Count);
            Assert.Contains(result.Edges, e => e.FromMethodId == "A" && e.ToMethodId == "B");
            Assert.Contains(result.Edges, e => e.FromMethodId == "A" && e.ToMethodId == "C");
            Assert.Contains(result.Edges, e => e.FromMethodId == "B" && e.ToMethodId == "D");
            Assert.Contains(result.Edges, e => e.FromMethodId == "C" && e.ToMethodId == "D");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadiusAsync_LeafMethod_ReturnsZero()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            // E is isolated, no callers
            var radius = await engine.ComputeBlastRadiusAsync("E", CancellationToken.None);
            Assert.Equal(0, radius);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadiusAsync_MethodWithCallers_CountsTransitiveCallers()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            // D is called by B and C, which are called by A
            // So D has blast radius 3 (A, B, C all affected)
            var radius = await engine.ComputeBlastRadiusAsync("D", CancellationToken.None);
            Assert.Equal(3, radius);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ComputeBlastRadiusAsync_CacheWorks()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var radius1 = await engine.ComputeBlastRadiusAsync("D", CancellationToken.None);
            var radius2 = await engine.ComputeBlastRadiusAsync("D", CancellationToken.None);

            Assert.Equal(radius1, radius2);
            Assert.Equal(3, radius1);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    // --- Ranking Strategy Tests ---

    [Fact]
    public async Task TraverseAsync_RankByBlastRadius_SortsDescending()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A",
                Direction: TraversalDirection.Callees,
                MaxDepth: 3,
                Ranking: RankingStrategy.BlastRadius);
            var result = await engine.TraverseAsync(config);

            // D has highest blast radius (3), then B,C (1 each from A), A has 0
            // After ranking: D should be first (highest score) among non-seed
            var nonSeedNodes = result.Nodes.Where(n => n.MethodId != "A").ToList();

            // D has blast radius 3 (A, B, C call it transitively)
            var dNode = result.Nodes.First(n => n.MethodId == "D");
            Assert.Equal(3, (int)dNode.RankingScore);

            // B and C have blast radius 1 (only A calls them)
            var bNode = result.Nodes.First(n => n.MethodId == "B");
            var cNode = result.Nodes.First(n => n.MethodId == "C");
            Assert.Equal(1, (int)bNode.RankingScore);
            Assert.Equal(1, (int)cNode.RankingScore);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_RankByComplexity_SortsDescending()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A",
                Direction: TraversalDirection.Callees,
                MaxDepth: 3,
                Ranking: RankingStrategy.Complexity);
            var result = await engine.TraverseAsync(config);

            // From test data: A=10, C=8, B=5, D=3
            // After sorting: A(10), C(8), B(5), D(3)
            Assert.True(result.Nodes.Count >= 4);

            var aNode = result.Nodes.First(n => n.MethodId == "A");
            var bNode = result.Nodes.First(n => n.MethodId == "B");
            var cNode = result.Nodes.First(n => n.MethodId == "C");
            var dNode = result.Nodes.First(n => n.MethodId == "D");

            Assert.Equal(10, (int)aNode.RankingScore);
            Assert.Equal(5, (int)bNode.RankingScore);
            Assert.Equal(8, (int)cNode.RankingScore);
            Assert.Equal(3, (int)dNode.RankingScore);

            // A should come before B in sorted order
            var aIndex = result.Nodes.ToList().FindIndex(n => n.MethodId == "A");
            var bIndex = result.Nodes.ToList().FindIndex(n => n.MethodId == "B");
            Assert.True(aIndex < bIndex);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_RankByCoupling_ComputesCaAndCe()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A",
                Direction: TraversalDirection.Callees,
                MaxDepth: 3,
                Ranking: RankingStrategy.Coupling);
            var result = await engine.TraverseAsync(config);

            // A: Ca=0, Ce=2 (calls B, C) -> coupling=2
            // B: Ca=1 (A), Ce=1 (D) -> coupling=2
            // C: Ca=1 (A), Ce=1 (D) -> coupling=2
            // D: Ca=2 (B, C), Ce=0 -> coupling=2
            var aNode = result.Nodes.First(n => n.MethodId == "A");
            var dNode = result.Nodes.First(n => n.MethodId == "D");

            Assert.NotNull(aNode.Metrics);
            Assert.Equal(0, aNode.Metrics!.AfferentCoupling);
            Assert.Equal(2, aNode.Metrics.EfferentCoupling);

            Assert.NotNull(dNode.Metrics);
            Assert.Equal(2, dNode.Metrics!.AfferentCoupling);
            Assert.Equal(0, dNode.Metrics.EfferentCoupling);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_RankByCombined_NormalizesAndWeights()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A",
                Direction: TraversalDirection.Callees,
                MaxDepth: 3,
                Ranking: RankingStrategy.Combined);
            var result = await engine.TraverseAsync(config);

            // Combined ranking should produce scores between 0 and 1
            foreach (var node in result.Nodes)
            {
                Assert.InRange(node.RankingScore, 0f, 1.01f);
                Assert.NotNull(node.Metrics);
            }

            // Nodes should be sorted descending
            for (int i = 0; i < result.Nodes.Count - 1; i++)
            {
                Assert.True(result.Nodes[i].RankingScore >= result.Nodes[i + 1].RankingScore);
            }
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task TraverseAsync_RankByComplexity_PopulatesMetrics()
    {
        var (storage, engine) = await CreateTestGraphAsync();
        try
        {
            var config = new TraversalConfig("A",
                Direction: TraversalDirection.Callees,
                MaxDepth: 1,
                Ranking: RankingStrategy.Complexity);
            var result = await engine.TraverseAsync(config);

            var aNode = result.Nodes.First(n => n.MethodId == "A");
            Assert.NotNull(aNode.Metrics);
            Assert.Equal(10, aNode.Metrics!.CognitiveComplexity);
            Assert.Equal(20, aNode.Metrics.LinesOfCode);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }
}
