Graph-based method retrieval: $ARGUMENTS

Steps:
1. Parse $ARGUMENTS for quick options or JSON query:
   - `--callers MethodName` -> find all callers of a method
   - `--callees MethodName` -> find all callees of a method
   - `--impact MethodName` -> transitive impact analysis
   - `--cluster ClusterLabel` -> methods in a cluster
   - JSON query for advanced use
2. Run `ai-code-graph query $ARGUMENTS --db ./ai-code-graph/graph.db`
3. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
4. Present the results with method IDs for stable references
5. Use `--format json` for structured output if needed
