using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class ProtectedZoneManagerTests : TempDirectoryFixture
{
    public ProtectedZoneManagerTests() : base("protected-zone-test") { }

    [Theory]
    [InlineData("MyApp.Security.AuthService", "*.Security.*", true)]
    [InlineData("MyApp.Services.UserService", "*.Security.*", false)]
    [InlineData("MyApp.Payment.PaymentProcessor", "*.Payment*", true)]
    [InlineData("MyApp.PaymentService", "*Payment*", true)]
    [InlineData("MyApp.Controllers.UserController", "*.Controllers.*", true)]
    [InlineData("MyApp.Domain.Entity", "*.*.*", true)]
    [InlineData("abc", "???", true)]
    [InlineData("abcd", "???", false)]
    public void MatchesPattern_ReturnsExpectedResult(string fullName, string pattern, bool expected)
    {
        var manager = new ProtectedZoneManager();
        var result = manager.MatchesPattern(fullName, pattern);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CheckProtection_WhenNotProtected_ReturnsFalse()
    {
        var zones = new List<ProtectedZone>
        {
            new("*.Security.*", ProtectionLevel.DoNotModify, "Security code")
        };
        var manager = new ProtectedZoneManager(zones);

        var result = manager.CheckProtection("MyApp.Services.UserService.GetUser()");

        Assert.False(result.IsProtected);
        Assert.Null(result.Zone);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void CheckProtection_WhenProtected_ReturnsWarning()
    {
        var zones = new List<ProtectedZone>
        {
            new("*.Security.*", ProtectionLevel.DoNotModify, "Critical security code", "security@test.com")
        };
        var manager = new ProtectedZoneManager(zones);

        var result = manager.CheckProtection("MyApp.Security.AuthService.Validate()");

        Assert.True(result.IsProtected);
        Assert.NotNull(result.Zone);
        Assert.Equal("*.Security.*", result.Zone.Pattern);
        Assert.Contains("DO NOT MODIFY", result.WarningMessage);
        Assert.Contains("security@test.com", result.WarningMessage);
    }

    [Fact]
    public void CheckProtection_RequireApproval_ReturnsCorrectLevel()
    {
        var zones = new List<ProtectedZone>
        {
            new("*.Payment*", ProtectionLevel.RequireApproval, "PCI compliance")
        };
        var manager = new ProtectedZoneManager(zones);

        var result = manager.CheckProtection("MyApp.PaymentService.ProcessPayment()");

        Assert.True(result.IsProtected);
        Assert.Equal(ProtectionLevel.RequireApproval, result.Zone!.Level);
        Assert.Contains("REQUIRES APPROVAL", result.WarningMessage);
    }

    [Fact]
    public void CheckProtection_Deprecated_ReturnsCorrectLevel()
    {
        var zones = new List<ProtectedZone>
        {
            new("*.Legacy*", ProtectionLevel.Deprecated, "Scheduled for removal")
        };
        var manager = new ProtectedZoneManager(zones);

        var result = manager.CheckProtection("MyApp.LegacyAdapter.Convert()");

        Assert.True(result.IsProtected);
        Assert.Equal(ProtectionLevel.Deprecated, result.Zone!.Level);
        Assert.Contains("DEPRECATED", result.WarningMessage);
    }

    [Fact]
    public void LoadFromJson_ParsesValidConfig()
    {
        var json = """
        {
            "zones": [
                {
                    "pattern": "*.Security.*",
                    "level": "DoNotModify",
                    "reason": "Security code",
                    "owner": "security@test.com"
                },
                {
                    "pattern": "*.Payment*",
                    "level": "RequireApproval",
                    "reason": "PCI compliance"
                }
            ]
        }
        """;

        var manager = ProtectedZoneManager.LoadFromJson(json);

        Assert.Equal(2, manager.Zones.Count);
        Assert.Equal("*.Security.*", manager.Zones[0].Pattern);
        Assert.Equal(ProtectionLevel.DoNotModify, manager.Zones[0].Level);
        Assert.Equal("security@test.com", manager.Zones[0].OwnerContact);
        Assert.Equal("*.Payment*", manager.Zones[1].Pattern);
        Assert.Equal(ProtectionLevel.RequireApproval, manager.Zones[1].Level);
    }

    [Fact]
    public void LoadFromJson_CaseInsensitiveLevel()
    {
        var json = """
        {
            "zones": [
                {
                    "pattern": "*.Test.*",
                    "level": "donotmodify",
                    "reason": "Test"
                }
            ]
        }
        """;

        var manager = ProtectedZoneManager.LoadFromJson(json);

        Assert.Single(manager.Zones);
        Assert.Equal(ProtectionLevel.DoNotModify, manager.Zones[0].Level);
    }

    [Fact]
    public void LoadFromFile_WhenMissing_ReturnsEmptyManager()
    {
        var manager = ProtectedZoneManager.LoadFromFile("/nonexistent/path/zones.json");

        Assert.Empty(manager.Zones);
    }

    [Fact]
    public void LoadFromFile_WhenExists_LoadsZones()
    {
        var json = """
        {
            "zones": [
                {
                    "pattern": "*.Security.*",
                    "level": "DoNotModify",
                    "reason": "Security code"
                }
            ]
        }
        """;
        var path = Path.Combine(TempDir, "zones.json");
        File.WriteAllText(path, json);

        var manager = ProtectedZoneManager.LoadFromFile(path);

        Assert.Single(manager.Zones);
    }

    [Fact]
    public void GenerateSampleJson_ReturnsValidJson()
    {
        var json = ProtectedZoneManager.GenerateSampleJson();

        Assert.Contains("zones", json);
        Assert.Contains("pattern", json);
        Assert.Contains("level", json);
        Assert.Contains("reason", json);
        Assert.Contains("DoNotModify", json);
        Assert.Contains("RequireApproval", json);
    }

    [Fact]
    public void CheckProtection_FirstMatchingZoneWins()
    {
        var zones = new List<ProtectedZone>
        {
            new("*.Security.Auth*", ProtectionLevel.DoNotModify, "Auth is critical"),
            new("*.Security.*", ProtectionLevel.RequireApproval, "Other security code")
        };
        var manager = new ProtectedZoneManager(zones);

        var result = manager.CheckProtection("MyApp.Security.AuthService.Login()");

        Assert.True(result.IsProtected);
        Assert.Equal(ProtectionLevel.DoNotModify, result.Zone!.Level);
        Assert.Equal("Auth is critical", result.Zone.Reason);
    }

    private async Task<StorageService> CreateTestDatabaseAsync()
    {
        var dbPath = Path.Combine(TempDir, "test.db");
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
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp.Security', 'proj1');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns2', 'MyApp.Services', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('t1', 'AuthService', 'MyApp.Security.AuthService', 'ns1', 'Class'),
                    ('t2', 'UserService', 'MyApp.Services.UserService', 'ns2', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, FilePath) VALUES
                    ('M1', 'Login', 'MyApp.Security.AuthService.Login()', 'void', 't1', 10, 20, '/src/Security/AuthService.cs'),
                    ('M2', 'Logout', 'MyApp.Security.AuthService.Logout()', 'void', 't1', 30, 40, '/src/Security/AuthService.cs'),
                    ('M3', 'GetUser', 'MyApp.Services.UserService.GetUser()', 'void', 't2', 50, 60, '/src/Services/UserService.cs');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        return storage;
    }

    [Fact]
    public async Task GetProtectedMethodsAsync_ReturnsMatchingMethods()
    {
        await using var storage = await CreateTestDatabaseAsync();

        var zones = new List<ProtectedZone>
        {
            new("*.Security.*", ProtectionLevel.DoNotModify, "Security code")
        };
        var manager = new ProtectedZoneManager(zones);

        var protected_ = await manager.GetProtectedMethodsAsync(storage);

        Assert.Equal(2, protected_.Count);
        Assert.All(protected_, p => Assert.Contains("Security", p.FullName));
    }

    [Fact]
    public async Task FilterProtectedAsync_ReturnsOnlyProtectedFromList()
    {
        await using var storage = await CreateTestDatabaseAsync();

        var zones = new List<ProtectedZone>
        {
            new("*.Security.*", ProtectionLevel.DoNotModify, "Security code")
        };
        var manager = new ProtectedZoneManager(zones);

        var methodIds = new[] { "M1", "M2", "M3" };
        var protected_ = await manager.FilterProtectedAsync(methodIds, storage);

        Assert.Equal(2, protected_.Count);
        Assert.Contains(protected_, p => p.MethodId == "M1");
        Assert.Contains(protected_, p => p.MethodId == "M2");
        Assert.DoesNotContain(protected_, p => p.MethodId == "M3");
    }
}
