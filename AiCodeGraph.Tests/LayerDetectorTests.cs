using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class LayerDetectorTests : TempDirectoryFixture
{
    public LayerDetectorTests() : base("layer-test") { }

    [Fact]
    public void IsDependencyValid_PresentationToApplication_ReturnsTrue()
    {
        var detector = new LayerDetector();
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Presentation, ArchitecturalLayer.Application));
    }

    [Fact]
    public void IsDependencyValid_ApplicationToDomain_ReturnsTrue()
    {
        var detector = new LayerDetector();
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Application, ArchitecturalLayer.Domain));
    }

    [Fact]
    public void IsDependencyValid_InfrastructureToDomain_ReturnsTrue()
    {
        var detector = new LayerDetector();
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Infrastructure, ArchitecturalLayer.Domain));
    }

    [Fact]
    public void IsDependencyValid_DomainToInfrastructure_ReturnsFalse()
    {
        var detector = new LayerDetector();
        Assert.False(detector.IsDependencyValid(ArchitecturalLayer.Domain, ArchitecturalLayer.Infrastructure));
    }

    [Fact]
    public void IsDependencyValid_DomainToApplication_ReturnsFalse()
    {
        var detector = new LayerDetector();
        Assert.False(detector.IsDependencyValid(ArchitecturalLayer.Domain, ArchitecturalLayer.Application));
    }

    [Fact]
    public void IsDependencyValid_InfrastructureToPresentation_ReturnsFalse()
    {
        var detector = new LayerDetector();
        Assert.False(detector.IsDependencyValid(ArchitecturalLayer.Infrastructure, ArchitecturalLayer.Presentation));
    }

    [Fact]
    public void IsDependencyValid_SameLayer_ReturnsTrue()
    {
        var detector = new LayerDetector();
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Application, ArchitecturalLayer.Application));
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Domain, ArchitecturalLayer.Domain));
    }

    [Fact]
    public void IsDependencyValid_UnknownLayer_ReturnsTrue()
    {
        var detector = new LayerDetector();
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Unknown, ArchitecturalLayer.Domain));
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Domain, ArchitecturalLayer.Unknown));
    }

    [Fact]
    public void IsDependencyValid_AnyToShared_ReturnsTrue()
    {
        var detector = new LayerDetector();
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Presentation, ArchitecturalLayer.Shared));
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Application, ArchitecturalLayer.Shared));
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Domain, ArchitecturalLayer.Shared));
        Assert.True(detector.IsDependencyValid(ArchitecturalLayer.Infrastructure, ArchitecturalLayer.Shared));
    }

    [Fact]
    public async Task DetectLayersAsync_WithLayeredTypes_AssignsCorrectLayers()
    {
        var dbPath = Path.Combine(TempDir, "layers.db");
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES
                    ('ns1', 'MyApp.Controllers', 'proj1'),
                    ('ns2', 'MyApp.Services', 'proj1'),
                    ('ns3', 'MyApp.Domain', 'proj1'),
                    ('ns4', 'MyApp.Infrastructure', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'UserController', 'MyApp.Controllers.UserController', 'ns1', 'Class'),
                    ('type2', 'UserService', 'MyApp.Services.UserService', 'ns2', 'Class'),
                    ('type3', 'User', 'MyApp.Domain.User', 'ns3', 'Class'),
                    ('type4', 'UserRepository', 'MyApp.Infrastructure.UserRepository', 'ns4', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'GetUser', 'MyApp.Controllers.UserController.GetUser()', 'User', 'type1', 10, 20, 'Public'),
                    ('m2', 'FindUser', 'MyApp.Services.UserService.FindUser()', 'User', 'type2', 10, 20, 'Public'),
                    ('m3', 'GetId', 'MyApp.Domain.User.GetId()', 'int', 'type3', 10, 20, 'Public'),
                    ('m4', 'GetById', 'MyApp.Infrastructure.UserRepository.GetById()', 'User', 'type4', 10, 20, 'Public');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = await detector.DetectLayersAsync(storage);

            Assert.Equal(4, assignments.Count);

            var controllerAssignment = assignments.First(a => a.TypeId.Contains("UserController"));
            Assert.Equal(ArchitecturalLayer.Presentation, controllerAssignment.Layer);
            Assert.True(controllerAssignment.Confidence >= 0.9f);

            var serviceAssignment = assignments.First(a => a.TypeId.Contains("UserService"));
            Assert.Equal(ArchitecturalLayer.Application, serviceAssignment.Layer);
            Assert.True(serviceAssignment.Confidence >= 0.8f);

            var domainAssignment = assignments.First(a => a.TypeId == "MyApp.Domain.User");
            Assert.Equal(ArchitecturalLayer.Domain, domainAssignment.Layer);

            var repoAssignment = assignments.First(a => a.TypeId.Contains("UserRepository"));
            Assert.Equal(ArchitecturalLayer.Infrastructure, repoAssignment.Layer);
            Assert.True(repoAssignment.Confidence >= 0.9f);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task DetectLayersAsync_UnknownNamespace_AssignsUnknown()
    {
        var dbPath = Path.Combine(TempDir, "unknown.db");
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'RandomNamespace', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES ('type1', 'SomeClass', 'RandomNamespace.SomeClass', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'DoSomething', 'RandomNamespace.SomeClass.DoSomething()', 'void', 'type1', 10, 20, 'Public');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = await detector.DetectLayersAsync(storage);

            Assert.Single(assignments);
            Assert.Equal(ArchitecturalLayer.Unknown, assignments[0].Layer);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task DetectLayersAsync_TypeNameHints_TakePrecedence()
    {
        var dbPath = Path.Combine(TempDir, "hints.db");
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
            // Put a Controller in an Infrastructure namespace - type name should win
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp.Infrastructure.Web', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'HealthController', 'MyApp.Infrastructure.Web.HealthController', 'ns1', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'Check', 'MyApp.Infrastructure.Web.HealthController.Check()', 'IActionResult', 'type1', 10, 20, 'Public');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = await detector.DetectLayersAsync(storage);

            Assert.Single(assignments);
            // Type name "Controller" suffix should make it Presentation with high confidence
            Assert.Equal(ArchitecturalLayer.Presentation, assignments[0].Layer);
            Assert.Equal(1.0f, assignments[0].Confidence);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task RefineByDependencyDirectionAsync_ValidDependency_DoesNotLowerConfidence()
    {
        var dbPath = Path.Combine(TempDir, "valid-dep.db");
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
            // Controller calls Service (Presentation → Application is valid)
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES
                    ('ns1', 'MyApp.Controllers', 'proj1'),
                    ('ns2', 'MyApp.Services', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'UserController', 'MyApp.Controllers.UserController', 'ns1', 'Class'),
                    ('type2', 'UserService', 'MyApp.Services.UserService', 'ns2', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'GetUser', 'void MyApp.Controllers.UserController.GetUser()', 'void', 'type1', 10, 20, 'Public'),
                    ('m2', 'FindUser', 'User MyApp.Services.UserService.FindUser()', 'User', 'type2', 10, 20, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('m1', 1, 10, 1), ('m2', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES ('m1', 'm2');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = new List<LayerAssignment>
            {
                new("MyApp.Controllers.UserController", ArchitecturalLayer.Presentation, 0.9f, "Controller pattern"),
                new("MyApp.Services.UserService", ArchitecturalLayer.Application, 0.9f, "Service pattern")
            };

            var refined = await detector.RefineByDependencyDirectionAsync(assignments, storage);

            Assert.Equal(2, refined.Count);
            var controllerAssignment = refined.First(a => a.TypeId.Contains("UserController"));
            // Valid dependency - confidence should not decrease
            Assert.Equal(0.9f, controllerAssignment.Confidence);
            Assert.DoesNotContain("WARNING", controllerAssignment.Reason);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task RefineByDependencyDirectionAsync_Violation_LowersConfidence()
    {
        var dbPath = Path.Combine(TempDir, "violation.db");
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
            // Repository calls Controller (Infrastructure → Presentation is invalid)
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES
                    ('ns1', 'MyApp.Controllers', 'proj1'),
                    ('ns2', 'MyApp.Infrastructure', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'UserController', 'MyApp.Controllers.UserController', 'ns1', 'Class'),
                    ('type2', 'UserRepository', 'MyApp.Infrastructure.UserRepository', 'ns2', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'GetUser', 'void MyApp.Controllers.UserController.GetUser()', 'void', 'type1', 10, 20, 'Public'),
                    ('m2', 'GetById', 'User MyApp.Infrastructure.UserRepository.GetById()', 'User', 'type2', 10, 20, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('m1', 1, 10, 1), ('m2', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES ('m2', 'm1');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = new List<LayerAssignment>
            {
                new("MyApp.Controllers.UserController", ArchitecturalLayer.Presentation, 0.9f, "Controller pattern"),
                new("MyApp.Infrastructure.UserRepository", ArchitecturalLayer.Infrastructure, 0.9f, "Repository pattern")
            };

            var refined = await detector.RefineByDependencyDirectionAsync(assignments, storage);

            var repoAssignment = refined.First(a => a.TypeId.Contains("UserRepository"));
            // Violation - confidence should decrease by 10%
            Assert.True(repoAssignment.Confidence < 0.9f);
            Assert.Equal(0.81f, repoAssignment.Confidence, 2);
            Assert.Contains("WARNING", repoAssignment.Reason);
            Assert.Contains("Infrastructure→Presentation", repoAssignment.Reason);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task RefineByDependencyDirectionAsync_DomainToInfrastructure_IsFlagged()
    {
        var dbPath = Path.Combine(TempDir, "domain-infra.db");
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
            // Domain entity calls Infrastructure (violation)
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES
                    ('ns1', 'MyApp.Domain', 'proj1'),
                    ('ns2', 'MyApp.Infrastructure', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'User', 'MyApp.Domain.User', 'ns1', 'Class'),
                    ('type2', 'DbHelper', 'MyApp.Infrastructure.DbHelper', 'ns2', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'Save', 'void MyApp.Domain.User.Save()', 'void', 'type1', 10, 20, 'Public'),
                    ('m2', 'Execute', 'void MyApp.Infrastructure.DbHelper.Execute()', 'void', 'type2', 10, 20, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('m1', 1, 10, 1), ('m2', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES ('m1', 'm2');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = new List<LayerAssignment>
            {
                new("MyApp.Domain.User", ArchitecturalLayer.Domain, 0.9f, "Domain namespace"),
                new("MyApp.Infrastructure.DbHelper", ArchitecturalLayer.Infrastructure, 0.9f, "Infrastructure namespace")
            };

            var refined = await detector.RefineByDependencyDirectionAsync(assignments, storage);

            var userAssignment = refined.First(a => a.TypeId == "MyApp.Domain.User");
            Assert.Contains("Domain→Infrastructure", userAssignment.Reason);
            Assert.Contains("WARNING", userAssignment.Reason);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task RefineByDependencyDirectionAsync_ConfidenceNeverBelowMinimum()
    {
        var dbPath = Path.Combine(TempDir, "min-conf.db");
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
            // Domain entity calls many Infrastructure types (many violations)
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES
                    ('ns1', 'MyApp.Domain', 'proj1'),
                    ('ns2', 'MyApp.Infrastructure', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'BadEntity', 'MyApp.Domain.BadEntity', 'ns1', 'Class'),
                    ('type2', 'Db1', 'MyApp.Infrastructure.Db1', 'ns2', 'Class'),
                    ('type3', 'Db2', 'MyApp.Infrastructure.Db2', 'ns2', 'Class'),
                    ('type4', 'Db3', 'MyApp.Infrastructure.Db3', 'ns2', 'Class'),
                    ('type5', 'Db4', 'MyApp.Infrastructure.Db4', 'ns2', 'Class'),
                    ('type6', 'Db5', 'MyApp.Infrastructure.Db5', 'ns2', 'Class'),
                    ('type7', 'Db6', 'MyApp.Infrastructure.Db6', 'ns2', 'Class'),
                    ('type8', 'Db7', 'MyApp.Infrastructure.Db7', 'ns2', 'Class'),
                    ('type9', 'Db8', 'MyApp.Infrastructure.Db8', 'ns2', 'Class'),
                    ('type10', 'Db9', 'MyApp.Infrastructure.Db9', 'ns2', 'Class'),
                    ('type11', 'Db10', 'MyApp.Infrastructure.Db10', 'ns2', 'Class'),
                    ('type12', 'Db11', 'MyApp.Infrastructure.Db11', 'ns2', 'Class'),
                    ('type13', 'Db12', 'MyApp.Infrastructure.Db12', 'ns2', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'DoAll', 'void MyApp.Domain.BadEntity.DoAll()', 'void', 'type1', 10, 20, 'Public'),
                    ('m2', 'A', 'void MyApp.Infrastructure.Db1.A()', 'void', 'type2', 10, 20, 'Public'),
                    ('m3', 'A', 'void MyApp.Infrastructure.Db2.A()', 'void', 'type3', 10, 20, 'Public'),
                    ('m4', 'A', 'void MyApp.Infrastructure.Db3.A()', 'void', 'type4', 10, 20, 'Public'),
                    ('m5', 'A', 'void MyApp.Infrastructure.Db4.A()', 'void', 'type5', 10, 20, 'Public'),
                    ('m6', 'A', 'void MyApp.Infrastructure.Db5.A()', 'void', 'type6', 10, 20, 'Public'),
                    ('m7', 'A', 'void MyApp.Infrastructure.Db6.A()', 'void', 'type7', 10, 20, 'Public'),
                    ('m8', 'A', 'void MyApp.Infrastructure.Db7.A()', 'void', 'type8', 10, 20, 'Public'),
                    ('m9', 'A', 'void MyApp.Infrastructure.Db8.A()', 'void', 'type9', 10, 20, 'Public'),
                    ('m10', 'A', 'void MyApp.Infrastructure.Db9.A()', 'void', 'type10', 10, 20, 'Public'),
                    ('m11', 'A', 'void MyApp.Infrastructure.Db10.A()', 'void', 'type11', 10, 20, 'Public'),
                    ('m12', 'A', 'void MyApp.Infrastructure.Db11.A()', 'void', 'type12', 10, 20, 'Public'),
                    ('m13', 'A', 'void MyApp.Infrastructure.Db12.A()', 'void', 'type13', 10, 20, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('m1', 1, 10, 1), ('m2', 1, 10, 1), ('m3', 1, 10, 1), ('m4', 1, 10, 1),
                    ('m5', 1, 10, 1), ('m6', 1, 10, 1), ('m7', 1, 10, 1), ('m8', 1, 10, 1),
                    ('m9', 1, 10, 1), ('m10', 1, 10, 1), ('m11', 1, 10, 1), ('m12', 1, 10, 1),
                    ('m13', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES
                    ('m1', 'm2'), ('m1', 'm3'), ('m1', 'm4'), ('m1', 'm5'), ('m1', 'm6'),
                    ('m1', 'm7'), ('m1', 'm8'), ('m1', 'm9'), ('m1', 'm10'), ('m1', 'm11'),
                    ('m1', 'm12'), ('m1', 'm13');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = new List<LayerAssignment>
            {
                new("MyApp.Domain.BadEntity", ArchitecturalLayer.Domain, 0.5f, "Domain namespace"),
                new("MyApp.Infrastructure.Db1", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db2", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db3", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db4", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db5", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db6", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db7", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db8", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db9", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db10", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db11", ArchitecturalLayer.Infrastructure, 0.9f, "Infra"),
                new("MyApp.Infrastructure.Db12", ArchitecturalLayer.Infrastructure, 0.9f, "Infra")
            };

            var refined = await detector.RefineByDependencyDirectionAsync(assignments, storage);

            var badEntity = refined.First(a => a.TypeId == "MyApp.Domain.BadEntity");
            // With 12 violations and starting confidence of 0.5, would go negative
            // but should be clamped to minimum 0.1
            Assert.True(badEntity.Confidence >= 0.1f);
            Assert.Equal(0.1f, badEntity.Confidence);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    [Fact]
    public async Task RefineByDependencyDirectionAsync_ConsistentBehavior_BoostsLowConfidence()
    {
        var dbPath = Path.Combine(TempDir, "boost.db");
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
            // Service with low confidence but calls Domain types consistently (valid)
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES
                    ('ns1', 'MyApp.Stuff', 'proj1'),
                    ('ns2', 'MyApp.Domain', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('type1', 'SomeService', 'MyApp.Stuff.SomeService', 'ns1', 'Class'),
                    ('type2', 'User', 'MyApp.Domain.User', 'ns2', 'Class'),
                    ('type3', 'Order', 'MyApp.Domain.Order', 'ns2', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, Accessibility) VALUES
                    ('m1', 'Process', 'void MyApp.Stuff.SomeService.Process()', 'void', 'type1', 10, 20, 'Public'),
                    ('m2', 'GetId', 'int MyApp.Domain.User.GetId()', 'int', 'type2', 10, 20, 'Public'),
                    ('m3', 'GetTotal', 'decimal MyApp.Domain.Order.GetTotal()', 'decimal', 'type3', 10, 20, 'Public');
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES
                    ('m1', 1, 10, 1), ('m2', 1, 10, 1), ('m3', 1, 10, 1);
                INSERT INTO MethodCalls (CallerId, CalleeId) VALUES ('m1', 'm2'), ('m1', 'm3');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        try
        {
            var detector = new LayerDetector();
            var assignments = new List<LayerAssignment>
            {
                // Low confidence assignment (pattern match was weak)
                new("MyApp.Stuff.SomeService", ArchitecturalLayer.Application, 0.6f, "Partial match: Service"),
                new("MyApp.Domain.User", ArchitecturalLayer.Domain, 0.9f, "Domain namespace"),
                new("MyApp.Domain.Order", ArchitecturalLayer.Domain, 0.9f, "Domain namespace")
            };

            var refined = await detector.RefineByDependencyDirectionAsync(assignments, storage);

            var serviceAssignment = refined.First(a => a.TypeId.Contains("SomeService"));
            // Should be boosted from 0.6 to 0.7 due to consistent valid dependencies
            Assert.True(serviceAssignment.Confidence > 0.6f);
            Assert.Equal(0.7f, serviceAssignment.Confidence, 2);
            Assert.Contains("consistent dependencies", serviceAssignment.Reason);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }
}
