using AiCodeGraph.Core.Query;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class GraphQueryExecutorTests : TempDirectoryFixture
{
    public GraphQueryExecutorTests() : base("executor-test") { }

    private async Task<(StorageService Storage, GraphQueryExecutor Executor)> CreateTestExecutorAsync()
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
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, FilePath) VALUES
                    ('A', 'MethodA', 'TestNs.TestClass.MethodA()', 'void', 'type1', 10, 20, '/src/TestClass.cs'),
                    ('B', 'MethodB', 'TestNs.TestClass.MethodB()', 'void', 'type1', 30, 40, '/src/TestClass.cs'),
                    ('C', 'MethodC', 'TestNs.TestClass.MethodC()', 'void', 'type1', 50, 60, '/src/TestClass.cs'),
                    ('D', 'MethodD', 'TestNs.TestClass.MethodD()', 'void', 'type1', 70, 80, '/src/TestClass.cs'),
                    ('E', 'MethodE', 'TestNs.TestClass.MethodE()', 'void', 'type1', 90, 100, '/src/TestClass.cs');
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
        var executor = new GraphQueryExecutor(storage, engine);
        return (storage, executor);
    }

    [Fact]
    public async Task ExecuteAsync_SimpleMethodIdSeed_ReturnsResults()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success, $"Query failed: {result.Error}");
            Assert.NotEmpty(result.Nodes);
            Assert.Contains(result.Nodes, n => n.MethodId == "A");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_MethodPatternSeed_ResolvesMultipleMethods()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodPattern = "*Method*" },
                Expand = new QueryExpand { Direction = ExpandDirection.None, IncludeTransitive = false }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success, $"Query failed: {result.Error}");
            Assert.True(result.Nodes.Count >= 2);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_FilterExcludesNamespace_FiltersResults()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 3 },
                Filter = new QueryFilter { ExcludeNamespaces = new List<string> { "*MethodB*" } }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Nodes, n => n.MethodId == "B");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_MaxResultsLimitsOutput()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 3 },
                Output = new QueryOutput { MaxResults = 2 }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.Equal(2, result.Nodes.Count);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_IncludeMetricsFalse_ExcludesComplexityData()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.None, IncludeTransitive = false },
                Output = new QueryOutput { IncludeMetrics = false }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.NotEmpty(result.Nodes);
            Assert.All(result.Nodes, n =>
            {
                Assert.Null(n.Complexity);
                Assert.Null(n.Loc);
                Assert.Null(n.Nesting);
            });
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_IncludeLocationFalse_ExcludesFileInfo()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.None, IncludeTransitive = false },
                Output = new QueryOutput { IncludeLocation = false }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.NotEmpty(result.Nodes);
            Assert.All(result.Nodes, n =>
            {
                Assert.Null(n.FilePath);
                Assert.Null(n.Line);
            });
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidQuery_ReturnsErrorWithoutThrowing()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "   " } // whitespace = invalid
            };

            var result = await executor.ExecuteAsync(query);

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
            Assert.Contains("validation failed", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_CapturesExecutionTime()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.True(result.ExecutionTime > TimeSpan.Zero);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_NonexistentSeed_ReturnsError()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "NonExistent" }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.False(result.Success);
            Assert.Contains("No methods matched", result.Error);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpandDirectionCallers_TraversesUpward()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "D" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callers, MaxDepth = 2 }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.Contains(result.Nodes, n => n.MethodId == "D");
            Assert.Contains(result.Nodes, n => n.MethodId == "B");
            Assert.Contains(result.Nodes, n => n.MethodId == "C");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpandDirectionCallees_TraversesDownward()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 2 }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.Contains(result.Nodes, n => n.MethodId == "A");
            Assert.Contains(result.Nodes, n => n.MethodId == "B");
            Assert.Contains(result.Nodes, n => n.MethodId == "C");
            Assert.Contains(result.Nodes, n => n.MethodId == "D");
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExpandDirectionNone_ReturnsSeedOnly()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.None, IncludeTransitive = false }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.Single(result.Nodes);
            Assert.Equal("A", result.Nodes[0].MethodId);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_RankByComplexity_SortsDescending()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 3 },
                Rank = new QueryRank { Strategy = RankStrategy.Complexity, Descending = true }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.True(result.Nodes.Count >= 4);

            // Verify descending order by rank score
            for (int i = 0; i < result.Nodes.Count - 1; i++)
            {
                Assert.True((result.Nodes[i].RankScore ?? 0) >= (result.Nodes[i + 1].RankScore ?? 0));
            }
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_RankAscending_ReversesOrder()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 3 },
                Rank = new QueryRank { Strategy = RankStrategy.Complexity, Descending = false }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.True(result.Nodes.Count >= 4);

            // Verify ascending order by rank score
            for (int i = 0; i < result.Nodes.Count - 1; i++)
            {
                Assert.True((result.Nodes[i].RankScore ?? 0) <= (result.Nodes[i + 1].RankScore ?? 0));
            }
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeMetrics_PopulatesComplexityData()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.None, IncludeTransitive = false },
                Output = new QueryOutput { IncludeMetrics = true }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.Single(result.Nodes);
            Assert.Equal(10, result.Nodes[0].Complexity);
            Assert.Equal(20, result.Nodes[0].Loc);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeLocation_PopulatesFileInfo()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.None, IncludeTransitive = false },
                Output = new QueryOutput { IncludeLocation = true }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.Single(result.Nodes);
            Assert.Equal("/src/TestClass.cs", result.Nodes[0].FilePath);
            Assert.Equal(10, result.Nodes[0].Line);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_TotalMatchesCountsBeforeLimit()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 3 },
                Output = new QueryOutput { MaxResults = 1 }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);
            Assert.Equal(1, result.Nodes.Count);
            Assert.True(result.TotalMatches > result.Nodes.Count);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_DepthIsCorrect()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed { MethodId = "A" },
                Expand = new QueryExpand { Direction = ExpandDirection.Callees, MaxDepth = 3 },
                Rank = new QueryRank { Strategy = RankStrategy.Complexity }
            };

            var result = await executor.ExecuteAsync(query);

            Assert.True(result.Success);

            var seedNode = result.Nodes.First(n => n.MethodId == "A");
            Assert.Equal(0, seedNode.Depth);

            // B and C should be depth 1
            var bNode = result.Nodes.First(n => n.MethodId == "B");
            var cNode = result.Nodes.First(n => n.MethodId == "C");
            Assert.Equal(1, bNode.Depth);
            Assert.Equal(1, cNode.Depth);

            // D should be depth 2
            var dNode = result.Nodes.First(n => n.MethodId == "D");
            Assert.Equal(2, dNode.Depth);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_EmptySeed_ReturnsValidationError()
    {
        var (storage, executor) = await CreateTestExecutorAsync();
        try
        {
            var query = new GraphQuery
            {
                Seed = new QuerySeed() // Empty seed = invalid
            };

            var result = await executor.ExecuteAsync(query);

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
            Assert.Contains("validation failed", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }
}
