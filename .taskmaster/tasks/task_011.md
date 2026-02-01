# Task ID: 11

**Title:** Implement Duplicate Detection and Intent Clustering

**Status:** done

**Dependencies:** 9 ✓, 10 ✓

**Priority:** medium

**Description:** Detect structural clones (AST similarity) and semantic clones (embedding similarity), compute hybrid scores, and cluster methods by intent to identify patterns like 'permission checks' or 'tag management'.

**Details:**

1. **Structural Clone Detection:**
   ```csharp
   public class StructuralCloneDetector
   {
       public List<ClonePair> DetectClones(List<NormalizedMethod> methods, float threshold = 0.8f)
       {
           // Compare structural signatures
           // Use edit distance or token-level Jaccard similarity
           // Return pairs above threshold
       }
   }
   ```
2. **Semantic Clone Detection:**
   ```csharp
   public class SemanticCloneDetector
   {
       public List<ClonePair> DetectClones(VectorIndex index, List<NormalizedMethod> methods, float threshold = 0.85f)
       {
           // For each method, find k nearest neighbors
           // Filter by cosine similarity threshold
           // Return pairs
       }
   }
   ```
3. **Hybrid Scoring:**
   - `HybridScore = α * StructuralSimilarity + (1-α) * SemanticSimilarity`
   - Default α = 0.4 (favor semantic similarity)
   - Configurable threshold for reporting

4. **Intent Clustering:**
   ```csharp
   public class IntentClusterer
   {
       public List<IntentCluster> ClusterMethods(List<NormalizedMethod> methods, VectorIndex index)
       {
           // Use DBSCAN or agglomerative clustering on embedding vectors
           // Label clusters using common tokens from semantic payloads
           // Return labeled clusters with member methods
       }
   }
   ```
5. Create `IntentCluster` record: `(string Id, string Label, string Description, List<string> MethodIds, float Cohesion)`
6. Auto-label clusters by finding most common identifier tokens among members
7. Store clusters and clone pairs in SQLite
8. Support configurable thresholds via CLI options

**Test Strategy:**

Create test fixtures with known duplicates: exact clones, near-clones (renamed variables), semantic clones (same intent, different implementation). Verify structural detection catches renamed-variable clones. Verify semantic detection catches different-implementation clones. Test clustering produces meaningful groups. Verify hybrid scoring correctly combines both signals.

## Subtasks

### 11.1. Implement StructuralCloneDetector with Token-Level Jaccard Similarity

**Status:** pending  
**Dependencies:** None  

Create the StructuralCloneDetector class that compares normalized method structural signatures using edit distance and token-level Jaccard similarity to identify structural clones above a configurable threshold.

**Details:**

Implement StructuralCloneDetector in the Core project with a DetectClones method that accepts a list of NormalizedMethod objects and a similarity threshold (default 0.8f). For each pair of methods, compute token-level Jaccard similarity on their structural signatures (tokenized type sequences, control flow patterns). Also implement Levenshtein edit distance as an alternative metric for finer-grained comparison. Use early termination when signature length differences exceed threshold bounds to optimize the O(n²) pairwise comparison. Return a List<ClonePair> containing method ID pairs and their structural similarity scores. Define ClonePair as a record: (string MethodIdA, string MethodIdB, float StructuralSimilarity, float SemanticSimilarity, float HybridScore, CloneType Type). Consider batching comparisons and parallelizing with Parallel.ForEach for large method sets.

### 11.2. Implement SemanticCloneDetector with kNN Embedding Search

**Status:** pending  
**Dependencies:** None  

Create the SemanticCloneDetector class that uses the VectorIndex to perform kNN search on method embedding vectors, identifying semantically similar method pairs above a cosine similarity threshold.

**Details:**

Implement SemanticCloneDetector with a DetectClones method that accepts a VectorIndex, a list of NormalizedMethod objects, and a similarity threshold (default 0.85f). For each method, query the VectorIndex for k nearest neighbors (k configurable, default 10). Filter results by cosine similarity threshold, excluding self-matches. Deduplicate pairs (A,B) and (B,A) into a single ClonePair. Populate the SemanticSimilarity field in each ClonePair. Handle edge cases: methods without embeddings (skip with warning), empty index, threshold of 1.0 (exact matches only). Use batch querying if the VectorIndex supports it to reduce overhead.

