using AiCodeGraph.Core.Drift;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Embeddings;
using AiCodeGraph.Core.Normalization;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class ErrorPathTests
{
    // --- StorageService error paths ---

    [Fact]
    public void StorageService_NullDbPath_UsesDefault()
    {
        var svc = new StorageService(null);
        Assert.NotNull(svc);
    }

    [Fact]
    public async Task StorageService_InvalidPath_ThrowsOnOpen()
    {
        var svc = new StorageService("/nonexistent/path/to/nowhere/db.sqlite");
        await Assert.ThrowsAsync<SqliteException>(() => svc.OpenAsync());
    }

    [Fact]
    public async Task StorageService_QueryBeforeInit_ThrowsInvalidOperation()
    {
        var svc = new StorageService(":memory:");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GetCallGraphForMethodsAsync(new HashSet<string> { "test" }));
    }

    [Fact]
    public async Task StorageService_EmptyMethodIds_ReturnsEmpty()
    {
        var svc = new StorageService(":memory:");
        await svc.InitializeAsync();
        var result = await svc.GetCallGraphForMethodsAsync(new HashSet<string>());
        Assert.Empty(result);
        svc.Dispose();
    }

    // --- VectorIndex error paths ---

    [Fact]
    public void VectorIndex_SearchEmptyIndex_ReturnsEmpty()
    {
        var index = new VectorIndex();
        var result = index.Search(new float[] { 1f, 0f, 0f });
        Assert.Empty(result);
    }

    [Fact]
    public void VectorIndex_AddNullVector_ThrowsArgumentNull()
    {
        var index = new VectorIndex();
        Assert.Throws<ArgumentNullException>(() => index.AddItem("test", null!));
    }

    [Fact]
    public void VectorIndex_AddNaNVector_ThrowsArgument()
    {
        var index = new VectorIndex();
        Assert.Throws<ArgumentException>(() =>
            index.AddItem("test", new float[] { 1f, float.NaN, 0f }));
    }

    [Fact]
    public void VectorIndex_AddInfinityVector_ThrowsArgument()
    {
        var index = new VectorIndex();
        Assert.Throws<ArgumentException>(() =>
            index.AddItem("test", new float[] { 1f, float.PositiveInfinity, 0f }));
    }

    [Fact]
    public void VectorIndex_MismatchedDimensions_ThrowsArgument()
    {
        var index = new VectorIndex();
        index.AddItem("a", new float[] { 1f, 0f, 0f });
        Assert.Throws<ArgumentException>(() =>
            index.AddItem("b", new float[] { 1f, 0f }));
    }

    [Fact]
    public void VectorIndex_BuildIndexNullVector_ThrowsArgumentNull()
    {
        var index = new VectorIndex();
        var items = new List<(string Id, float[] Vector)>
        {
            ("a", null!)
        };
        Assert.Throws<ArgumentNullException>(() => index.BuildIndex(items));
    }

    [Fact]
    public void VectorIndex_BuildIndexMismatchedDimensions_ThrowsArgument()
    {
        var index = new VectorIndex();
        var items = new List<(string Id, float[] Vector)>
        {
            ("a", new float[] { 1f, 0f, 0f }),
            ("b", new float[] { 1f, 0f })
        };
        Assert.Throws<ArgumentException>(() => index.BuildIndex(items));
    }

    [Fact]
    public void VectorIndex_BuildIndexEmpty_CountIsZero()
    {
        var index = new VectorIndex();
        index.BuildIndex(new List<(string Id, float[] Vector)>());
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void VectorIndex_SearchNullQuery_ThrowsArgumentNull()
    {
        var index = new VectorIndex();
        index.AddItem("a", new float[] { 1f, 0f, 0f });
        Assert.Throws<ArgumentNullException>(() => index.Search(null!));
    }

    [Fact]
    public void VectorIndex_SearchNaNQuery_ThrowsArgument()
    {
        var index = new VectorIndex();
        index.AddItem("a", new float[] { 1f, 0f, 0f });
        Assert.Throws<ArgumentException>(() =>
            index.Search(new float[] { float.NaN, 0f, 0f }));
    }

    // --- DriftDetector error paths ---

    [Fact]
    public async Task DriftDetector_MissingBaselineFile_ThrowsFileNotFound()
    {
        var detector = new DriftDetector();
        var tempCurrent = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                detector.CompareAsync(tempCurrent, "/nonexistent/baseline.db"));
        }
        finally
        {
            File.Delete(tempCurrent);
        }
    }

    [Fact]
    public async Task DriftDetector_MissingCurrentFile_ThrowsFileNotFound()
    {
        var detector = new DriftDetector();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            detector.CompareAsync("/nonexistent/current.db", "/nonexistent/baseline.db"));
    }

    [Fact]
    public async Task DriftDetector_EmptyDatabases_ReturnsEmptyReport()
    {
        var detector = new DriftDetector();
        var currentPath = Path.GetTempFileName();
        var baselinePath = Path.GetTempFileName();
        try
        {
            // Create empty SQLite databases
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={currentPath}"))
            {
                await conn.OpenAsync();
            }
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={baselinePath}"))
            {
                await conn.OpenAsync();
            }

            var report = await detector.CompareAsync(currentPath, baselinePath);
            Assert.Empty(report.NewMethods);
            Assert.Empty(report.RemovedMethods);
            Assert.Empty(report.Regressions);
            Assert.Empty(report.NewDuplicates);
            Assert.Empty(report.IntentScattering);
        }
        finally
        {
            File.Delete(currentPath);
            File.Delete(baselinePath);
        }
    }

    // --- IntentClusterer error paths ---

    [Fact]
    public void IntentClusterer_EmptyMethodList_ReturnsEmptyClusters()
    {
        var clusterer = new IntentClusterer();
        var result = clusterer.ClusterMethods(
            new List<NormalizedMethod>(),
            new List<(string MethodId, float[] Vector)>());
        Assert.Empty(result);
    }

    [Fact]
    public void IntentClusterer_FewerThanMinPoints_ReturnsEmptyClusters()
    {
        var clusterer = new IntentClusterer(minPoints: 5);
        var methods = new List<NormalizedMethod>
        {
            new("m1", "sig1", "payload1"),
            new("m2", "sig2", "payload2")
        };
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("m1", new float[] { 1f, 0f, 0f }),
            ("m2", new float[] { 0f, 1f, 0f })
        };
        var result = clusterer.ClusterMethods(methods, embeddings);
        Assert.Empty(result);
    }

    [Fact]
    public void IntentClusterer_SingleMethod_ReturnsEmptyClusters()
    {
        var clusterer = new IntentClusterer();
        var methods = new List<NormalizedMethod>
        {
            new("m1", "sig1", "payload1")
        };
        var embeddings = new List<(string MethodId, float[] Vector)>
        {
            ("m1", new float[] { 1f, 0f, 0f })
        };
        var result = clusterer.ClusterMethods(methods, embeddings);
        Assert.Empty(result);
    }
}
