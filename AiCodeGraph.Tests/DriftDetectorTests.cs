using AiCodeGraph.Core.Drift;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Models.CodeGraph;
using AiCodeGraph.Core.Storage;
using Microsoft.CodeAnalysis;
using CodeGraphTypeKind = AiCodeGraph.Core.Models.CodeGraph.TypeKind;

namespace AiCodeGraph.Tests;

public class DriftDetectorTests : TempDirectoryFixture
{
    private readonly string _currentDbPath;
    private readonly string _baselineDbPath;

    public DriftDetectorTests() : base("drift-test")
    {
        _currentDbPath = GetDbPath("current.db");
        _baselineDbPath = GetDbPath("baseline.db");
    }

    private async Task<StorageService> CreateDatabase(string path, List<MethodModel>? methods = null, List<(string, int, int, int)>? metrics = null, List<ClonePair>? clonePairs = null, List<IntentCluster>? clusters = null)
    {
        var storage = new StorageService(path);
        await storage.InitializeAsync();

        methods ??= new List<MethodModel>();
        if (methods.Count > 0)
        {
            var types = new List<TypeModel>
            {
                new("type:Svc", "Svc", "App.Svc",
                    CodeGraphTypeKind.Class, methods, [], Accessibility.Public,
                    false, false, false, false, [], [])
            };
            var ns = new NamespaceModel("ns:App", "App", types, []);
            var project = new ProjectModel("proj:App", "App", "app.csproj", [ns]);
            await storage.SaveCodeModelAsync([new ExtractionResult(project, [])]);
        }

        if (metrics != null && metrics.Count > 0)
            await storage.SaveMetricsAsync(metrics);

        if (clonePairs != null && clonePairs.Count > 0)
            await storage.SaveClonePairsAsync(clonePairs);

        if (clusters != null && clusters.Count > 0)
            await storage.SaveClustersAsync(clusters);

        return storage;
    }

