Show intent clusters in the codebase.

Steps:
1. Run `ai-code-graph clusters --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present each cluster with its label, cohesion score, and member methods
4. Highlight clusters with low cohesion (<0.5) as candidates for refactoring
5. Use cluster information to understand which methods belong together conceptually
