using AiCodeGraph.Core;

namespace AiCodeGraph.Tests;

public class SolutionDiscoveryTests : TempDirectoryFixture
{
    public SolutionDiscoveryTests() : base("acg-test") { }

    [Fact]
    public void FindSolutionFile_ExplicitPath_ReturnsFullPath()
    {
        var slnPath = Path.Combine(TempDir, "Test.sln");
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
        var slnPath = Path.Combine(TempDir, "MyApp.sln");
        File.WriteAllText(slnPath, "");

        var result = SolutionDiscovery.FindSolutionFile(null, TempDir);

        Assert.Equal(slnPath, result);
    }

    [Fact]
    public void FindSolutionFile_AutoDiscovery_FindsInParentDir()
    {
        var childDir = Path.Combine(TempDir, "src", "project");
        Directory.CreateDirectory(childDir);
        var slnPath = Path.Combine(TempDir, "MyApp.sln");
        File.WriteAllText(slnPath, "");

        var result = SolutionDiscovery.FindSolutionFile(null, childDir);

        Assert.Equal(slnPath, result);
    }

    [Fact]
    public void FindSolutionFile_MultipleSolutions_ThrowsInvalidOperation()
    {
        File.WriteAllText(Path.Combine(TempDir, "A.sln"), "");
        File.WriteAllText(Path.Combine(TempDir, "B.sln"), "");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SolutionDiscovery.FindSolutionFile(null, TempDir));
        Assert.Contains("Multiple .sln files", ex.Message);
    }

    [Fact]
    public void FindSolutionFile_NoSolution_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            SolutionDiscovery.FindSolutionFile(null, TempDir));
    }
}
