using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Tests;

public class DependencyRuleEngineTests : TempDirectoryFixture
{
    public DependencyRuleEngineTests() : base("dep-rule-test") { }

    [Theory]
    [InlineData("MyApp.Domain.UserEntity", "*.Domain.*", true)]
    [InlineData("MyApp.Domain.UserEntity", "*.Infrastructure.*", false)]
    [InlineData("MyApp.Controllers.UserController", "*.Controllers.*", true)]
    [InlineData("MyApp.Services.UserService", "*Service*", true)]
    [InlineData("MyApp.Repository.UserRepo", "*Repo*", true)]
    [InlineData("abc", "*", true)]
    [InlineData("abc", "???", true)]
    [InlineData("abcd", "???", false)]
    public void MatchesPattern_ReturnsExpectedResult(string fullName, string pattern, bool expected)
    {
        var result = DependencyRuleEngine.MatchesPattern(fullName, pattern);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CreateWithDefaults_HasCleanArchitectureRules()
    {
        var engine = DependencyRuleEngine.CreateWithDefaults();

        Assert.NotEmpty(engine.Rules);
        Assert.Contains(engine.Rules, r => r.Name.Contains("Domain → Infrastructure"));
        Assert.Contains(engine.Rules, r => r.Type == RuleType.Forbidden);
    }

    [Fact]
    public void LoadFromJson_WithIncludeDefaults_MergesRules()
    {
        var json = """
        {
            "includeDefaults": true,
            "rules": [
                {
                    "name": "Custom Rule",
                    "from": "*.Custom.*",
                    "to": "*.Legacy.*",
                    "type": "forbidden",
                    "explanation": "Custom code should not use legacy"
                }
            ]
        }
        """;

        var engine = DependencyRuleEngine.LoadFromJson(json);

        Assert.True(engine.Rules.Count > 1);
        Assert.Contains(engine.Rules, r => r.Name == "Custom Rule");
        Assert.Contains(engine.Rules, r => r.Name.Contains("Domain → Infrastructure")); // Default
    }

    [Fact]
    public void LoadFromJson_WithoutDefaults_OnlyCustomRules()
    {
        var json = """
        {
            "includeDefaults": false,
            "rules": [
                {
                    "name": "Only Rule",
                    "from": "*.A.*",
                    "to": "*.B.*",
                    "type": "forbidden"
                }
            ]
        }
        """;

        var engine = DependencyRuleEngine.LoadFromJson(json);

        Assert.Single(engine.Rules);
        Assert.Equal("Only Rule", engine.Rules[0].Name);
    }

    [Fact]
    public void LoadFromJson_ParsesSeverity()
    {
        var json = """
        {
            "includeDefaults": false,
            "rules": [
                {
                    "name": "Warning Rule",
                    "from": "*.A.*",
                    "to": "*.B.*",
                    "type": "forbidden",
                    "severity": "warning"
                }
            ]
        }
        """;

        var engine = DependencyRuleEngine.LoadFromJson(json);

        Assert.Equal(ViolationSeverity.Warning, engine.Rules[0].Severity);
    }

    [Fact]
    public void GenerateSampleRulesJson_ReturnsValidJson()
    {
        var json = DependencyRuleEngine.GenerateSampleRulesJson();

        Assert.Contains("includeDefaults", json);
        Assert.Contains("rules", json);
        Assert.Contains("from", json);
        Assert.Contains("to", json);
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
            // Create a graph with a violation:
            // MyApp.Domain.UserEntity calls MyApp.Infrastructure.UserRepository (forbidden!)
            ins.CommandText = """
                INSERT INTO Projects (Id, Name, FilePath) VALUES ('proj1', 'TestProject', '/test/test.csproj');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns1', 'MyApp.Domain', 'proj1');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns2', 'MyApp.Infrastructure', 'proj1');
                INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES ('ns3', 'MyApp.Services', 'proj1');
                INSERT INTO Types (Id, Name, FullName, NamespaceId, Kind) VALUES
                    ('t1', 'UserEntity', 'MyApp.Domain.UserEntity', 'ns1', 'Class'),
                    ('t2', 'UserRepository', 'MyApp.Infrastructure.UserRepository', 'ns2', 'Class'),
                    ('t3', 'UserService', 'MyApp.Services.UserService', 'ns3', 'Class');
                INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, FilePath) VALUES
                    ('M1', 'GetUser', 'MyApp.Domain.UserEntity.GetUser()', 'void', 't1', 10, 20, '/src/Domain/UserEntity.cs'),
                    ('M2', 'Save', 'MyApp.Infrastructure.UserRepository.Save()', 'void', 't2', 30, 40, '/src/Infrastructure/UserRepository.cs'),
                    ('M3', 'ProcessUser', 'MyApp.Services.UserService.ProcessUser()', 'void', 't3', 50, 60, '/src/Services/UserService.cs');
                """;
            await ins.ExecuteNonQueryAsync();
        }

        // Create calls: M1 -> M2 (violation!), M3 -> M2 (allowed)
        await storage.SaveCallGraphAsync(new List<(string, string)>
        {
            ("M1", "M2"), // Domain → Infrastructure (violation!)
            ("M3", "M2")  // Service → Repository (allowed)
        });

        await storage.SaveMetricsAsync(new List<(string, int, int, int)>
        {
            ("M1", 5, 10, 1),
            ("M2", 3, 8, 1),
            ("M3", 7, 15, 2)
        });

        return storage;
    }

