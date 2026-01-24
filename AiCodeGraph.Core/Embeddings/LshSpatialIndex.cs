namespace AiCodeGraph.Core.Embeddings;

public class LshSpatialIndex
{
    private readonly int _numHashFunctions;
    private readonly int _numBands;
    private readonly int _dimensions;
    private readonly float[][] _randomProjections;
    private readonly Dictionary<int, List<int>> _buckets;
    private List<float[]>? _vectors;

    public LshSpatialIndex(int dimensions = 384, int numHashFunctions = 32, int numBands = 8, int? seed = null)
    {
        _dimensions = dimensions;
        _numHashFunctions = numHashFunctions;
        _numBands = numBands;
        _buckets = new Dictionary<int, List<int>>();

        var rng = seed.HasValue ? new Random(seed.Value) : new Random(42);
        _randomProjections = new float[numHashFunctions][];
        for (int i = 0; i < numHashFunctions; i++)
        {
            _randomProjections[i] = new float[dimensions];
            for (int j = 0; j < dimensions; j++)
                _randomProjections[i][j] = (float)NextGaussian(rng);
        }
    }

    public void BuildIndex(List<float[]> vectors)
    {
        _vectors = vectors;
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

    public HashSet<int> GetCandidateNeighbors(int pointIdx)
    {
        if (_vectors == null)
            throw new InvalidOperationException("Call BuildIndex first.");

        return GetCandidateNeighbors(_vectors[pointIdx]);
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
        var dims = Math.Min(_dimensions, vector.Length);

        for (int i = 0; i < _numHashFunctions; i++)
        {
            float dot = 0;
            for (int j = 0; j < dims; j++)
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
            hash.Add(b);
            for (int i = b * bandSize; i < (b + 1) * bandSize; i++)
                hash.Add(hashes[i]);
            results.Add(hash.ToHashCode());
        }

        return results;
    }

    private static double NextGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