    [Fact]
    public async Task Compare_IdenticalDatabases_NoDrift()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "GetUser", "App.Svc.GetUser()", "void", [], null, 1, 10,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_currentDbPath, methods)) { }
        await using (await CreateDatabase(_baselineDbPath, methods)) { }

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Empty(report.NewMethods);
        Assert.Empty(report.RemovedMethods);
        Assert.Empty(report.Regressions);
        Assert.Empty(report.NewDuplicates);
        Assert.Empty(report.IntentScattering);
    }

    [Fact]
    public async Task Compare_DetectsNewMethods()
    {
        var baseMethods = new List<MethodModel>
        {
            new("m1", "GetUser", "App.Svc.GetUser()", "void", [], null, 1, 10,
                Accessibility.Public, false, false, false, false, false)
        };
        var currentMethods = new List<MethodModel>
        {
            new("m1", "GetUser", "App.Svc.GetUser()", "void", [], null, 1, 10,
                Accessibility.Public, false, false, false, false, false),
            new("m2", "DeleteUser", "App.Svc.DeleteUser()", "void", [], null, 11, 20,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_baselineDbPath, baseMethods)) { }
        await using (await CreateDatabase(_currentDbPath, currentMethods)) { }

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Single(report.NewMethods);
        Assert.Equal("m2", report.NewMethods[0].MethodId);
    }

    [Fact]
    public async Task Compare_DetectsRemovedMethods()
    {
        var baseMethods = new List<MethodModel>
        {
            new("m1", "GetUser", "App.Svc.GetUser()", "void", [], null, 1, 10,
                Accessibility.Public, false, false, false, false, false),
            new("m2", "DeleteUser", "App.Svc.DeleteUser()", "void", [], null, 11, 20,
                Accessibility.Public, false, false, false, false, false)
        };
        var currentMethods = new List<MethodModel>
        {
            new("m1", "GetUser", "App.Svc.GetUser()", "void", [], null, 1, 10,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_baselineDbPath, baseMethods)) { }
        await using (await CreateDatabase(_currentDbPath, currentMethods)) { }

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Single(report.RemovedMethods);
        Assert.Equal("m2", report.RemovedMethods[0].MethodId);
    }

    [Fact]
    public async Task Compare_DetectsComplexityRegression_PercentageThreshold()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "Process", "App.Svc.Process()", "void", [], null, 1, 30,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_baselineDbPath, methods, [("m1", 10, 20, 2)])) { }
        await using (await CreateDatabase(_currentDbPath, methods, [("m1", 13, 20, 2)])) { } // 30% increase

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Single(report.Regressions);
        Assert.Equal(10, report.Regressions[0].BaselineComplexity);
        Assert.Equal(13, report.Regressions[0].CurrentComplexity);
        Assert.True(report.Regressions[0].PercentageIncrease >= 0.25);
    }

    [Fact]
    public async Task Compare_NoRegression_BelowPercentageThreshold()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "Process", "App.Svc.Process()", "void", [], null, 1, 30,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_baselineDbPath, methods, [("m1", 10, 20, 2)])) { }
        await using (await CreateDatabase(_currentDbPath, methods, [("m1", 12, 20, 2)])) { } // 20% increase, below 25%

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Empty(report.Regressions);
    }

    [Fact]
    public async Task Compare_DetectsAbsoluteThresholdCrossing()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "Process", "App.Svc.Process()", "void", [], null, 1, 30,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_baselineDbPath, methods, [("m1", 14, 20, 2)])) { }
        await using (await CreateDatabase(_currentDbPath, methods, [("m1", 16, 20, 2)])) { } // crosses 15

        var options = new DriftDetectorOptions { ComplexityAbsoluteThreshold = 15 };
        var detector = new DriftDetector(options);
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Single(report.Regressions);
        Assert.True(report.Regressions[0].CrossedAbsoluteThreshold);
    }

    [Fact]
    public async Task Compare_NoRegression_ComplexityDecreased()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "Process", "App.Svc.Process()", "void", [], null, 1, 30,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_baselineDbPath, methods, [("m1", 20, 20, 3)])) { }
        await using (await CreateDatabase(_currentDbPath, methods, [("m1", 10, 15, 2)])) { } // improved

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Empty(report.Regressions);
    }

    [Fact]
    public async Task Compare_DetectsNewDuplicates()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "A", "App.Svc.A()", "void", [], null, 1, 5,
                Accessibility.Public, false, false, false, false, false),
            new("m2", "B", "App.Svc.B()", "void", [], null, 6, 10,
                Accessibility.Public, false, false, false, false, false)
        };

        var baseClones = new List<ClonePair>
        {
            new("m1", "m2", 0.8f, 0.7f, 0.74f, CloneType.Type2)
        };
        var currentClones = new List<ClonePair>
        {
            new("m1", "m2", 0.8f, 0.7f, 0.74f, CloneType.Type2),
            new("m1", "m3", 0.9f, 0.85f, 0.87f, CloneType.Type2) // new pair
        };

        await using (await CreateDatabase(_baselineDbPath, methods, clonePairs: baseClones)) { }
        await using (await CreateDatabase(_currentDbPath, methods, clonePairs: currentClones)) { }

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Single(report.NewDuplicates);
        Assert.Equal("m1", report.NewDuplicates[0].MethodIdA);
        Assert.Equal("m3", report.NewDuplicates[0].MethodIdB);
    }

    [Fact]
    public async Task Compare_MissingBaseline_Throws()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "A", "App.Svc.A()", "void", [], null, 1, 5,
                Accessibility.Public, false, false, false, false, false)
        };
        await using (await CreateDatabase(_currentDbPath, methods)) { }

        var detector = new DriftDetector();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            detector.CompareAsync(_currentDbPath, "/nonexistent/baseline.db"));
    }

    [Fact]
    public async Task Compare_MissingCurrent_Throws()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "A", "App.Svc.A()", "void", [], null, 1, 5,
                Accessibility.Public, false, false, false, false, false)
        };
        await using (await CreateDatabase(_baselineDbPath, methods)) { }

        var detector = new DriftDetector();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            detector.CompareAsync("/nonexistent/current.db", _baselineDbPath));
    }

    [Fact]
    public async Task Compare_CancellationToken_ThrowsWhenCancelled()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "A", "App.Svc.A()", "void", [], null, 1, 5,
                Accessibility.Public, false, false, false, false, false)
        };
        await using (await CreateDatabase(_currentDbPath, methods)) { }
        await using (await CreateDatabase(_baselineDbPath, methods)) { }

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var detector = new DriftDetector();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            detector.CompareAsync(_currentDbPath, _baselineDbPath, cts.Token));
    }

    [Fact]
    public async Task Compare_EmptyDatabases_NoDrift()
    {
        await using (await CreateDatabase(_currentDbPath)) { }
        await using (await CreateDatabase(_baselineDbPath)) { }

        var detector = new DriftDetector();
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Empty(report.NewMethods);
        Assert.Empty(report.RemovedMethods);
        Assert.Empty(report.Regressions);
        Assert.Empty(report.NewDuplicates);
        Assert.Empty(report.IntentScattering);
    }

    [Fact]
    public async Task Compare_CustomThresholds()
    {
        var methods = new List<MethodModel>
        {
            new("m1", "Process", "App.Svc.Process()", "void", [], null, 1, 30,
                Accessibility.Public, false, false, false, false, false)
        };

        await using (await CreateDatabase(_baselineDbPath, methods, [("m1", 10, 20, 2)])) { }
        await using (await CreateDatabase(_currentDbPath, methods, [("m1", 15, 20, 2)])) { } // 50% increase

        // With a very high threshold, shouldn't flag
        var options = new DriftDetectorOptions { ComplexityPercentageThreshold = 0.75 };
        var detector = new DriftDetector(options);
        var report = await detector.CompareAsync(_currentDbPath, _baselineDbPath);

        Assert.Empty(report.Regressions);
    }
}
