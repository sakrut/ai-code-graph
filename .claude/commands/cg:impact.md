Show transitive impact of changing a method: $ARGUMENTS

Steps:
1. Run `ai-code-graph impact "$ARGUMENTS" --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the full chain of methods that would be affected by changes to this method
4. Highlight entry points (methods with no further callers) as they represent top-level impact boundaries
5. Use `--depth N` to limit traversal if the impact tree is too large
