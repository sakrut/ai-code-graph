# Task ID: 10

**Title:** Implement Local Embedding Engine and Vector Index

**Status:** done

**Dependencies:** 9 ✓

**Priority:** medium

**Description:** Integrate a local open-source embedding model to generate vector embeddings for each method's semantic payload, and implement a vector index for kNN similarity search.

**Details:**

1. **Embedding Model Selection:**
   - Use `Microsoft.ML.OnnxRuntime` to run a local ONNX embedding model
   - Recommended model: `all-MiniLM-L6-v2` (384 dimensions, fast, good quality)
   - Download model on first run to `./ai-code-graph/models/`
   - Alternative: use `SmartComponents.LocalEmbeddings` or `Microsoft.SemanticKernel` with local provider

2. Create `EmbeddingEngine` class:
   ```csharp
   public class EmbeddingEngine : IDisposable
   {
       public float[] GenerateEmbedding(string text);
       public List<(string MethodId, float[] Vector)> GenerateEmbeddings(List<NormalizedMethod> methods);
   }
   ```
3. **Vector Index** implementation:
   - Use a simple in-memory HNSW index or flat index for v1
   - Consider `Annoy.Net`, `HNSW.Net`, or implement flat brute-force for small codebases
   - Persist vectors to disk: `./ai-code-graph/vectors/` as binary files
   - Load into memory for search operations

4. Create `VectorIndex` class:
   ```csharp
   public class VectorIndex
   {
       public void BuildIndex(List<(string Id, float[] Vector)> items);
       public List<(string Id, float Score)> Search(float[] query, int topK = 10);
       public void SaveToDisk(string path);
       public void LoadFromDisk(string path);
   }
   ```
5. Cosine similarity for distance metric
6. Batch embedding generation with progress reporting
7. Cache embeddings - only regenerate for changed methods

**Test Strategy:**

Test embedding generation produces consistent vectors for same input. Test vector dimensions match expected (384 for MiniLM). Test kNN search returns correct nearest neighbors for known similar texts. Test persistence: save and reload vectors, verify search still works. Benchmark embedding generation time for 1000 methods. Test with edge cases: empty text, very long text.

## Subtasks

### 10.1. Research and Set Up ONNX Model Download/Caching Infrastructure

**Status:** done  
**Dependencies:** None  

Select the all-MiniLM-L6-v2 ONNX embedding model and implement infrastructure to download, cache, and validate the model file on first run.

**Details:**

1. Add `Microsoft.ML.OnnxRuntime` NuGet package to the project.
2. Create a `ModelManager` class responsible for:
   - Checking if the model exists at `./ai-code-graph/models/all-MiniLM-L6-v2.onnx`
   - Downloading the ONNX model from HuggingFace if not present (use HttpClient with progress reporting)
   - Validating the downloaded file (check file size, optionally SHA256 hash)
   - Providing the model path to the EmbeddingEngine
3. Handle cross-platform path considerations (Windows/Linux/Mac).
4. Include the tokenizer vocabulary file (`vocab.txt`) for the model's WordPiece tokenizer.
5. Add configuration options for custom model paths.
6. Consider retry logic for failed downloads and partial download resume support.

### 10.2. Implement EmbeddingEngine Class with ONNX Runtime and Tokenization

**Status:** done  
**Dependencies:** 10.1  

Create the EmbeddingEngine class that loads the ONNX model via OnnxRuntime, implements WordPiece tokenization matching the model's training, and generates 384-dimensional embeddings.

**Details:**

1. Implement `EmbeddingEngine : IDisposable` class:
   - Initialize `InferenceSession` with the ONNX model path
   - Configure session options (thread count, execution provider)
   - Implement proper disposal of the InferenceSession
2. Implement WordPiece tokenizer matching all-MiniLM-L6-v2 requirements:
   - Load `vocab.txt` vocabulary file
   - Implement text preprocessing: lowercase, Unicode normalization, punctuation handling
   - Implement WordPiece subword tokenization with `[UNK]` handling
   - Add special tokens: `[CLS]` at start, `[SEP]` at end
   - Generate `input_ids`, `attention_mask`, and `token_type_ids` tensors
   - Handle max sequence length (512 tokens) with truncation
3. Implement `float[] GenerateEmbedding(string text)` method:
   - Tokenize input text
   - Create OrtValue tensors for model input
   - Run inference
   - Apply mean pooling over token embeddings (using attention mask)
   - L2-normalize the resulting vector
   - Return 384-dimensional float array
4. Key challenge: Ensuring tokenizer output exactly matches the model's expected input format.

### 10.3. Implement Batch Embedding Generation with Progress Reporting

**Status:** done  
**Dependencies:** 10.2  

Add batch processing capability to EmbeddingEngine that efficiently generates embeddings for multiple methods with progress callbacks and cancellation support.

**Details:**

