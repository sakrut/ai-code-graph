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
        // Clear connection pools and retry deletion with delays to allow locks to be released
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var attempts = 0;
        var maxAttempts = 20; // Increased for Windows
        var delay = 100; // Start with 100ms

        while (attempts < maxAttempts)
        {
            try
            {
                // Force garbage collection to release any lingering file handles
                if (attempts > 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect(); // Second GC to clean up finalizer queue
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    Thread.Sleep(delay);
                }

                Directory.Delete(TempDir, recursive: true);
                return; // Success
            }
            catch (IOException) when (attempts < maxAttempts - 1)
            {
                attempts++;
                delay = Math.Min(delay * 2, 2000); // Cap at 2 seconds
            }
        }

        // Final attempt - let it throw if it still fails
        Directory.Delete(TempDir, recursive: true);
    }
}
