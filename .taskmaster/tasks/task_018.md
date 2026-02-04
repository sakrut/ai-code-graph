# Task ID: 18

**Title:** Update CLAUDE.md with Auto-Context Instructions

**Status:** done

**Dependencies:** 16 ✓, 17 ✓

**Priority:** medium

**Description:** Update the project's CLAUDE.md to instruct Claude Code to automatically run `ai-code-graph context <method>` before modifying any method with complexity > 5 or that has callers, providing architectural awareness without manual intervention.

**Details:**

Append a new section to the existing `/home/claude/projects/ai-code-graph/CLAUDE.md` file:

```markdown
## Auto-Context Rules

Before modifying any method in this codebase, follow these rules:

1. **Pre-edit context check**: Before editing a method, run:
   ```bash
   ai-code-graph context "<TypeName.MethodName>" --db ./ai-code-graph/graph.db
   ```

2. **When to check** (any of these conditions):
   - The method has cognitive complexity > 5
   - The method has callers (other methods depend on it)
   - You're changing the method's signature or return type
   - You're modifying control flow logic

3. **How to use the context**:
   - If the method has **callers**, verify your changes won't break them
   - If the method has **high complexity** (CC > 10), consider refactoring instead of adding more complexity
   - If **duplicates** exist, apply the same fix to clones if applicable
   - If the method is in a **cluster**, ensure changes maintain consistency with related methods

4. **Skip context check** when:
   - Adding a brand new method (no existing context)
   - Making trivial changes (comments, whitespace, renaming local variables)
   - The graph database doesn't exist yet (run `ai-code-graph analyze` first)

## Available Slash Commands

- `/context <method>` - Get full architectural context before editing
- `/hotspots [N]` - Show top N complexity hotspots
- `/duplicates [threshold]` - Show code clones above threshold
- `/drift [baseline]` - Run drift detection against baseline
```

Key considerations:
- Place this section after the existing "Conventions" section
- Keep instructions concise - every token counts in Claude's context
- Reference the slash commands so Claude Code knows they're available
- Don't duplicate existing CLAUDE.md content

**Test Strategy:**

1. Verify the updated CLAUDE.md is valid markdown
2. Verify it doesn't duplicate existing content
3. Test that Claude Code picks up the auto-context instructions by:
   - Starting a new Claude Code session in the project
   - Asking to modify a method - verify Claude attempts to run the context command
4. Verify the skip conditions work - trivial edits should not trigger context lookup
5. Ensure the CLAUDE.md stays under reasonable size (context budget)
