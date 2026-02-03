# Snapshot Testing

CLI output snapshot tests ensure output formats don't accidentally change or regress.

## Location

- Test file: `AiCodeGraph.Tests/SnapshotTests.cs`
- Golden files: `AiCodeGraph.Tests/Snapshots/*.txt`

## Running Snapshot Tests

```bash
# Run all snapshot tests
dotnet test --filter "FullyQualifiedName~SnapshotTests"
```

## Updating Snapshots

When you intentionally change CLI output format:

```bash
# Regenerate all golden files
UPDATE_SNAPSHOTS=1 dotnet test --filter "FullyQualifiedName~SnapshotTests"

# Review changes
git diff AiCodeGraph.Tests/Snapshots/
```

## Adding New Snapshots

1. Add a test method in `SnapshotTests.cs`:
```csharp
[Fact]
public async Task NewCommand_Compact_MatchesSnapshot()
{
    var dbPath = await CreateSnapshotDbAsync();
    var (exitCode, output, _) = await RunCliAsync($"new-command --db {dbPath}");
    Assert.Equal(0, exitCode);
    await AssertMatchesSnapshotAsync("newcommand_compact", output);
}
```

2. Run with UPDATE_SNAPSHOTS=1 to create the golden file
3. Review and commit the new `.txt` file

## CI Behavior

Snapshot tests fail if output differs from golden files. This prevents accidental output changes from being merged.
