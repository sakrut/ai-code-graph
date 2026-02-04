# Task ID: 49

**Title:** LSH Spatial Index for DBSCAN Optimization

**Status:** done

**Dependencies:** 30 ✓, 31 ✓, 32 ✓, 33 ✓, 35 ✓, 37 ✓, 38 ✓, 39 ✓, 40 ✓, 41 ✓

**Priority:** medium

**Description:** Implement Locality-Sensitive Hashing (LSH) for 384-dimensional vectors to replace O(n) brute-force neighbor queries in DBSCAN, achieving O(n) amortized total complexity.

**Details:**

Create new file: AiCodeGraph.Core/Embeddings/SpatialIndex.cs

```csharp
namespace AiCodeGraph.Core.Embeddings;

public class LshSpatialIndex
{
    private readonly int _numHashFunctions;
    private readonly int _numBands;
    private readonly int _dimensions;
    private readonly float[][] _randomProjections;
    private readonly Dictionary<int, List<int>> _buckets;
    private readonly Random _rng;
    
    public LshSpatialIndex(int dimensions = 384, int numHashFunctions = 32, int numBands = 8, int? seed = null)
    {
        _dimensions = dimensions;
        _numHashFunctions = numHashFunctions;
        _numBands = numBands;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _buckets = new Dictionary<int, List<int>>();
        
        // Generate random projection vectors
        _randomProjections = new float[numHashFunctions][];
        for (int i = 0; i < numHashFunctions; i++)
        {
            _randomProjections[i] = new float[dimensions];
            for (int j = 0; j < dimensions; j++)
                _randomProjections[i][j] = (float)NextGaussian();
        }
    }
    
    public void BuildIndex(List<float[]> vectors)
    {
        _buckets.Clear();
        for (int i = 0; i < vectors.Count; i++)
        {
            var hashes = ComputeHashes(vectors[i]);
            var bandHashes = GetBandHashes(hashes);
            
            foreach (var bh in bandHashes)
            {
                if (!_buckets.TryGetValue(bh, out var bucket))
                {
                    bucket = new List<int>();
                    _buckets[bh] = bucket;
                }
                bucket.Add(i);
            }
        }
    }
    
    public HashSet<int> GetCandidateNeighbors(float[] query)
    {
        var candidates = new HashSet<int>();
        var hashes = ComputeHashes(query);
        var bandHashes = GetBandHashes(hashes);
        
        foreach (var bh in bandHashes)
        {
            if (_buckets.TryGetValue(bh, out var bucket))
                candidates.UnionWith(bucket);
        }
        
        return candidates;
    }
    
    private int[] ComputeHashes(float[] vector)
    {
        var hashes = new int[_numHashFunctions];
        for (int i = 0; i < _numHashFunctions; i++)
        {
            float dot = 0;
            for (int j = 0; j < _dimensions; j++)
                dot += vector[j] * _randomProjections[i][j];
            hashes[i] = dot >= 0 ? 1 : 0;
        }
        return hashes;
    }
    
    private List<int> GetBandHashes(int[] hashes)
    {
        var bandSize = _numHashFunctions / _numBands;
        var results = new List<int>(_numBands);
        for (int b = 0; b < _numBands; b++)
        {
            var hash = new HashCode();
            hash.Add(b); // band identifier
            for (int i = b * bandSize; i < (b + 1) * bandSize; i++)
                hash.Add(hashes[i]);
            results.Add(hash.ToHashCode());
        }
        return results;
    }
    
    private double NextGaussian()
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
```

Modify IntentClusterer.GetNeighbors() to use LshSpatialIndex:
```csharp
private List<int> GetNeighbors(int pointIdx, ...)
{
    var candidates = _spatialIndex.GetCandidateNeighbors(vectorMap[methodIds[pointIdx]]);
    return candidates
        .Where(c => c != pointIdx && CosineDistance(vectorMap[methodIds[pointIdx]], vectorMap[methodIds[c]]) <= _epsilon)
        .ToList();
}
```

**Test Strategy:**

Create SpatialIndexTests.cs. (1) Verify same clustering results as brute-force (within tolerance due to LSH approximation). (2) Benchmark with 1000+ random vectors comparing LSH vs brute-force time. (3) Test edge cases: single vector, all identical vectors, orthogonal vectors. (4) Verify deterministic with seed. (5) Test with actual 384-dim hash embeddings from fixture.

## Subtasks

### 49.1. Implement Gaussian Random Projection Vector Generation

**Status:** pending  
**Dependencies:** None  

Implement the Box-Muller transform for Gaussian random number generation and the random projection matrix initialization in the LshSpatialIndex constructor.

**Details:**

