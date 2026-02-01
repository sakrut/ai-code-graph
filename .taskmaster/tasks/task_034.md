# Task ID: 34

**Title:** Add Git Blame Info to Context Command

**Status:** done

**Dependencies:** 33 ✓

**Priority:** medium

**Description:** Show who last modified the method and when by running git blame on the method's source lines and parsing the output.

**Details:**

File: AiCodeGraph.Cli/Program.cs context command

After the source snippet section, add git blame:

```csharp
// Git blame section (after source snippet)
if (info.FilePath != null && File.Exists(info.FilePath))
{
    try
    {
        var startLine = info.StartLine;
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"blame -L {startLine},+20 --porcelain \"{info.FilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process != null)
        {
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                // Parse porcelain format for author and author-time
                var authorMatch = Regex.Match(output, @"^author (.+)$", RegexOptions.Multiline);
                var timeMatch = Regex.Match(output, @"^author-time (\d+)$", RegexOptions.Multiline);
                
                if (authorMatch.Success && timeMatch.Success)
                {
                    var author = authorMatch.Groups[1].Value;
                    var timestamp = long.Parse(timeMatch.Groups[1].Value);
                    var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
                    Console.WriteLine($"\nLast modified: {author} on {date:yyyy-MM-dd}");
                }
            }
        }
    }
    catch (Exception) when (ex is Win32Exception or IOException)
    {
        // Git not available or not a git repo - skip silently
    }
}
```

Edge cases: Not a git repo (skip), git not installed (skip), file not tracked (skip).

**Test Strategy:**

Integration test with git fixture: create a file, commit it, verify blame output shows committer and date. Test non-git directory (graceful skip). Test file not tracked by git. Mock Process.Start for unit testing if needed.

## Subtasks

### 34.1. Add Process.Start git blame execution with porcelain format after source snippet section

**Status:** pending  
**Dependencies:** None  

Add the git blame process execution logic to the context command in Program.cs, invoking git blame with --porcelain flag on the method's source lines (startLine, +20 lines) after the source snippet section.

**Details:**

In AiCodeGraph.Cli/Program.cs, after the source snippet section in the context command handler, add ProcessStartInfo configuration with FileName='git', Arguments using blame -L {startLine},+20 --porcelain format, RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false, CreateNoWindow=true. Start the process, read StandardOutput asynchronously, and await WaitForExitAsync with the cancellation token. Only proceed with parsing if ExitCode == 0 and output is non-empty.

### 34.2. Parse author and author-time from porcelain output using regex

**Status:** pending  
**Dependencies:** 34.1  

Extract the author name and author-time (unix timestamp) from the git blame porcelain output using regex, then format and display the last-modified information.

**Details:**

After reading the git blame porcelain output, use Regex.Match with pattern @"^author (.+)$" (RegexOptions.Multiline) to extract the author name, and @"^author-time (\d+)$" (RegexOptions.Multiline) to extract the unix timestamp. If both matches succeed, parse the timestamp with long.Parse, convert to local DateTime using DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime, and write to console: $"\nLast modified: {author} on {date:yyyy-MM-dd}". Add 'using System.Text.RegularExpressions' and 'using System.Diagnostics' if not already present.

### 34.3. Handle edge cases and add integration tests with git fixture

**Status:** pending  
**Dependencies:** 34.1, 34.2  

Add proper error handling for git not installed, directory not a git repo, and file not tracked scenarios. Add integration tests covering all edge cases.

**Details:**

Wrap the git blame execution in a try-catch that catches Win32Exception (git not installed) and IOException (process errors), silently skipping the blame section. The ExitCode != 0 check already handles non-repo and untracked file cases (git blame returns non-zero). Add integration tests: (1) test in a valid git repo with a committed file verifying author/date output appears, (2) test with a file not tracked by git (verify graceful skip with no blame output), (3) test in a non-git directory (verify no exception and no blame output), (4) test with git available but file path that doesn't exist (already handled by File.Exists check before blame section).
