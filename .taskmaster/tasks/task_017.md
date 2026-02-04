# Task ID: 17

**Title:** Create Claude Code Slash Commands

**Status:** done

**Dependencies:** 16 ✓

**Priority:** medium

**Description:** Create `.claude/commands/` directory with markdown files defining reusable slash commands for context retrieval, hotspot viewing, duplicate detection, and drift analysis. Each command instructs Claude Code how to invoke the CLI and interpret results.

**Details:**

Create the following markdown files in `.claude/commands/`:

1. `.claude/commands/context.md`:
```markdown
Get full architectural context for a method before editing.

Usage: /context <method-name-or-pattern>

Steps:
1. Run `ai-code-graph context "$ARGUMENTS" --db ./ai-code-graph/graph.db`
2. If the method is found, review the output:
   - **Complexity**: If CC > 10, flag as high-complexity - consider refactoring
   - **Callers**: These methods depend on the target - changes may break them
   - **Callees**: These are dependencies - verify they still satisfy requirements after edits
   - **Cluster**: Shows related methods with similar intent - check for consistency
   - **Duplicates**: If duplicates exist, consider whether changes should apply to clones too
3. Use this context to inform your edit strategy before modifying the method
4. If method not found, try a broader pattern or check the suggestions provided
```

2. `.claude/commands/hotspots.md`:
```markdown
Show the top complexity hotspots in the codebase.

Usage: /hotspots [count]

Steps:
1. Run `ai-code-graph hotspots --top ${ARGUMENTS:-10} --format table --db ./ai-code-graph/graph.db`
2. Present the results highlighting:
   - Methods with CC > 15 as critical complexity
   - Methods with CC > 10 as high complexity
   - Methods with Nesting > 4 as deeply nested
3. Suggest which methods would benefit most from refactoring
4. For the top 3 hotspots, briefly explain what makes them complex
```

3. `.claude/commands/duplicates.md`:
```markdown
Show detected code clones in the codebase.

Usage: /duplicates [threshold]

Steps:
1. Run `ai-code-graph duplicates --threshold ${ARGUMENTS:-0.7} --format table --db ./ai-code-graph/graph.db`
2. Group duplicates by clone type:
   - Type1/Type2 (structural): Near-identical code that should likely be extracted
   - Semantic: Methods with similar intent that might benefit from a shared abstraction
3. For high-score pairs (> 0.9), recommend extraction into a shared method
4. For medium-score pairs (0.7-0.9), suggest reviewing for potential consolidation
```

4. `.claude/commands/drift.md`:
```markdown
Run drift detection against the baseline to identify architectural changes.

Usage: /drift [baseline-path]

Steps:
1. Run `ai-code-graph drift --vs ${ARGUMENTS:-./ai-code-graph/baseline.db} --format table --db ./ai-code-graph/graph.db`
2. Analyze the drift report:
   - **New methods**: Review if they follow existing patterns and conventions
   - **Removed methods**: Check if callers have been updated
   - **Complexity regressions**: Flag methods that got significantly more complex
   - **New duplicates**: Identify if new code duplicates existing functionality
   - **Intent scattering**: Highlight cluster members that moved to unexpected namespaces
3. Summarize the overall architectural health trend
4. Recommend actions for any concerning drift patterns
```

**Test Strategy:**

1. Verify each markdown file is valid markdown and well-formatted
2. Manually test each slash command in a Claude Code session:
   - `/context UserService.CreateUser` should invoke the context command
   - `/hotspots 5` should show top 5 hotspots
   - `/duplicates 0.8` should show clones above 0.8 threshold
   - `/drift` should run drift detection with default baseline path
3. Verify the $ARGUMENTS substitution works correctly in each command
4. Confirm commands produce actionable guidance, not just raw output