Create the new file AiCodeGraph.Core/Embeddings/SpatialIndex.cs with the LshSpatialIndex class. Implement the constructor that accepts dimensions (default 384), numHashFunctions (default 32), numBands (default 8), and an optional seed for deterministic testing. Implement the NextGaussian() private method using the Box-Muller transform: generate two uniform random numbers u1, u2 and compute sqrt(-2*ln(u1))*sin(2*pi*u2). In the constructor, allocate _randomProjections as a float[numHashFunctions][dimensions] jagged array and fill each element with a sample from the Gaussian distribution. Store all configuration parameters as readonly fields. The seed parameter enables reproducible tests by seeding the Random instance.

### 49.2. Implement Hash Computation and Band-Based Bucketing

**Status:** pending  
**Dependencies:** 49.1  

Implement the ComputeHashes method for sign-bit random projections and the GetBandHashes method that partitions hash bits into bands for locality-sensitive bucketing.

**Details:**

Implement ComputeHashes(float[] vector) that computes the dot product of the input vector with each random projection vector and returns a sign bit (1 if dot >= 0, else 0) for each, producing an int[] of length numHashFunctions. Implement GetBandHashes(int[] hashes) that partitions the hash array into _numBands equal-sized bands (bandSize = numHashFunctions / numBands), and for each band computes a combined hash using System.HashCode by adding the band index and each hash bit in the band's range. Return a List<int> of band hash values. These two methods form the core LSH mechanism: similar vectors will share sign bits and thus collide in the same band buckets with high probability.

### 49.3. Implement BuildIndex and GetCandidateNeighbors Public API

**Status:** pending  
**Dependencies:** 49.2  

Implement the public BuildIndex and GetCandidateNeighbors methods that construct the LSH index from a vector collection and retrieve approximate neighbor candidates for a query vector.

**Details:**

Implement BuildIndex(List<float[]> vectors) that clears the _buckets dictionary, iterates over all vectors, computes their hashes and band hashes, and inserts each vector's index into the corresponding bucket lists in the dictionary. If a bucket key doesn't exist yet, create a new List<int> for it. Implement GetCandidateNeighbors(float[] query) that computes hashes and band hashes for the query, looks up each band hash in _buckets, and unions all found indices into a HashSet<int> to deduplicate candidates. Return the candidate set. This provides the O(1) amortized lookup per query that replaces the O(n) brute-force scan, with the trade-off being approximate results that require a distance verification step.

### 49.4. Integrate LshSpatialIndex with IntentClusterer GetNeighbors

**Status:** pending  
**Dependencies:** 49.3  

Modify the IntentClusterer class to build an LshSpatialIndex during DBSCAN initialization and use it in GetNeighbors to filter candidates before exact cosine distance computation.

**Details:**

In IntentClusterer (AiCodeGraph.Core/Duplicates/IntentClusterer.cs), add a private LshSpatialIndex field. Before the DBSCAN loop begins, instantiate LshSpatialIndex with dimensions=384 and call BuildIndex with the list of embedding vectors. Modify the GetNeighbors(int pointIdx, ...) method to first call _spatialIndex.GetCandidateNeighbors(vectorMap[methodIds[pointIdx]]) to get candidate indices, then filter candidates by computing exact CosineDistance and checking against _epsilon threshold, excluding the point itself. This replaces the previous O(n) linear scan of all points with an approximate candidate retrieval followed by exact verification on a smaller set. Ensure the method signature and return type remain compatible with existing DBSCAN logic.

### 49.5. Write Comprehensive Tests Comparing LSH vs Brute-Force and Benchmarking

**Status:** pending  
**Dependencies:** 49.3, 49.4  

Create SpatialIndexTests.cs with tests that verify correctness of LSH approximate results against brute-force ground truth and benchmark performance improvements.

**Details:**

Create AiCodeGraph.Tests/SpatialIndexTests.cs with the following test cases: (1) Determinism test: with a fixed seed, verify BuildIndex and GetCandidateNeighbors produce identical results across multiple runs. (2) Recall test: generate 200+ random 384-d vectors, for each vector compute true neighbors via brute-force cosine distance within epsilon, then compute LSH candidates and verify recall >= 0.8 (at least 80% of true neighbors found). (3) Edge cases: single vector returns empty neighbors, all-identical vectors are all candidates for each other, orthogonal vectors are not candidates. (4) Performance benchmark: generate 1000+ random vectors, time the brute-force O(n^2) neighbor computation vs LSH-backed computation, assert LSH is faster (use Stopwatch). (5) Integration test: run IntentClusterer with LSH on a fixture and compare cluster output to brute-force clustering, allowing tolerance for approximate results (e.g., >= 90% agreement on cluster assignments).
