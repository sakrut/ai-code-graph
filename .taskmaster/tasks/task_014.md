# Task ID: 14

**Title:** Implement Diff and Drift Detection Engine

**Status:** done

**Dependencies:** 7 ✓, 11 ✓

**Priority:** low

**Description:** Compare current analysis against a previous snapshot or main branch artifact to detect new duplicates, complexity regressions, and scattered intent clusters.

**Details:**

1. **Snapshot Management:**
   - After each analysis, optionally save a baseline: `./ai-code-graph/baseline.db`
   - `ai-code-graph analyze --save-baseline` saves current as baseline
   - Compare current run vs saved baseline

2. **Diff Engine:**
   ```csharp
   public class DriftDetector
   {
       public DriftReport Compare(string currentDbPath, string baselineDbPath)
       {
           // Compare:
           // 1. New methods not in baseline
           // 2. Removed methods
           // 3. Complexity changes (regressions)
           // 4. New duplicate pairs
           // 5. New cluster members (intent scattering)
       }
   }
   ```
3. Create `DriftReport` model:
   ```csharp
   public record DriftReport(
       List<MethodDiff> NewMethods,
       List<MethodDiff> RemovedMethods,
       List<ComplexityRegression> Regressions,
       List<ClonePair> NewDuplicates,
       List<ScatteringAlert> IntentScattering
   );
   ```
4. **`drift` CLI command:**
   ```csharp
   var driftCmd = new Command("drift", "Detect architectural drift");
   driftCmd.AddOption(new Option<string>("--vs", () => "baseline", "baseline|<path>"));
   driftCmd.AddOption(new Option<string>("--format", () => "summary", "summary|detail|json"));
   ```
5. Complexity regression: flag methods where complexity increased by >25% or crossed threshold (e.g., >15)
6. Intent scattering: detect when a cluster gains members in new namespaces
7. Support comparing against a specific database file path (e.g., from main branch CI artifact)

**Test Strategy:**

Create two database snapshots with known differences. Test detection of: new methods, removed methods, complexity increases, new duplicates. Test threshold-based regression detection. Test scattering detection when cluster grows across namespaces. Test all output formats.

## Subtasks

### 14.1. Implement Baseline Snapshot Management

**Status:** pending  
**Dependencies:** None  

Add --save-baseline flag to the analyze command that copies the current analysis database to a designated baseline path (./ai-code-graph/baseline.db). Implement path resolution logic for baseline files and support both default baseline location and custom paths.

**Details:**

1. Add a `--save-baseline` option to the existing `analyze` CLI command that, when specified, copies the current analysis SQLite database to `./ai-code-graph/baseline.db` after analysis completes.
2. Create a `BaselineManager` class responsible for:
   - Saving a database snapshot: copy the current DB file to the baseline path atomically (write to temp file, then move)
   - Resolving baseline paths: support `baseline` keyword (resolves to default path) and arbitrary file paths
   - Checking if a baseline exists and is valid (has expected tables/schema)
3. Ensure the baseline directory exists before saving (create if needed).
4. Add validation that the source database is complete before saving as baseline (all analysis tables populated).
5. Support overwriting an existing baseline with a confirmation-style approach (force overwrite since CLI is non-interactive).

### 14.2. Implement DriftDetector Core Comparison Logic

**Status:** pending  
**Dependencies:** 14.1  

Create the DriftDetector class that opens two SQLite databases simultaneously (current and baseline) and computes set-based diffs for methods: new methods not in baseline, removed methods no longer present, and basic structural changes.

**Details:**

1. Create `DriftDetector` class with a `Compare(string currentDbPath, string baselineDbPath)` method.
2. Create `DriftReport` record model:
   ```csharp
   public record DriftReport(
       List<MethodDiff> NewMethods,
       List<MethodDiff> RemovedMethods,
       List<ComplexityRegression> Regressions,
       List<ClonePair> NewDuplicates,
       List<ScatteringAlert> IntentScattering
   );
   ```
3. Create supporting models: `MethodDiff` (method ID, name, namespace, file path), `ComplexityRegression`, `ClonePair`, `ScatteringAlert`.
4. Open both databases using separate SQLite connections. Query the methods table from each.
5. Compute set difference for new methods (in current but not baseline, matched by stable symbol ID).
6. Compute set difference for removed methods (in baseline but not current).
7. Detect new duplicate pairs by comparing clone pair tables between the two databases.
8. Handle edge cases: missing tables in either database, schema version mismatches, empty databases.

