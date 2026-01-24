Show methods with high change-frequency x complexity (churn hotspots): $ARGUMENTS

Steps:
1. Run `ai-code-graph churn --since "$ARGUMENTS" --db ./ai-code-graph/graph.db` (use "6 months ago" if no argument provided)
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the results ranked by churn score (changes × complexity)
4. For the top results, explain why they are risky: high change frequency combined with high complexity
5. Suggest which methods would benefit most from refactoring to reduce complexity
