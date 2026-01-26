Display code structure tree.

Steps:
1. Run `ai-code-graph tree --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the hierarchical structure: Projects > Namespaces > Types > Methods
4. Use the structure to understand the codebase organization and identify where new code should be placed

Options:
- `--namespace <prefix>` - Filter by namespace prefix
- `--type <name>` - Filter by type name
- `--include-private` - Include non-public methods (default: public only)
- `--format json` - Output as JSON with accessibility field

Notes:
- By default, only **public methods** are shown
- **Constructors are always excluded** regardless of visibility settings
- When `--include-private` is used, non-public methods are tagged with `[private]`, `[internal]`, etc.
- JSON format includes an `accessibility` field for each method
