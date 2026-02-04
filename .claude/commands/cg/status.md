Show database status and staleness detection.

Steps:
1. Run `ai-code-graph status --db ./ai-code-graph/graph.db`
2. If the database doesn't exist, inform the user to run `ai-code-graph analyze` first
3. Present the status information:
   - Database path and size
   - Last analysis timestamp
   - Method/type/namespace counts
   - Staleness indicator (files changed since last analysis)
4. If database is stale, suggest re-running `ai-code-graph analyze`
