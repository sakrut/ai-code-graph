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

    private static string GetRuleContent() => AgentIntegrationContent.GetCursorRuleContent();

    private static string GetSkillContent() => AgentIntegrationContent.GetSharedSkillContent();

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
