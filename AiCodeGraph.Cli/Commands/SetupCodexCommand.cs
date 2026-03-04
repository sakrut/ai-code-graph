using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiCodeGraph.Cli.Commands;

public class SetupCodexCommand : ICommandHandler
{
    public Command BuildCommand()
    {
        var dbOption = new Option<string>("--db")
        {
            Description = "Path to graph.db used by Codex MCP integration",
            DefaultValueFactory = _ => "./ai-code-graph/graph.db"
        };

        var command = new Command("setup-codex", "Scaffold Codex MCP config, AGENTS.md guidance, and skill metadata for AI Code Graph")
        {
            dbOption
        };

        command.SetAction((parseResult, _) =>
        {
            var dbPath = parseResult.GetValue(dbOption) ?? "./ai-code-graph/graph.db";
            var created = new List<string>();
            var currentDir = Directory.GetCurrentDirectory();

            var codexDir = Path.Combine(currentDir, ".codex");
            var skillDir = Path.Combine(codexDir, "skills", "ai-code-graph");
            var agentsDir = Path.Combine(skillDir, "agents");

            Directory.CreateDirectory(codexDir);
            Directory.CreateDirectory(skillDir);
            Directory.CreateDirectory(agentsDir);

            CreateFileIfMissing(skillDir, "SKILL.md", AgentIntegrationContent.GetSharedSkillContent(), created);
            CreateFileIfMissing(agentsDir, "openai.yaml", AgentIntegrationContent.GetCodexOpenAiYamlContent(), created);

            EnsureMcpConfig(currentDir, dbPath, created);
            EnsureAgentsMd(currentDir, dbPath, created);

            if (created.Count > 0)
            {
                Console.WriteLine("Codex integration set up:");
                foreach (var path in created)
                {
                    Console.WriteLine($"  + {Path.GetRelativePath(currentDir, path)}");
                }

                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine("  1. Run `ai-code-graph analyze YourSolution.sln` to build graph.db");
                Console.WriteLine("  2. Ensure your Codex client loads `.mcp.json` for MCP server discovery");
                Console.WriteLine("  3. Ask the agent to use AI Code Graph context before edits");
            }
            else
            {
                Console.WriteLine("All Codex integration files already exist. Nothing to do.");
            }

            return Task.CompletedTask;
        });

        return command;
    }

    private static void EnsureMcpConfig(string currentDir, string dbPath, List<string> created)
    {
        var mcpPath = Path.Combine(currentDir, ".mcp.json");
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
                Console.WriteLine("Warning: .mcp.json is not a JSON object. Skipping MCP update.");
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
            else if (MergeMcpServer(existingServerObject, dbPath))
            {
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
            Console.WriteLine("Warning: failed to parse .mcp.json. Skipping MCP update.");
        }
    }

    private static void EnsureAgentsMd(string currentDir, string dbPath, List<string> created)
    {
        var agentsPath = Path.Combine(currentDir, "AGENTS.md");
        var snippet = AgentIntegrationContent.GetAgentsMdSnippet(dbPath);
        const string sectionHeader = "## Auto-Context: Code Graph Integration";

        if (File.Exists(agentsPath))
        {
            var existing = File.ReadAllText(agentsPath);
            var sectionIndex = existing.IndexOf(sectionHeader, StringComparison.Ordinal);
            if (sectionIndex < 0)
            {
                File.AppendAllText(agentsPath, snippet);
                created.Add(agentsPath + " (appended)");
            }
            else
            {
                var replacement = snippet.TrimStart('\r', '\n');
                var nextHeaderIndex = existing.IndexOf("\n## ", sectionIndex + sectionHeader.Length, StringComparison.Ordinal);
                var sectionEnd = nextHeaderIndex >= 0 ? nextHeaderIndex + 1 : existing.Length;

                var currentSection = existing[sectionIndex..sectionEnd];
                if (!string.Equals(currentSection.Trim(), replacement.Trim(), StringComparison.Ordinal))
                {
                    var updated = existing[..sectionIndex] + replacement + existing[sectionEnd..];
                    File.WriteAllText(agentsPath, updated);
                    created.Add(agentsPath + " (updated)");
                }
            }

            return;
        }

        File.WriteAllText(agentsPath, $"# Agent Instructions\n{snippet}");
        created.Add(agentsPath);
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

    private static bool MergeMcpServer(JsonObject existingServerObject, string dbPath)
    {
        var changed = false;

        if (existingServerObject["type"] == null)
        {
            existingServerObject["type"] = "stdio";
            changed = true;
        }

        if (existingServerObject["command"] == null)
        {
            existingServerObject["command"] = "ai-code-graph";
            changed = true;
        }

        if (existingServerObject["args"] is not JsonArray args)
        {
            existingServerObject["args"] = new JsonArray("mcp", "--db", dbPath);
            return true;
        }

        var commandIsAiCodeGraph = NodeEqualsString(existingServerObject["command"], "ai-code-graph");
        var hasMcp = false;
        for (var i = 0; i < args.Count; i++)
        {
            if (NodeEqualsString(args[i], "mcp"))
            {
                hasMcp = true;
                break;
            }
        }

        if (commandIsAiCodeGraph && !hasMcp)
        {
            args.Insert(0, "mcp");
            changed = true;
        }

        var dbFlagIndex = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (NodeEqualsString(args[i], "--db"))
            {
                dbFlagIndex = i;
                break;
            }
        }

        if (dbFlagIndex >= 0)
        {
            if (dbFlagIndex + 1 >= args.Count)
            {
                args.Add(dbPath);
                changed = true;
            }
            else if (!NodeEqualsString(args[dbFlagIndex + 1], dbPath))
            {
                args[dbFlagIndex + 1] = dbPath;
                changed = true;
            }
        }
        else
        {
            args.Add("--db");
            args.Add(dbPath);
            changed = true;
        }

        return changed;
    }

    private static bool NodeEqualsString(JsonNode? node, string expected)
    {
        return node is JsonValue valueNode &&
               valueNode.TryGetValue<string>(out var value) &&
               string.Equals(value, expected, StringComparison.Ordinal);
    }
}
