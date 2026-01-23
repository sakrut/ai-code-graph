using AiCodeGraph.Core;

namespace AiCodeGraph.Tests;

public class SolutionDiscoveryTests : IDisposable
{
    private readonly string _tempDir;

    public SolutionDiscoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"acg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void FindSolutionFile_ExplicitPath_ReturnsFullPath()
    {
        var slnPath = Path.Combine(_tempDir, "Test.sln");
        File.WriteAllText(slnPath, "");

        var result = SolutionDiscovery.FindSolutionFile(slnPath);

        Assert.Equal(Path.GetFullPath(slnPath), result);
    }

    [Fact]
    public void FindSolutionFile_ExplicitPathNotFound_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            SolutionDiscovery.FindSolutionFile("/nonexistent/path.sln"));
    }

    [Fact]
    public void FindSolutionFile_AutoDiscovery_FindsInCurrentDir()
    {
        var slnPath = Path.Combine(_tempDir, "MyApp.sln");
        File.WriteAllText(slnPath, "");

        var result = SolutionDiscovery.FindSolutionFile(null, _tempDir);

        Assert.Equal(slnPath, result);
    }

    [Fact]
    public void FindSolutionFile_AutoDiscovery_FindsInParentDir()
    {
        var childDir = Path.Combine(_tempDir, "src", "project");
        Directory.CreateDirectory(childDir);
        var slnPath = Path.Combine(_tempDir, "MyApp.sln");
        File.WriteAllText(slnPath, "");

        var result = SolutionDiscovery.FindSolutionFile(null, childDir);

        Assert.Equal(slnPath, result);
    }

    [Fact]
    public void FindSolutionFile_MultipleSolutions_ThrowsInvalidOperation()
    {
        File.WriteAllText(Path.Combine(_tempDir, "A.sln"), "");
        File.WriteAllText(Path.Combine(_tempDir, "B.sln"), "");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SolutionDiscovery.FindSolutionFile(null, _tempDir));
        Assert.Contains("Multiple .sln files", ex.Message);
    }

    [Fact]
    public void FindSolutionFile_NoSolution_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            SolutionDiscovery.FindSolutionFile(null, _tempDir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
