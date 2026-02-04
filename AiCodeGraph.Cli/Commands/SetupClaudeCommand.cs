using System.CommandLine;
using System.CommandLine.Parsing;

namespace AiCodeGraph.Cli.Commands;

public class SetupClaudeCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db used by commands",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("setup-claude", "Scaffold Claude Code slash commands, CLAUDE.md snippet, and MCP config into the current project")
        {
            dbOption
        };

        command.SetAction((parseResult, _) =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var created = new List<string>();

            // 1. Create .claude/commands/cg/ directory
            var commandsDir = Path.Combine(Directory.GetCurrentDirectory(), ".claude", "commands", "cg");
            Directory.CreateDirectory(commandsDir);

            // 2. Write slash command files
            CreateCommandFile(commandsDir, "context.md", GetContextCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "hotspots.md", GetHotspotsCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "duplicates.md", GetDuplicatesCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "drift.md", GetDriftCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "callgraph.md", GetCallgraphCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "tree.md", GetTreeCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "similar.md", GetSimilarCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "clusters.md", GetClustersCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "token-search.md", GetTokenSearchCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "export.md", GetExportCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "analyze.md", GetAnalyzeCommandContent(), created);
            CreateCommandFile(commandsDir, "churn.md", GetChurnCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "impact.md", GetImpactCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "dead-code.md", GetDeadCodeCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "coupling.md", GetCouplingCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "diff.md", GetDiffCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "semantic-search.md", GetSemanticSearchCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "query.md", GetQueryCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "status.md", GetStatusCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "layers.md", GetLayersCommandContent(dbPath), created);
            CreateCommandFile(commandsDir, "check-deps.md", GetCheckDepsCommandContent(dbPath), created);

            // 3. Create .mcp.json for MCP server integration
            var mcpJson = Path.Combine(Directory.GetCurrentDirectory(), ".mcp.json");
            if (!File.Exists(mcpJson))
            {
                File.WriteAllText(mcpJson, $$"""
{
  "mcpServers": {
    "ai-code-graph": {
      "type": "stdio",
      "command": "ai-code-graph",
      "args": ["mcp", "--db", "{{dbPath}}"]
    }
  }
}
""");
                created.Add(mcpJson);
            }

            // 4. Append auto-context section to CLAUDE.md
            var claudeMd = Path.Combine(Directory.GetCurrentDirectory(), "CLAUDE.md");
            var snippet = GetClaudeMdSnippet(dbPath);

            if (File.Exists(claudeMd))
            {
                var existing = File.ReadAllText(claudeMd);
                if (!existing.Contains("Auto-Context: Code Graph Integration"))
                {
                    File.AppendAllText(claudeMd, snippet);
                    created.Add(claudeMd + " (appended)");
                }
            }
            else
            {
                File.WriteAllText(claudeMd, $"# Claude Code Instructions\n{snippet}");
                created.Add(claudeMd);
            }

            // Summary
            if (created.Count > 0)
            {
                Console.WriteLine("Claude Code integration set up:");
                foreach (var path in created)
                    Console.WriteLine($"  + {Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}");
                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine($"  1. Run: ai-code-graph analyze YourSolution.sln");
                Console.WriteLine($"  2. Use /cg:context, /cg:hotspots, /cg:token-search etc. in Claude Code");
                Console.WriteLine($"  3. MCP tools (cg_*) are available to any MCP-compatible IDE");
            }
            else
            {
                Console.WriteLine("All Claude Code integration files already exist. Nothing to do.");
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static void CreateCommandFile(string dir, string filename, string content, List<string> created)
    {
        var path = Path.Combine(dir, filename);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content);
            created.Add(path);
        }
    }

    private static string GetContextCommandContent(string dbPath) => $@"Get method context before editing: $ARGUMENTS

Steps:
1. Run `ai-code-graph context ""$ARGUMENTS"" --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Review the output: complexity, callers, callees, cluster, and duplicates
4. If complexity (CC) is high (>10), warn about the method's complexity before making changes
5. If the method has callers, note that changes may affect those callers
6. If duplicates exist, suggest whether the change should also apply to the duplicate methods
7. Proceed with the user's requested edit, keeping the context in mind
";

    private static string GetHotspotsCommandContent(string dbPath) => $@"Show complexity hotspots in the codebase.

Steps:
1. Run `ai-code-graph hotspots --top 15 --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results, highlighting methods with CC > 15 as candidates for refactoring
4. For the top 3 hotspots, briefly suggest what makes them complex (deep nesting, many branches, etc.)
";

    private static string GetDuplicatesCommandContent(string dbPath) => $@"Show detected code duplicates in the codebase.