### 11.3. Implement Hybrid Scoring with Configurable Alpha Weight

**Status:** pending  
**Dependencies:** 11.1, 11.2  

Implement the hybrid scoring formula that combines structural and semantic similarity scores with a configurable alpha weight, and produce a unified ranked list of clone pairs.

**Details:**

Create a HybridScorer class that takes structural clone pairs and semantic clone pairs, merges them by method ID pair, and computes HybridScore = α * StructuralSimilarity + (1-α) * SemanticSimilarity. Default α = 0.4 (favoring semantic similarity). For pairs found only by one detector, use 0.0 for the missing score. Support a configurable hybrid threshold for filtering the final output. Implement a Merge method that unions both sets of pairs, joining on (MethodIdA, MethodIdB), and computes the hybrid score. Classify clone types: Type1 (structural > 0.95), Type2 (structural > 0.8), Semantic (semantic > 0.85, structural < 0.8). Sort results by hybrid score descending. Expose alpha and threshold as constructor parameters or options.

### 11.4. Implement IntentClusterer with DBSCAN on Embedding Vectors

**Status:** pending  
**Dependencies:** None  

Implement the IntentClusterer class that groups methods by semantic intent using DBSCAN clustering on their embedding vectors, producing labeled clusters with cohesion scores.

**Details:**

Create IntentClusterer with a ClusterMethods method that accepts NormalizedMethod list and VectorIndex. Extract embedding vectors for all methods from the index. Implement DBSCAN algorithm with configurable epsilon (default 0.3 for cosine distance) and minPoints (default 3). Use cosine distance (1 - cosine_similarity) as the distance metric. For each resulting cluster, compute cohesion as the average pairwise cosine similarity of members. Create IntentCluster record: (string Id, string Label, string Description, List<string> MethodIds, float Cohesion). Generate cluster IDs as 'cluster-{n}'. Mark noise points (not in any cluster) separately. Consider implementing agglomerative clustering as a fallback when DBSCAN produces too many noise points (>50% of methods).

### 11.5. Implement Cluster Auto-Labeling Using Common Identifier Tokens

**Status:** pending  
**Dependencies:** 11.4  

Implement automatic label generation for intent clusters by analyzing the most frequent identifier tokens and semantic payload terms among cluster members.

**Details:**

Create a ClusterLabeler class with a LabelCluster method that accepts an IntentCluster and the corresponding NormalizedMethod objects. Extract identifier tokens from each member method: method names (split by camelCase/PascalCase), parameter names, return type names, and key tokens from semantic payloads. Compute token frequency across all members, excluding common stop-words (get, set, is, has, the, a, etc.) and C# keywords. Select top 2-3 most frequent meaningful tokens to form the label (e.g., 'permission check', 'tag management', 'customer validation'). Generate a description by combining the label with member count and average cohesion. Handle edge cases: single-member clusters, clusters with no common tokens (use 'miscellaneous-{n}'). Apply the labeler to all clusters produced by IntentClusterer.

### 11.6. Store Clone Pairs and Intent Clusters in SQLite

**Status:** pending  
**Dependencies:** 11.3, 11.5  

Create SQLite tables for persisting clone pairs (ClonePairs) and intent clusters (IntentClusters, MethodClusterMap), with methods for insert, query, and threshold-based filtering.

**Details:**

Extend the existing SQLite database schema with three tables: 1) ClonePairs (MethodIdA TEXT, MethodIdB TEXT, StructuralSimilarity REAL, SemanticSimilarity REAL, HybridScore REAL, CloneType TEXT, PRIMARY KEY(MethodIdA, MethodIdB)). 2) IntentClusters (ClusterId TEXT PRIMARY KEY, Label TEXT, Description TEXT, Cohesion REAL, MemberCount INTEGER). 3) MethodClusterMap (MethodId TEXT, ClusterId TEXT, PRIMARY KEY(MethodId, ClusterId), FOREIGN KEY ClusterId REFERENCES IntentClusters). Create a DuplicateRepository class with methods: SaveClonePairs(List<ClonePair>), GetClonePairs(float minThreshold, string type), SaveClusters(List<IntentCluster>), GetClusters(), GetClusterMembers(string clusterId). Use transactions for batch inserts. Add indexes on HybridScore and CloneType for efficient filtering. Support upsert semantics for re-analysis runs.