1. Implement `List<(string MethodId, float[] Vector)> GenerateEmbeddings(List<NormalizedMethod> methods, IProgress<int>? progress = null, CancellationToken ct = default)` method:
   - Accept a list of NormalizedMethod objects (from Task 9's semantic payload normalization)
   - Build semantic text from each method's normalized payload (combine name, parameters, body summary, doc comments)
   - Process in configurable batch sizes (default: 32) to balance memory and throughput
   - Report progress after each batch completion
   - Support cancellation between batches
2. Implement text preparation logic:
   - Concatenate method signature, doc comments, and normalized body into a single embedding input string
   - Truncate to reasonable length before tokenization
3. Consider parallel tokenization (CPU-bound) with sequential inference (GPU/CPU bound through ONNX)
4. Add logging for batch processing statistics (methods/second, total time)
5. Handle errors gracefully: log and skip methods that fail tokenization, don't abort entire batch.

### 10.4. Implement VectorIndex Class with Cosine Similarity Search

**Status:** done  
**Dependencies:** 10.2  

Create the VectorIndex class implementing flat brute-force kNN search using cosine similarity, with support for building, querying, and managing the in-memory index.

**Details:**

1. Implement `VectorIndex` class:
   ```csharp
   public class VectorIndex
   {
       private List<(string Id, float[] Vector)> _items;
       private int _dimensions; // 384 for MiniLM
       
       public void BuildIndex(List<(string Id, float[] Vector)> items);
       public List<(string Id, float Score)> Search(float[] query, int topK = 10);
       public void AddItem(string id, float[] vector);
       public void RemoveItem(string id);
       public int Count { get; }
   }
   ```
2. Implement cosine similarity calculation:
   - `CosineSimilarity(float[] a, float[] b)` using dot product / (magnitude_a * magnitude_b)
   - Pre-normalize vectors during BuildIndex for faster search (then cosine = dot product)
   - Use SIMD intrinsics (`System.Numerics.Vector<float>`) for vectorized dot product computation
3. Implement brute-force search:
   - Compute similarity against all indexed vectors
   - Use a min-heap or partial sort to efficiently find top-K results
   - Return results sorted by descending similarity score
4. Validate input dimensions match expected (384)
5. Thread-safety: use `ReaderWriterLockSlim` for concurrent read access during search
6. Consider future HNSW upgrade path: define `IVectorIndex` interface for abstraction.

### 10.5. Implement Vector Persistence (Save/Load to Disk)

**Status:** done  
**Dependencies:** 10.4  

Add binary serialization for the vector index, enabling save to and load from disk at the ./ai-code-graph/vectors/ path with efficient binary format.

**Details:**

1. Add persistence methods to VectorIndex:
   ```csharp
   public void SaveToDisk(string path);
   public void LoadFromDisk(string path);
   ```
2. Design binary file format:
   - Header: magic bytes (4 bytes), version (4 bytes), dimension count (4 bytes), item count (4 bytes)
   - For each item: ID length (4 bytes), ID string (UTF-8), vector data (dimensions * 4 bytes as float32)
   - Footer: checksum (optional, for integrity verification)
3. Implementation details:
   - Use `BinaryWriter`/`BinaryReader` for efficient serialization
   - Write vectors directory to `./ai-code-graph/vectors/index.bin`
   - Support versioning in format header for future format changes
   - Memory-map large files if needed (for future optimization)
4. Directory management:
   - Create `./ai-code-graph/vectors/` directory if it doesn't exist
   - Support configurable base path
5. Handle corruption gracefully: validate header on load, provide clear error messages
6. Add metadata file alongside binary (JSON with creation timestamp, method count, model version used)

### 10.6. Add Embedding Caching Logic for Changed Method Detection

**Status:** done  
**Dependencies:** 10.3, 10.5  

Implement smart caching that detects which methods have changed since last embedding generation and only regenerates embeddings for modified methods, preserving unchanged embeddings.

**Details:**

1. Create `EmbeddingCache` class:
   ```csharp
   public class EmbeddingCache
   {
       public List<string> GetChangedMethodIds(List<NormalizedMethod> currentMethods);
       public void UpdateCache(List<(string MethodId, float[] Vector)> newEmbeddings);
       public List<(string MethodId, float[] Vector)> GetCachedEmbeddings();
       public void RemoveStaleEntries(List<string> currentMethodIds);
   }
   ```
2. Change detection strategy:
   - Store a content hash (SHA256 or xxHash for speed) of each method's semantic payload alongside its embedding
   - On re-analysis, compute hash of current method payload
   - Compare with stored hash: if different, mark for re-embedding
   - Handle new methods (no cached hash) and removed methods (stale cache entries)
3. Cache storage:
   - Store hash map as JSON or binary file: `./ai-code-graph/vectors/cache-manifest.json`
   - Format: `{ "methodId": { "hash": "abc123", "vectorOffset": 0 } }`
4. Integration with EmbeddingEngine:
   - Create orchestration method that coordinates cache checking, selective re-embedding, and cache updating
   - Merge new embeddings with cached ones for complete index rebuild
5. Handle edge cases:
   - Model version change (invalidate all cache)
   - Renamed methods (new ID = new embedding needed, old removed)
   - Store model version in manifest to detect model changes
