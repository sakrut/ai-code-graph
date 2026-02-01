# Task ID: 70

**Title:** MCP: ensure compact responses + sensible defaults for agent usage

**Status:** pending

**Dependencies:** None

**Priority:** high

**Description:** MCP should return high-signal, bounded outputs and stable ids.

**Details:**

1) Audit existing MCP tool handlers.
2) Ensure each tool supports compact mode and bounded list defaults.
3) Include MethodId in MCP responses where relevant.
4) Add an integration test for MCP tool calls returning compact payloads.

**Test Strategy:**

Run MCP server in test mode and call a few tools; verify output size bounds and stability.