### 14.3. Implement Complexity Regression Detection

**Status:** pending  
**Dependencies:** 14.2  

Add complexity regression detection to the DriftDetector that flags methods where cyclomatic complexity increased by more than 25% or crossed an absolute threshold (default >15), with configurable threshold parameters.

**Details:**

1. Add `ComplexityRegression` model:
   ```csharp
   public record ComplexityRegression(
       string MethodId,
       string MethodName,
       string Namespace,
       int BaselineComplexity,
       int CurrentComplexity,
       double PercentageIncrease,
       bool CrossedAbsoluteThreshold,
       int AbsoluteThreshold
   );
   ```
2. In `DriftDetector.Compare()`, for each method present in both databases, compare complexity metrics.
3. Flag as regression if: (a) complexity increased by >25% (configurable via `percentageThreshold` parameter, default 0.25), OR (b) complexity crossed the absolute threshold (configurable, default 15).
4. Add configuration options to DriftDetector or a `DriftDetectorOptions` class:
   - `ComplexityPercentageThreshold` (default 0.25)
   - `ComplexityAbsoluteThreshold` (default 15)
5. Sort regressions by severity (highest percentage increase first).
6. Only flag increases, not decreases (decreases are improvements, not regressions).

### 14.4. Implement Intent Scattering Detection

**Status:** pending  
**Dependencies:** 14.2  

Detect when an intent cluster gains members in new namespaces compared to the baseline, indicating that related functionality is becoming more scattered across the codebase rather than consolidated.

**Details:**

1. Add `ScatteringAlert` model:
   ```csharp
   public record ScatteringAlert(
       string ClusterId,
       string ClusterLabel,
       List<string> BaselineNamespaces,
       List<string> NewNamespaces,
       List<string> NewMemberMethods,
       int TotalMemberCount
   );
   ```
2. In `DriftDetector.Compare()`, query cluster membership tables from both databases.
3. For each cluster present in both databases:
   - Get the set of namespaces containing cluster members in baseline
   - Get the set of namespaces containing cluster members in current
   - If current has namespaces not present in baseline, create a ScatteringAlert
4. Include the specific new methods that were added in the new namespaces.
5. Handle cluster ID matching between databases (clusters may be re-computed, so match by label or by significant member overlap if IDs differ).
6. Filter out trivial scattering (e.g., a single method in a test namespace).

### 14.5. Implement Drift CLI Command with Output Formats

**Status:** pending  
**Dependencies:** 14.1, 14.2, 14.3, 14.4  

Add the `drift` CLI command that invokes DriftDetector.Compare() and presents results in summary, detail, or JSON format, with --vs option to specify the comparison target (baseline path or keyword).

**Details:**

1. Register a new `drift` command in the CLI command hierarchy:
   ```csharp
   var driftCmd = new Command("drift", "Detect architectural drift");
   driftCmd.AddOption(new Option<string>("--vs", () => "baseline", "baseline|<path>"));
   driftCmd.AddOption(new Option<string>("--format", () => "summary", "summary|detail|json"));
   driftCmd.AddOption(new Option<double>("--complexity-pct", () => 0.25, "Complexity percentage threshold"));
   driftCmd.AddOption(new Option<int>("--complexity-abs", () => 15, "Complexity absolute threshold"));
   ```
2. In the command handler:
   - Resolve the `--vs` path ("baseline" maps to default path, otherwise use as file path)
   - Validate both current DB and baseline DB exist
   - Create DriftDetector with configured thresholds and call Compare()
3. Output formatters:
   - **Summary**: One-line counts (e.g., "3 new methods, 1 removed, 2 complexity regressions, 1 new duplicate pair, 1 scattering alert")
   - **Detail**: Grouped sections with full method names, complexity values, namespace details
   - **JSON**: Serialize the full DriftReport to JSON with proper formatting
4. Set appropriate exit codes: 0 for no drift, 1 for drift detected (useful in CI pipelines).
5. Handle error cases with user-friendly messages: baseline not found, databases incompatible, etc.
