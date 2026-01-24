namespace AiCodeGraph.Tests;

public abstract class TempDirectoryFixture : IAsyncDisposable, IDisposable
{
    protected readonly string TempDir;

    protected TempDirectoryFixture(string prefix)
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDir);
    }

    protected string GetDbPath(string filename = "graph.db") => Path.Combine(TempDir, filename);

    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public virtual void Dispose()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, recursive: true);
    }
}
