# Task ID: 36

**Title:** Add Recently Modified Cluster Members to Context

**Status:** done

**Dependencies:** 34 ✓

**Priority:** low

**Description:** Show other methods in the same intent cluster that were recently modified according to git log, helping developers understand related recent changes.

**Details:**

File: AiCodeGraph.Cli/Program.cs context command

After git blame section, add cluster activity:

```csharp
// Recent cluster activity section
if (clusterInfo != null)
{
    var clusters = await storage.GetClustersAsync(ct);
    var myCluster = clusters.FirstOrDefault(c => c.MethodIds.Contains(methodId));
    
    if (myCluster != null && myCluster.MethodIds.Count > 1)
    {
        var recentChanges = new List<(string MethodName, TimeSpan Age)>();
        
        foreach (var memberId in myCluster.MethodIds.Where(id => id != methodId).Take(10))
        {
            var memberInfo = await storage.GetMethodInfoAsync(memberId, ct);
            if (memberInfo?.FilePath == null || !File.Exists(memberInfo.Value.FilePath)) continue;
            
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"log -1 --format=%ct -L {memberInfo.Value.StartLine},{memberInfo.Value.StartLine + 1}:\"{memberInfo.Value.FilePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = (await process.StandardOutput.ReadToEndAsync(ct)).Trim();
                    await process.WaitForExitAsync(ct);
                    
                    if (long.TryParse(output, out var ts))
                    {
                        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(ts);
                        recentChanges.Add((memberInfo.Value.Name, age));
                    }
                }
            }
            catch { /* skip on any error */ }
        }
        
        if (recentChanges.Count > 0)
        {
            var top3 = recentChanges.OrderBy(r => r.Age).Take(3);
            var formatted = string.Join(", ", top3.Select(r => $"{r.MethodName} ({FormatAge(r.Age)})"));
            Console.WriteLine($"\nRecent cluster activity: {formatted}");
        }
    }
}

private static string FormatAge(TimeSpan age)
{
    if (age.TotalDays < 1) return "today";
    if (age.TotalDays < 2) return "1d ago";
    if (age.TotalDays < 30) return $"{(int)age.TotalDays}d ago";
    if (age.TotalDays < 365) return $"{(int)(age.TotalDays / 30)}mo ago";
    return $"{(int)(age.TotalDays / 365)}y ago";
}
```

**Test Strategy:**

Integration test with git fixture: create multiple methods in same cluster, commit changes at different times, verify output shows recent activity. Test cluster with no git history (graceful skip). Test method not in any cluster (skip section). Verify formatting of age strings.

## Subtasks

### 36.1. Query cluster membership and retrieve method info for cluster members

**Status:** pending  
**Dependencies:** None  

After the existing git blame section in the context command, query the storage for clusters, find the cluster containing the target method, and retrieve method info (file path, start line, name) for up to 10 other members in the same cluster.

**Details:**

In AiCodeGraph.Cli/Program.cs context command, after the git blame section, add code that calls storage.GetClustersAsync(ct) to get all clusters, then uses FirstOrDefault to find the cluster containing the current methodId. If found and the cluster has more than 1 member, iterate over MethodIds (excluding the current method, limited to 10) and call storage.GetMethodInfoAsync for each to get FilePath, StartLine, and Name. Skip members where FilePath is null or the file doesn't exist on disk. Collect valid member info into a list for subsequent git log processing.

### 36.2. Execute git log for each cluster member to get last modification timestamp

**Status:** pending  
**Dependencies:** 36.1  

For each valid cluster member from subtask 1, spawn a git log process using ProcessStartInfo to retrieve the Unix timestamp of the most recent commit affecting that method's line range.

**Details:**

For each cluster member with a valid file path, create a ProcessStartInfo with FileName='git' and Arguments='log -1 --format=%ct -L {startLine},{startLine+1}:"{filePath}"'. Set RedirectStandardOutput=true, UseShellExecute=false, CreateNoWindow=true. Start the process, read stdout asynchronously using ReadToEndAsync with the cancellation token, then call WaitForExitAsync. Parse the trimmed output as a long Unix timestamp. If parsing succeeds, compute the age as DateTimeOffset.UtcNow minus DateTimeOffset.FromUnixTimeSeconds(ts). Collect successful results as (MethodName, TimeSpan Age) tuples. Wrap the entire per-member block in try-catch to gracefully skip any failures (git not installed, process errors, etc.).

### 36.3. Parse timestamps, sort by recency, and format age strings

**Status:** pending  
**Dependencies:** 36.2  

Sort collected recent changes by age ascending, take the top 3 most recently modified methods, format their ages using a FormatAge helper, and output the 'Recent cluster activity' line.

**Details:**

After collecting all (MethodName, Age) tuples, check if the list has any entries. If so, order by Age ascending (most recent first), take the top 3, and format each as '{MethodName} ({FormatAge(age)})'. Join with comma-space separator and write to console as 'Recent cluster activity: {formatted}'. Implement a static FormatAge(TimeSpan) helper method that returns: 'today' if TotalDays < 1, '1d ago' if < 2, '{days}d ago' if < 30, '{months}mo ago' if < 365, '{years}y ago' otherwise. Place FormatAge as a private static method accessible within Program.cs.

### 36.4. Handle edge cases and add integration tests with git fixture

**Status:** pending  
**Dependencies:** 36.1, 36.2, 36.3  

Ensure graceful handling of edge cases (no git installed, method not in any cluster, empty cluster, no git history for members) and create comprehensive integration tests using a git fixture with multiple methods in the same cluster.

**Details:**

Edge cases to handle: (1) Method not in any cluster - skip the entire section silently. (2) Cluster has only the target method - skip. (3) Git is not installed or not in a git repo - catch exceptions per-member and skip. (4) No members have recent history - skip output. (5) CancellationToken is respected in process calls. For integration tests, create a test class that sets up a temporary git repository with a test solution containing multiple methods assigned to the same cluster in a test SQLite database. Make commits at different known timestamps, run the context command, and verify the output includes 'Recent cluster activity' with correctly ordered and formatted entries. Also test the negative cases: method with no cluster, cluster with no git history.
