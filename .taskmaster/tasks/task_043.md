# Task ID: 43

**Title:** ONNX Embedding Engine Adapter

**Status:** done

**Dependencies:** 30 ✓, 31 ✓, 32 ✓, 33 ✓, 35 ✓, 37 ✓, 38 ✓, 39 ✓, 40 ✓, 41 ✓

**Priority:** medium

**Description:** Implement IEmbeddingEngine using local ONNX Runtime for running models like all-MiniLM-L6-v2 without external API dependencies.

**Details:**

Create new file: AiCodeGraph.Core/Embeddings/OnnxEmbeddingEngine.cs

First, add NuGet package to Core project:
```bash
dotnet add AiCodeGraph.Core package Microsoft.ML.OnnxRuntime
```

```csharp
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AiCodeGraph.Core.Embeddings;

public class OnnxEmbeddingEngine : IEmbeddingEngine
{
    private readonly InferenceSession _session;
    private readonly int _dimensions;
    private readonly int _maxTokens;
    
    public int Dimensions => _dimensions;
    
    public OnnxEmbeddingEngine(string modelPath, int dimensions = 384, int maxTokens = 512)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("ONNX model not found", modelPath);
        
        _dimensions = dimensions;
        _maxTokens = maxTokens;
        _session = new InferenceSession(modelPath);
    }
    
    public float[] GenerateEmbedding(string text)
    {
        // Simple whitespace tokenization (for models that accept raw token IDs)
        // For production, would need a proper tokenizer (e.g., BPE)
        var tokens = SimpleTokenize(text);
        
        // Create input tensors
        var inputIds = new DenseTensor<long>(new[] { 1, tokens.Length });
        var attentionMask = new DenseTensor<long>(new[] { 1, tokens.Length });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, tokens.Length });
        
        for (int i = 0; i < tokens.Length; i++)
        {
            inputIds[0, i] = tokens[i];
            attentionMask[0, i] = 1;
            tokenTypeIds[0, i] = 0;
        }
        
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };
        
        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();
        
        // Mean pooling over token dimension
        var embedding = new float[_dimensions];
        for (int d = 0; d < _dimensions; d++)
        {
            float sum = 0;
            for (int t = 0; t < tokens.Length; t++)
                sum += output[0, t, d];
            embedding[d] = sum / tokens.Length;
        }
        
        return embedding;
    }
    
    private long[] SimpleTokenize(string text)
    {
        // Basic tokenization - split by whitespace, hash to vocab range
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokens = new long[Math.Min(words.Length, _maxTokens)];
        for (int i = 0; i < tokens.Length; i++)
            tokens[i] = Math.Abs(words[i].GetHashCode()) % 30522; // BERT vocab size
        return tokens;
    }
    
    public void Dispose() => _session.Dispose();
}
```

Note: The ONNX Runtime package should be added as an optional dependency. Consider making it a separate project (AiCodeGraph.Onnx) to avoid bloating the core package.

**Test Strategy:**

Create OnnxEmbeddingEngineTests.cs. Since ONNX models are large, test with: (1) Constructor with missing model path throws FileNotFoundException. (2) If a small test model is available, verify output dimensions match. (3) Mock InferenceSession for unit tests. (4) Verify Dispose cleans up session. (5) Integration test (marked with [Trait]) that runs with actual model if available.

## Subtasks

### 43.1. Add OnnxRuntime NuGet package and create OnnxEmbeddingEngine class with InferenceSession lifecycle

**Status:** pending  
**Dependencies:** None  

Add the Microsoft.ML.OnnxRuntime NuGet package reference to AiCodeGraph.Core and create the OnnxEmbeddingEngine class implementing IEmbeddingEngine. The class should manage an InferenceSession with proper constructor validation (FileNotFoundException for missing model) and IDisposable implementation to clean up the session.

**Details:**

1. Run `dotnet add AiCodeGraph.Core package Microsoft.ML.OnnxRuntime` to add the dependency.
2. Create `AiCodeGraph.Core/Embeddings/OnnxEmbeddingEngine.cs` with:
   - Private readonly fields: `InferenceSession _session`, `int _dimensions`, `int _maxTokens`
   - Public property `int Dimensions => _dimensions` (satisfies IEmbeddingEngine)
   - Constructor `OnnxEmbeddingEngine(string modelPath, int dimensions = 384, int maxTokens = 512)` that validates the file exists (throw FileNotFoundException if not) and creates the InferenceSession
   - `Dispose()` method that disposes the InferenceSession
   - Stub `GenerateEmbedding(string text)` returning a zero vector initially
