Find methods with no callers (potential dead code).

Steps:
1. Run `ai-code-graph dead-code --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the list of potentially unreachable methods, sorted by complexity
4. Highlight high-complexity dead code as priority candidates for removal
5. Note that test methods, constructors, and override methods are excluded by default
6. Use `--include-overrides` to also show override/abstract methods
