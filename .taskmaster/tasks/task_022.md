# Task ID: 22

**Title:** VectorIndex Caching in Similar/Search Commands

**Status:** done

**Dependencies:** None

**Priority:** medium

**Description:** Cache VectorIndex instances per database path in Program.cs so repeated similar/search queries don't rebuild the index from scratch.

**Details:**

File: AiCodeGraph.Cli/Program.cs lines 549-551

Currently every similar/search command call does:
```csharp
var vectorIndex = new VectorIndex();
vectorIndex.BuildIndex(allEmbeddings);
```

Add a static cache at the top of Program.cs:

```csharp
private static readonly Dictionary<string, VectorIndex> _vectorIndexCache = new();
private static readonly object _cacheLock = new();

private static VectorIndex GetOrBuildVectorIndex(string dbPath, List<(string MethodId, float[] Vector)> embeddings)
{
    var fullPath = Path.GetFullPath(dbPath);
    lock (_cacheLock)
    {
        if (_vectorIndexCache.TryGetValue(fullPath, out var cached))
            return cached;
        
        var index = new VectorIndex();
        index.BuildIndex(embeddings);
        _vectorIndexCache[fullPath] = index;
        return index;
    }
}

// Call this after successful analyze to invalidate cache
private static void InvalidateVectorCache(string dbPath)
{
    var fullPath = Path.GetFullPath(dbPath);
    lock (_cacheLock)
    {
        _vectorIndexCache.Remove(fullPath);
    }
}
```

Update the similar command, search command, and analyze command to use these methods. Call InvalidateVectorCache at the end of a successful analyze run.

**Test Strategy:**

Write a test that calls the similar command twice with same DB and verifies the second call is faster (or mock VectorIndex to verify BuildIndex not called twice). Verify cache invalidation works after analyze. Verify different DB paths get separate caches.

## Subtasks

### 22.1. Add static VectorIndex cache and helper methods to Program.cs

**Status:** pending  
**Dependencies:** None  

Add a static Dictionary<string, VectorIndex> cache, a lock object, a GetOrBuildVectorIndex helper, and an InvalidateVectorCache helper as static fields/methods at the top-level scope of Program.cs (since it uses top-level statements, these will be local static methods or a partial class wrapper).

**Details:**

Since Program.cs uses top-level statements (no explicit class), add the cache as a static field in a partial Program class at the bottom of the file or use a nested static helper class. The cache maps Path.GetFullPath(dbPath) to VectorIndex instances. GetOrBuildVectorIndex takes dbPath and the embeddings list, checks the cache under lock, and either returns the cached index or builds a new one via BuildIndex. InvalidateVectorCache removes an entry by normalized path. Use `private static readonly Dictionary<string, VectorIndex> _vectorIndexCache = new();` and `private static readonly object _cacheLock = new();`. The lock ensures thread-safety for the CLI tool's potential parallel invocations within the same process (e.g., MCP server mode).

### 22.2. Update the similar command to use GetOrBuildVectorIndex

**Status:** pending  
**Dependencies:** 22.1  

Replace the direct VectorIndex instantiation and BuildIndex call in the similar command handler (Program.cs lines 549-550) with a call to GetOrBuildVectorIndex, passing the dbPath and the filtered embeddings list.

**Details:**

In the similarCommand.SetAction handler (starting at line 506), replace:
```csharp
var index = new VectorIndex();
index.BuildIndex(allEmbeddings.Where(e => e.MethodId != targetId).ToList());
```
with:
```csharp
var index = GetOrBuildVectorIndex(dbPath, allEmbeddings);
```
Note: The similar command currently excludes the target method from the index. For caching to work correctly, cache the full index (all embeddings) and filter results after search instead, or use a cache key that includes the exclusion. The simpler approach is to cache the full index and just exclude the target from results. Update the search call to request top+1 results and filter out the target method from the results list.

### 22.3. Update the search command to use GetOrBuildVectorIndex

**Status:** pending  
**Dependencies:** 22.1  

Replace the direct VectorIndex instantiation and BuildIndex call in the search command handler (Program.cs lines 759-760) with a call to GetOrBuildVectorIndex.

**Details:**

In the searchCommand.SetAction handler (starting at line 728), replace:
```csharp
var index = new VectorIndex();
index.BuildIndex(allEmbeddings);
```
with:
```csharp
var index = GetOrBuildVectorIndex(dbPath, allEmbeddings);
```
The search command already uses the full embeddings list without exclusion, so this is a straightforward replacement. The cache key (normalized dbPath) will match between similar and search commands using the same database, so they share the cached index.

### 22.4. Add cache invalidation call after successful analyze

**Status:** pending  
**Dependencies:** 22.1  

Call InvalidateVectorCache at the end of the analyze command's success path (around line 195) so that subsequent similar/search commands rebuild the index with fresh embeddings.

**Details:**

In the analyzeCommand.SetAction handler, after the 'Analysis complete:' summary output (line 195, `Console.WriteLine($"  Output: {Path.GetFullPath(dbPath)}");`), add:
```csharp
InvalidateVectorCache(dbPath);
```
This ensures that after a successful analysis run writes new embeddings to the database, any cached VectorIndex for that database path is discarded. The next similar/search command will rebuild the index with the updated embeddings. The invalidation should occur inside the try block, only on the success path (not in catch blocks).

### 22.5. Update MCP server similar/search tools to use shared cache

**Status:** pending  
**Dependencies:** 22.1  

Update the McpServer.cs ToolSearchCode (line 358) and ToolGetSimilar (line 520) methods to use a similar caching mechanism, since the MCP server is a long-running process where caching provides the most benefit.

**Details:**

McpServer already has _dbPath as an instance field. Add a private VectorIndex? _cachedIndex field and a private string? _cachedDbPath field to McpServer. In ToolSearchCode (line 358) and ToolGetSimilar (line 520), replace:
```csharp
var index = new VectorIndex();
index.BuildIndex(embeddings);
```
with a check against the cached instance. Since McpServer is a single-instance long-running server, a simple instance-level cache without Dictionary is sufficient:
```csharp
private VectorIndex? _cachedVectorIndex;

private VectorIndex GetOrBuildIndex(List<(string MethodId, float[] Vector)> embeddings)
{
    if (_cachedVectorIndex != null && _cachedVectorIndex.Count == embeddings.Count)
        return _cachedVectorIndex;
    var index = new VectorIndex();
    index.BuildIndex(embeddings);
    _cachedVectorIndex = index;
    return index;
}
```
For ToolGetSimilar, cache the full index and filter the target from results rather than rebuilding without the target each time.