    [Fact]
    public async Task CheckViolationsAsync_DetectsDomainToInfrastructure()
    {
        await using var storage = await CreateTestDatabaseAsync();

        var engine = DependencyRuleEngine.CreateWithDefaults();
        var result = await engine.CheckViolationsAsync(storage);

        Assert.True(result.TotalCallsChecked > 0);
        Assert.NotEmpty(result.Violations);

        var domainViolation = result.Violations.FirstOrDefault(v =>
            v.Rule.Name.Contains("Domain → Infrastructure"));
        Assert.NotNull(domainViolation);
        Assert.Equal("M1", domainViolation.FromMethodId);
        Assert.Equal("M2", domainViolation.ToMethodId);
    }

    [Fact]
    public async Task CheckViolationsAsync_AllowsServiceToRepository()
    {
        await using var storage = await CreateTestDatabaseAsync();

        var engine = DependencyRuleEngine.CreateWithDefaults();
        var result = await engine.CheckViolationsAsync(storage);

        // Service → Repository should NOT be a violation
        var serviceViolation = result.Violations.FirstOrDefault(v =>
            v.FromMethodId == "M3" && v.ToMethodId == "M2");
        Assert.Null(serviceViolation);
    }

    [Fact]
    public async Task CheckViolationsAsync_ReturnsStatistics()
    {
        await using var storage = await CreateTestDatabaseAsync();

        var engine = DependencyRuleEngine.CreateWithDefaults();
        var result = await engine.CheckViolationsAsync(storage);

        Assert.True(result.TotalCallsChecked >= 2);
        Assert.True(result.RulesApplied > 0);
        Assert.True(result.ElapsedTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task CheckViolationsAsync_IncludesFileLocation()
    {
        await using var storage = await CreateTestDatabaseAsync();

        var engine = DependencyRuleEngine.CreateWithDefaults();
        var result = await engine.CheckViolationsAsync(storage);

        var violation = result.Violations.First();
        Assert.NotNull(violation.FromFilePath);
        Assert.NotNull(violation.FromLine);
    }

    [Fact]
    public async Task CheckViolationsAsync_CustomRuleDetectsViolation()
    {
        await using var storage = await CreateTestDatabaseAsync();

        var customRules = new List<DependencyRule>
        {
            new DependencyRule(
                "Service → Repository",
                "*.Services.*",
                "*.Infrastructure.*",
                RuleType.Forbidden,
                "Custom rule: Services should not call repositories directly")
        };

        var engine = new DependencyRuleEngine(customRules);
        var result = await engine.CheckViolationsAsync(storage);

        Assert.NotEmpty(result.Violations);
        var violation = result.Violations.First();
        Assert.Equal("Service → Repository", violation.Rule.Name);
    }
}
