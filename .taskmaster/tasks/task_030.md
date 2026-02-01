# Task ID: 30

**Title:** Rename search Command to token-search

**Status:** done

**Dependencies:** 20 ✓, 21 ✓, 22 ✓, 23 ✓, 24 ✓, 25 ✓, 26 ✓, 27 ✓, 28 ✓, 29 ✓

**Priority:** high

**Description:** Rename the CLI 'search' command to 'token-search' across CLI, MCP server, and Claude Code slash commands to accurately reflect hash-based matching.

**Details:**

This is a breaking change. Update all three integration points:

1. **CLI command** (AiCodeGraph.Cli/Program.cs lines 716-808):
   - Change `new Command("search", ...)` to `new Command("token-search", ...)`
   - Update description from 'Search code by natural language intent' to 'Search code by token overlap'
   - Update variable names (searchCommand → tokenSearchCommand, etc.)

2. **MCP server** (AiCodeGraph.Cli/Mcp/McpServer.cs):
   - Rename tool from `cg_search` to `cg_token_search`
   - Update tool description to match
   - Update the tools/list response
   - Update the tools/call handler

3. **Claude Code slash command**:
   - Rename `.claude/commands/cg:search.md` to `.claude/commands/cg:token-search.md`
   - Update content to reference `token-search` command

4. **CLAUDE.md**: Update the slash commands section

5. **Tests**: Update SearchCommandTests.cs to reference new command name

**Test Strategy:**

Update existing SearchCommandTests to use 'token-search' name. Verify old 'search' command name is no longer recognized. Run full test suite. Verify MCP tools/list returns cg_token_search. Verify slash command file exists at new path.

## Subtasks

### 30.1. Rename CLI search command to token-search in Program.cs

**Status:** pending  
**Dependencies:** None  

Rename the 'search' command to 'token-search' in AiCodeGraph.Cli/Program.cs (lines 716-808), update the command description from 'Search code by natural language intent' to 'Search code by token overlap', and rename all related variable names (searchCommand → tokenSearchCommand, searchQueryOption → tokenSearchQueryOption, etc.).

**Details:**

In AiCodeGraph.Cli/Program.cs:
1. Change `new Command("search", "Search code by natural language intent")` to `new Command("token-search", "Search code by token overlap")`
2. Rename variable `searchCommand` to `tokenSearchCommand` throughout the block
3. Rename option variables (e.g., searchQueryOption, searchTopOption, searchThresholdOption, searchFormatOption, searchDbOption) to use tokenSearch prefix
4. Update the command registration where it's added to the root command
5. Verify the command handler logic remains unchanged (only names change, not behavior)

### 30.2. Rename MCP tool from cg_search to cg_token_search in McpServer.cs

**Status:** pending  
**Dependencies:** 30.1  

Update the MCP server tool definition in AiCodeGraph.Cli/Mcp/McpServer.cs to rename the tool from 'cg_search' to 'cg_token_search', update its description to match the CLI change, and update both the tools/list response and tools/call handler.

**Details:**

In AiCodeGraph.Cli/Mcp/McpServer.cs:
1. Find the tool definition for 'cg_search' in the tools/list handler and rename to 'cg_token_search'
2. Update the tool description from any 'natural language' or 'intent' wording to 'Search code by token overlap'
3. Update the tools/call handler switch/if block that matches on 'cg_search' to match on 'cg_token_search'
4. Ensure the handler still invokes the same underlying search logic (now via token-search command path)
5. Verify no other references to the old tool name remain in the file

### 30.3. Rename slash command file from cg:search.md to cg:token-search.md

**Status:** pending  
**Dependencies:** 30.1  

Rename the Claude Code slash command file from .claude/commands/cg:search.md to .claude/commands/cg:token-search.md and update its internal content to reference the 'token-search' command name instead of 'search'.

**Details:**

File operations:
1. Rename `.claude/commands/cg:search.md` to `.claude/commands/cg:token-search.md`
2. Inside the renamed file, update any references to the 'search' CLI command to 'token-search'
3. Update the command description text to say 'token overlap' or 'token-based search' instead of 'natural language intent'
4. Ensure the slash command invocation examples use `ai-code-graph token-search` instead of `ai-code-graph search`
5. Verify no broken references to the old filename exist in other config files

### 30.4. Update CLAUDE.md references and SearchCommandTests.cs

**Status:** pending  
**Dependencies:** 30.1, 30.2, 30.3  

Update CLAUDE.md to reference the renamed slash command (/cg:token-search instead of /cg:search) and update SearchCommandTests.cs to use the new 'token-search' command name throughout.

**Details:**

1. In CLAUDE.md:
   - Find the slash commands section listing `/cg:search` and rename to `/cg:token-search`
   - Update the description from 'Natural language code search' to 'Search code by token overlap' or similar
   - Check for any other references to the search command or cg_search tool name

2. In AiCodeGraph.Tests/SearchCommandTests.cs:
   - Update command name strings from 'search' to 'token-search' in all test methods
   - Update any variable names referencing the old command name
   - Ensure test assertions check for 'token-search' in output where applicable
   - Run the full test suite to verify all tests pass with the new name