Steps:
1. Run `ai-code-graph duplicates --top 15 --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Group the results by clone type (Type1 = exact, Type2 = renamed, Semantic = similar logic)
4. For Type1 clones, suggest extracting a shared utility method
5. For Semantic clones, suggest whether they represent a pattern worth abstracting
";

    private static string GetDriftCommandContent(string dbPath)
    {
        var baselineDir = Path.GetDirectoryName(dbPath) ?? ".";
        return $@"Run drift detection against the baseline.

Steps:
1. Check if `{Path.Combine(baselineDir, "baseline.db")}` exists. If not, inform the user to run `ai-code-graph analyze --save-baseline` first
2. Run `ai-code-graph drift --vs {Path.Combine(baselineDir, "baseline.db")} --format detail --db {dbPath}`
3. Summarize findings:
   - New methods added
   - Methods removed
   - Complexity regressions (methods that got more complex)
   - New duplicates introduced
   - Intent scattering (logic spreading across namespaces)
4. For complexity regressions, show the before/after values and suggest refactoring if the increase is significant
";
    }

    private static string GetCallgraphCommandContent(string dbPath) => $@"Explore method call graph: $ARGUMENTS

Steps:
1. Run `ai-code-graph callgraph --method ""$ARGUMENTS"" --depth 2 --direction both --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the call tree showing callers and callees
4. Highlight any deep call chains or circular dependencies
5. If modifying this method, note which callers might be affected
";

    private static string GetTreeCommandContent(string dbPath) => $@"Display code structure tree.

Steps:
1. Run `ai-code-graph tree --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the hierarchical structure: Projects > Namespaces > Types > Methods
4. Use the structure to understand codebase organization
";

    private static string GetSimilarCommandContent(string dbPath) => $@"Find methods similar to: $ARGUMENTS

Steps:
1. Run `ai-code-graph similar ""$ARGUMENTS"" --top 10 --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present ranked list of similar methods with similarity scores
4. For high-similarity matches (>0.8), suggest consolidation
";

    private static string GetClustersCommandContent(string dbPath) => $@"Show intent clusters in the codebase.

Steps:
1. Run `ai-code-graph clusters --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present each cluster with its label, cohesion score, and member methods
4. Highlight clusters with low cohesion (<0.5) as refactoring candidates
";

    private static string GetTokenSearchCommandContent(string dbPath) => $@"Search code by token overlap: $ARGUMENTS

Steps:
1. Run `ai-code-graph token-search ""$ARGUMENTS"" --top 10 --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present results ranked by similarity score
4. Suggest which methods are most relevant to the query
";

    private static string GetExportCommandContent(string dbPath) => $@"Export code graph data.

Steps:
1. Run `ai-code-graph export --format json --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present a summary of the exported data
";

    private static string GetAnalyzeCommandContent() => @"Analyze solution and build code graph.

Steps:
1. Look for a .sln file in the current directory or use the path: $ARGUMENTS
2. Run `ai-code-graph analyze ""$ARGUMENTS"" --save-baseline`
3. Report the summary stats after analysis completes
4. Inform the user that all slash commands are now available
";

    private static string GetChurnCommandContent(string dbPath) => $@"Show methods with high change-frequency x complexity (churn hotspots): $ARGUMENTS

Steps:
1. Run `ai-code-graph churn --since ""$ARGUMENTS"" --db {dbPath}` (use ""6 months ago"" if no argument provided)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results ranked by churn score (changes x complexity)
4. For the top results, explain why they are risky: high change frequency combined with high complexity
5. Suggest which methods would benefit most from refactoring to reduce complexity
";

    private static string GetImpactCommandContent(string dbPath) => $@"Show transitive impact of changing a method: $ARGUMENTS

Steps:
1. Run `ai-code-graph impact ""$ARGUMENTS"" --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the full chain of methods that would be affected by changes to this method
4. Highlight entry points (methods with no further callers) as they represent top-level impact boundaries
5. Use `--depth N` to limit traversal if the impact tree is too large
";

    private static string GetDeadCodeCommandContent(string dbPath) => $@"Find methods with no callers (potential dead code).

Steps:
1. Run `ai-code-graph dead-code --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the list of potentially unreachable methods, sorted by complexity
4. Highlight high-complexity dead code as priority candidates for removal
5. Note that test methods, constructors, and override methods are excluded by default
6. Use `--include-overrides` to also show override/abstract methods
";

    private static string GetCouplingCommandContent(string dbPath) => $@"Show afferent/efferent coupling and instability metrics: $ARGUMENTS

Steps:
1. Run `ai-code-graph coupling --level namespace --top 20 --db {dbPath}` (use ""type"" level if $ARGUMENTS contains ""type"")
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results showing Ca (afferent), Ce (efferent), I (instability), A (abstractness), D (distance from main sequence)
4. Highlight components with high instability (I > 0.8) as fragile - lots of outgoing dependencies
5. Highlight components with high distance (D > 0.5) as violating the main sequence principle
6. Suggest which namespaces/types might benefit from refactoring to reduce coupling
";

    private static string GetDiffCommandContent(string dbPath) => $@"Show methods affected by changes between git refs: $ARGUMENTS

Steps:
1. Run `ai-code-graph diff --from HEAD~1 --to HEAD --format detail --db {dbPath}` (adjust refs if $ARGUMENTS specifies them)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results showing changed files and affected methods with their complexity
4. Highlight high-complexity methods (CC > 10) that were touched - these are risky changes
5. Suggest reviewing methods with high complexity that appear in the diff
";

    private static string GetSemanticSearchCommandContent(string dbPath) => $@"Search code by semantic meaning: $ARGUMENTS

Note: For most use cases, use `/cg:query` instead for graph-based retrieval (faster, deterministic).
Use semantic-search as a fallback when you need natural language matching or when query returns no results.

Steps:
1. Run `ai-code-graph semantic-search ""$ARGUMENTS"" --top 10 --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. If a warning about hash-based embeddings appears, inform the user they can re-analyze with `--embedding-engine openai` for better results
4. Present the results ranked by similarity score
5. For the top results, briefly describe what the method does based on its name and location
6. Suggest which method(s) are most relevant to the user's query
";

    private static string GetQueryCommandContent(string dbPath) => $@"Graph-based method retrieval: $ARGUMENTS

