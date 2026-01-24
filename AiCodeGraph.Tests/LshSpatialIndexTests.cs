using AiCodeGraph.Core.Embeddings;

namespace AiCodeGraph.Tests;

public class LshSpatialIndexTests
{
    [Fact]
    public void BuildIndex_WithVectors_DoesNotThrow()
    {
        var index = new LshSpatialIndex(dimensions: 10, seed: 42);
        var vectors = GenerateRandomVectors(50, 10, seed: 1);
        index.BuildIndex(vectors);
    }

    [Fact]
    public void GetCandidateNeighbors_ReturnsNonEmpty_ForSimilarVectors()
    {
        var index = new LshSpatialIndex(dimensions: 10, numHashFunctions: 16, numBands: 4, seed: 42);
        var baseVector = new float[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var similarVector = new float[] { 0.9f, 0.1f, 0, 0, 0, 0, 0, 0, 0, 0 };
        var vectors = new List<float[]> { baseVector, similarVector };

        index.BuildIndex(vectors);
        var candidates = index.GetCandidateNeighbors(0);

        // The similar vector should be a candidate
        Assert.Contains(1, candidates);
    }

    [Fact]
    public void GetCandidateNeighbors_IncludesSelf()
    {
        var index = new LshSpatialIndex(dimensions: 10, seed: 42);
        var vectors = GenerateRandomVectors(10, 10, seed: 1);
        index.BuildIndex(vectors);

        var candidates = index.GetCandidateNeighbors(0);
        Assert.Contains(0, candidates);
    }

    [Fact]
    public void GetCandidateNeighbors_WithQuery_Works()
    {
        var index = new LshSpatialIndex(dimensions: 10, numHashFunctions: 16, numBands: 4, seed: 42);
        var vectors = GenerateRandomVectors(20, 10, seed: 1);
        index.BuildIndex(vectors);

        // Query with the same vector as index 0 - should find index 0 as candidate
        var candidates = index.GetCandidateNeighbors(vectors[0]);
        Assert.Contains(0, candidates);
    }

    [Fact]
    public void GetCandidateNeighbors_ThrowsWithoutBuild()
    {
        var index = new LshSpatialIndex(dimensions: 10, seed: 42);
        Assert.Throws<InvalidOperationException>(() => index.GetCandidateNeighbors(0));
    }

    [Fact]
    public void DeterministicWithSeed()
    {
        var vectors = GenerateRandomVectors(50, 10, seed: 1);

        var index1 = new LshSpatialIndex(dimensions: 10, seed: 42);
        index1.BuildIndex(vectors);
        var candidates1 = index1.GetCandidateNeighbors(0);

        var index2 = new LshSpatialIndex(dimensions: 10, seed: 42);
        index2.BuildIndex(vectors);
        var candidates2 = index2.GetCandidateNeighbors(0);

        Assert.True(candidates1.SetEquals(candidates2));
    }

    [Fact]
    public void SingleVector_ReturnsSelf()
    {
        var index = new LshSpatialIndex(dimensions: 10, seed: 42);
        var vectors = new List<float[]> { new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 } };
        index.BuildIndex(vectors);

        var candidates = index.GetCandidateNeighbors(0);
        Assert.Contains(0, candidates);
    }

    [Fact]
    public void IdenticalVectors_AllAreCandidates()
    {
        var index = new LshSpatialIndex(dimensions: 10, numHashFunctions: 16, numBands: 4, seed: 42);
        var vec = new float[] { 1, 0, 1, 0, 1, 0, 1, 0, 1, 0 };
        var vectors = Enumerable.Range(0, 5).Select(_ => (float[])vec.Clone()).ToList();
        index.BuildIndex(vectors);

        var candidates = index.GetCandidateNeighbors(0);
        // All identical vectors should hash to same buckets
        for (int i = 0; i < 5; i++)
            Assert.Contains(i, candidates);
    }

    [Fact]
    public void IntentClusterer_StillProducesClusters_WithLsh()
    {
        // Verify that the clusterer still works with LSH integration
        var rng = new Random(42);
        var cluster1Vectors = Enumerable.Range(0, 10).Select(_ =>
        {
            var v = new float[384];
            for (int i = 0; i < 50; i++) v[i] = 1f + (float)(rng.NextDouble() * 0.1);
            return v;
        }).ToList();

        var cluster2Vectors = Enumerable.Range(0, 10).Select(_ =>
        {
            var v = new float[384];
            for (int i = 200; i < 250; i++) v[i] = 1f + (float)(rng.NextDouble() * 0.1);
            return v;
        }).ToList();

        var embeddings = new List<(string MethodId, float[] Vector)>();
        var methods = new List<AiCodeGraph.Core.Normalization.NormalizedMethod>();

        for (int i = 0; i < cluster1Vectors.Count; i++)
        {
            var id = $"method-a{i}";
            embeddings.Add((id, cluster1Vectors[i]));
            methods.Add(new AiCodeGraph.Core.Normalization.NormalizedMethod(id, "sig", $"process data handler execute {i}"));
        }
        for (int i = 0; i < cluster2Vectors.Count; i++)
        {
            var id = $"method-b{i}";
            embeddings.Add((id, cluster2Vectors[i]));
            methods.Add(new AiCodeGraph.Core.Normalization.NormalizedMethod(id, "sig", $"validate input check verify {i}"));
        }

        var clusterer = new AiCodeGraph.Core.Duplicates.IntentClusterer(epsilon: 0.3f, minPoints: 3);
        var clusters = clusterer.ClusterMethods(methods, embeddings);

        // Should produce at least one cluster (the two groups are very different)
        Assert.True(clusters.Count >= 1);
    }

    [Fact]
    public void HighDimensionalVectors_384Dimensions()
    {
        var index = new LshSpatialIndex(dimensions: 384, numHashFunctions: 32, numBands: 8, seed: 42);
        var vectors = GenerateRandomVectors(100, 384, seed: 1);
        index.BuildIndex(vectors);

        // Should not throw and should return candidates
        var candidates = index.GetCandidateNeighbors(0);
        Assert.NotEmpty(candidates);
    }

    private static List<float[]> GenerateRandomVectors(int count, int dimensions, int seed)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, count).Select(_ =>
        {
            var v = new float[dimensions];
            for (int i = 0; i < dimensions; i++)
                v[i] = (float)(rng.NextDouble() * 2 - 1);
            return v;
        }).ToList();
    }
}
