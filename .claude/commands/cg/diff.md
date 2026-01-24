Show methods affected by changes between git refs: $ARGUMENTS

Steps:
1. Run `ai-code-graph diff --from HEAD~1 --to HEAD --format detail --db ./ai-code-graph/graph.db` (adjust refs if $ARGUMENTS specifies them)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results showing changed files and affected methods with their complexity
4. Highlight high-complexity methods (CC > 10) that were touched - these are risky changes
5. Suggest reviewing methods with high complexity that appear in the diff
