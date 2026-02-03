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
}
