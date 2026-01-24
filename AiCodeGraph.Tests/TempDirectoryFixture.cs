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
        if (!Directory.Exists(TempDir))
            return;

        // On Windows, SQLite WAL mode keeps files locked briefly after disposal
        // Retry deletion with delays to allow locks to be released
        var attempts = 0;
        var maxAttempts = 10;
        var delay = 50; // ms

        while (attempts < maxAttempts)
        {
            try
            {
                // Force garbage collection to release any lingering file handles
                if (attempts > 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(delay);
                }

                Directory.Delete(TempDir, recursive: true);
                return; // Success
            }
            catch (IOException) when (attempts < maxAttempts - 1)
            {
                attempts++;
                delay *= 2; // Exponential backoff
            }
        }

        // Final attempt - let it throw if it still fails
        Directory.Delete(TempDir, recursive: true);
    }
}
