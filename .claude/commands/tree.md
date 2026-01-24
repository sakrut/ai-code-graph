Display code structure tree.

Steps:
1. Run `ai-code-graph tree --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the hierarchical structure: Projects > Namespaces > Types > Methods
4. Use the structure to understand the codebase organization and identify where new code should be placed
