Run drift detection against the baseline.

Steps:
1. Check if `./ai-code-graph/baseline.db` exists. If not, inform the user to run `ai-code-graph analyze --save-baseline` first
2. Run `ai-code-graph drift --vs ./ai-code-graph/baseline.db --format detail --db ./ai-code-graph/graph.db`
3. Summarize findings:
   - New methods added
   - Methods removed
   - Complexity regressions (methods that got more complex)
   - New duplicates introduced
   - Intent scattering (logic spreading across namespaces)
4. For complexity regressions, show the before/after values and suggest refactoring if the increase is significant