Steps:
1. Parse $ARGUMENTS for quick options or JSON query:
   - `--callers MethodName` -> find all callers of a method
   - `--callees MethodName` -> find all callees of a method
   - `--impact MethodName` -> transitive impact analysis
   - `--cluster ClusterLabel` -> methods in a cluster
   - JSON query for advanced use
2. Run `ai-code-graph query $ARGUMENTS --db {dbPath}`
3. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
4. Present the results with method IDs for stable references
5. Use `--format json` for structured output if needed
";

    private static string GetStatusCommandContent(string dbPath) => $@"Show database status and staleness detection.

Steps:
1. Run `ai-code-graph status --db {dbPath}`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the status information:
   - Database path and size
   - Last analysis timestamp
   - Method/type/namespace counts
   - Staleness indicator (files changed since last analysis)
4. If database is stale, suggest re-running `ai-code-graph analyze`
";

    private static string GetLayersCommandContent(string dbPath) => $@"Show architectural layer assignments: $ARGUMENTS

Steps:
1. Run `ai-code-graph layers --db {dbPath}` (filter by $ARGUMENTS if provided)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the layer assignments showing which namespaces/types belong to which architectural layers:
   - Presentation (Controllers, Views, Pages)
   - Application (Services, Handlers, UseCases)
   - Domain (Entities, ValueObjects, Aggregates)
   - Infrastructure (Repositories, DbContexts, External)
4. Highlight any layer violations (e.g., Domain depending on Infrastructure)
";

    private static string GetCheckDepsCommandContent(string dbPath) => $@"Check for forbidden dependencies: $ARGUMENTS

Steps:
1. Run `ai-code-graph check-deps --db {dbPath}` (use $ARGUMENTS for custom rules if provided)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present any violations of dependency rules:
   - Layer violations (e.g., Domain -> Infrastructure)
   - Circular dependencies
   - Forbidden namespace dependencies
4. For each violation, show the dependency chain and suggest how to fix it
5. If no violations found, confirm the architecture is clean
";

    private static string GetClaudeMdSnippet(string dbPath) => $@"
## Auto-Context: Code Graph Integration

When modifying methods in this codebase, run the context command first if `{dbPath}` exists:

```bash
ai-code-graph context ""MethodName"" --db {dbPath}
```

This returns complexity, callers, callees, cluster membership, and duplicates in one call. Use this information to:
- Avoid increasing complexity of already-complex methods (CC > 10)
- Update callers if you change a method's signature or behavior
- Apply the same fix to duplicates when fixing bugs
- Understand which intent cluster a method belongs to before refactoring

Available slash commands (primary):
- `/cg:analyze [solution]` - Analyze solution and build the graph
- `/cg:context <method>` - Full method context before editing (recommended first step)
- `/cg:query <pattern>` - Graph-based method retrieval (recommended for code lookup)
- `/cg:hotspots` - Top complexity hotspots
- `/cg:callgraph <method>` - Explore call relationships
- `/cg:impact <method>` - Transitive impact analysis

Available slash commands (secondary):
- `/cg:similar <method>` - Find methods with similar intent
- `/cg:token-search <query>` - Fallback: token-based search
- `/cg:semantic-search <query>` - Fallback: semantic search
- `/cg:duplicates` - Detected code clones
- `/cg:clusters` - Intent clusters
- `/cg:tree` - Code structure tree
- `/cg:export` - Export graph data
- `/cg:drift` - Architectural drift from baseline
- `/cg:churn` - Change-frequency x complexity hotspots
- `/cg:dead-code` - Find methods with no callers
- `/cg:coupling <method>` - Coupling metrics
- `/cg:diff <refs>` - Methods affected by git changes
- `/cg:layers` - Architectural layer assignments
- `/cg:check-deps` - Forbidden dependency detection
- `/cg:status` - Database status and staleness

To rebuild the graph after significant changes: `ai-code-graph analyze YourSolution.sln`
";
}