3. Consider whether to place this in a separate project (AiCodeGraph.Onnx) to keep the OnnxRuntime dependency optional. If kept in Core, the package could be marked as a PrivateAsset or the class can be conditionally compiled.

### 43.2. Implement tokenization and BERT-like tensor construction

**Status:** pending  
**Dependencies:** 43.1  

Implement the SimpleTokenize method for basic whitespace tokenization with hash-to-vocab-range mapping, and build the input tensor construction logic that creates input_ids, attention_mask, and token_type_ids DenseTensor<long> instances for BERT-like model input.

**Details:**

1. Implement `private long[] SimpleTokenize(string text)` method:
   - Split text by whitespace with `StringSplitOptions.RemoveEmptyEntries`
   - Limit to `_maxTokens` tokens
   - Map each word to a vocab index using `Math.Abs(word.GetHashCode()) % 30522` (BERT vocab size)
   - Return the long[] array of token IDs
2. In `GenerateEmbedding`, build three DenseTensor<long> instances with shape [1, tokenCount]:
   - `input_ids`: filled with the tokenized values
   - `attention_mask`: filled with 1s for all token positions
   - `token_type_ids`: filled with 0s for all positions
3. Create the `List<NamedOnnxValue>` with named tensors "input_ids", "attention_mask", "token_type_ids"
4. Call `_session.Run(inputs)` and store the result
5. Handle edge case of empty text (return zero vector like HashEmbeddingEngine does)

### 43.3. Implement mean pooling over token dimension from model output

**Status:** pending  
**Dependencies:** 43.2  

Extract the model output tensor and implement mean pooling across the token dimension to produce the final fixed-size embedding vector matching the configured dimensions.

**Details:**

1. After `_session.Run(inputs)`, get the first output result and cast to `Tensor<float>` using `results.First().AsTensor<float>()`
2. The output tensor has shape [1, token_count, dimensions] for BERT-like models
3. Implement mean pooling:
   - Allocate `float[_dimensions]` for the embedding
   - For each dimension d in [0, _dimensions):
     - Sum output[0, t, d] across all tokens t in [0, token_count)
     - Divide by token_count to get the mean
4. Return the pooled embedding vector
5. Consider adding L2 normalization (as HashEmbeddingEngine does) for consistency, or make it optional
6. Ensure proper disposal of the Run results using `using` statement

### 43.4. Write unit tests for OnnxEmbeddingEngine

**Status:** pending  
**Dependencies:** 43.1, 43.2, 43.3  

Create comprehensive unit tests covering constructor validation, dispose behavior, and optional integration tests with a real ONNX model file for end-to-end verification.

**Details:**

1. Create `AiCodeGraph.Tests/Embeddings/OnnxEmbeddingEngineTests.cs`
2. Unit tests (no model file needed):
   - `Constructor_WithMissingModelPath_ThrowsFileNotFoundException`: Verify FileNotFoundException with a non-existent path
   - `Constructor_WithNullModelPath_ThrowsException`: Verify argument handling for null
   - `Dimensions_ReturnsConfiguredValue`: Verify default (384) and custom dimensions
   - `Dispose_DoesNotThrow`: Create with valid path (if available) or verify dispose pattern
3. Integration tests (require a model file, use [Trait] or conditional skip):
   - `GenerateEmbedding_WithRealModel_ReturnsCorrectDimensions`: Load a real ONNX model and verify output length
   - `GenerateEmbedding_WithEmptyText_ReturnsZeroVector`: Verify edge case handling
   - `GenerateEmbedding_DifferentTexts_ProduceDifferentVectors`: Semantic difference check
4. Use `[Fact]` for unit tests and `[Fact(Skip = "Requires ONNX model file")]` or environment-based skip for integration tests
5. Follow existing test patterns from the project (xUnit, naming conventions like `{Method}_{Scenario}_{Expected}`)
