using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiCodeGraph.Cli.Commands;

public class SetupCursorCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db used by Cursor MCP integration",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("setup-cursor", "Scaffold Cursor MCP config, rules, and skill for AI Code Graph")
        {
            dbOption
        };

        command.SetAction((parseResult, _) =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var created = new List<string>();

            var cursorDir = Path.Combine(Directory.GetCurrentDirectory(), ".cursor");
            var rulesDir = Path.Combine(cursorDir, "rules");
            var skillsDir = Path.Combine(cursorDir, "skills", "ai-code-graph");

            Directory.CreateDirectory(cursorDir);
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(skillsDir);

            EnsureCursorMcpConfig(cursorDir, dbPath, created);
            CreateFileIfMissing(rulesDir, "ai-code-graph.mdc", GetRuleContent(), created);
            CreateFileIfMissing(skillsDir, "SKILL.md", GetSkillContent(), created);

            if (created.Count > 0)
            {
                Console.WriteLine("Cursor integration set up:");
                foreach (var path in created)
                {
                    Console.WriteLine($"  + {Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}");
                }

                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine("  1. In Cursor, enable the ai-code-graph MCP server if prompted");
                Console.WriteLine("  2. Run `ai-code-graph analyze YourSolution.sln` to build graph.db");
                Console.WriteLine("  3. Ask the agent to use AI Code Graph context before edits");
            }
            else
            {
                Console.WriteLine("All Cursor integration files already exist. Nothing to do.");
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static void EnsureCursorMcpConfig(string cursorDir, string dbPath, List<string> created)
    {
        var mcpPath = Path.Combine(cursorDir, "mcp.json");
        var serverNode = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = "ai-code-graph",
            ["args"] = new JsonArray("mcp", "--db", dbPath)
        };

        if (!File.Exists(mcpPath))
        {
            var root = new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["ai-code-graph"] = serverNode
                }
            };

            File.WriteAllText(mcpPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            created.Add(mcpPath);
            return;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(mcpPath)) as JsonObject;
            if (root == null)
            {
                Console.WriteLine("Warning: .cursor/mcp.json is not a JSON object. Skipping MCP update.");
                return;
            }

            var servers = root["mcpServers"] as JsonObject;
            if (servers == null)
            {
                servers = new JsonObject();
                root["mcpServers"] = servers;
            }

            var hasServer = servers.ContainsKey("ai-code-graph");
            var existingServer = hasServer ? servers["ai-code-graph"] : null;
            var shouldWrite = false;

            if (!hasServer)
            {
                servers["ai-code-graph"] = serverNode;
                shouldWrite = true;
            }
            else if (existingServer is not JsonObject existingServerObject)
            {
                servers["ai-code-graph"] = serverNode;
                shouldWrite = true;
            }
            else if (!JsonNode.DeepEquals(existingServerObject, serverNode))
            {
                servers["ai-code-graph"] = serverNode;
                shouldWrite = true;
            }

            if (shouldWrite)
            {
                File.WriteAllText(mcpPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                created.Add(mcpPath + " (updated)");
            }
        }
        catch (JsonException)
        {
            Console.WriteLine("Warning: failed to parse .cursor/mcp.json. Skipping MCP update.");
        }
    }

    private static void CreateFileIfMissing(string dir, string filename, string content, List<string> created)
    {
        var path = Path.Combine(dir, filename);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content);
            created.Add(path);
        }
    }

    private static string GetRuleContent() => @"---
description: AI Code Graph MCP workflow for C# analysis and edits
alwaysApply: true
---

# AI Code Graph Integration

If `cg_*` MCP tools are available, use them for code-understanding tasks.

Before editing any method, call `cg_get_context` first (when graph data exists) to review complexity, callers, callees, duplicates, and cluster context.

Primary tools:
- `cg_get_context`
- `cg_get_hotspots`
- `cg_get_callgraph`
- `cg_get_impact`
- `cg_query`
- `cg_dead_code`

Secondary tools:
- `cg_token_search`, `cg_semantic_search`, `cg_get_similar`
- `cg_get_duplicates`, `cg_get_clusters`, `cg_export_graph`
- `cg_churn`, `cg_coupling`, `cg_diff`, `cg_get_drift`, `cg_get_tree`, `cg_analyze`

If the database is missing, run `cg_analyze` first to build `./ai-code-graph/graph.db`.
";

    private static string GetSkillContent() => @"---
name: ai-code-graph
description: Guides AI Code Graph workflows for .NET analysis, refactoring, architecture checks, impact analysis, and duplicate detection. Use when users ask about complexity hotspots, blast radius, dead code, coupling, drift, or semantic code lookup.
---

# AI Code Graph Skill

## Quick Start

1. If graph data is missing, run `cg_analyze` on the solution.
2. Before editing a method, run `cg_get_context`.
3. Use method IDs from results for stable follow-up calls.

## Before Editing a Method

Use `cg_get_context` and interpret:
- **Complexity**: If CC > 10, avoid increasing branching/nesting.
- **Callers/Callees**: Estimate blast radius before signature or behavior changes.
- **Duplicates**: Apply the same fix to clone partners when appropriate.
- **Cluster**: Keep changes aligned with the method's intent group.

Then run:
- `cg_get_impact` for transitive caller impact
- `cg_get_callgraph` for direct relationships

## Refactoring Workflows

- **Complexity hotspot review**: `cg_get_hotspots` and prioritize highest CC methods.
- **Dead code cleanup**: `cg_dead_code`, remove or deprecate unreachable methods.
- **Coupling risk scan**: `cg_coupling`, focus on high-instability namespaces/types.
- **Churn risk scan**: `cg_churn`, prioritize high change-frequency x complexity areas.

## Search Workflows

- Use `cg_query` first for deterministic graph-based retrieval.
- Use `cg_token_search` for identifier/token overlap lookups.
- Use `cg_semantic_search` as fallback for natural-language intent matching.
- Use `cg_get_similar` to find candidates for consolidation.

## Architecture Review

- Use `cg_get_drift` to compare against baseline and detect regressions.
- Use `cg_get_clusters` and `cg_get_duplicates` to inspect intent fragmentation.
- Use `cg_export_graph` for structured downstream analysis.
- For layer and dependency policy checks, run CLI commands:
  - `ai-code-graph layers --db ./ai-code-graph/graph.db`
  - `ai-code-graph check-deps --db ./ai-code-graph/graph.db`

## Task-to-Tool Decision Guide

- **""I need context before changing code""** -> `cg_get_context`
- **""What breaks if I change this?""** -> `cg_get_impact` then `cg_get_callgraph`
- **""Where are risky areas?""** -> `cg_get_hotspots`, `cg_churn`, `cg_coupling`
- **""Can we delete this?""** -> `cg_dead_code` and caller checks
- **""Find code related to this idea""** -> `cg_query`, then `cg_token_search`/`cg_semantic_search`
- **""Are we drifting architecturally?""** -> `cg_get_drift`, `cg_get_clusters`, duplicates review
";
}
