using System.Numerics;

namespace AiCodeGraph.Core.Embeddings;

public class VectorIndex
{
    private readonly List<(string Id, float[] Vector)> _items = new();
    private int _dimensions;

    public int Count => _items.Count;

    public void BuildIndex(List<(string Id, float[] Vector)> items)
    {
        _items.Clear();
        if (items.Count == 0) return;

        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item.Vector, nameof(item.Vector));
            ValidateVector(item.Vector);
        }

        _dimensions = items[0].Vector.Length;
        foreach (var item in items)
        {
            if (item.Vector.Length != _dimensions)
                throw new ArgumentException($"Vector dimension mismatch: expected {_dimensions}, got {item.Vector.Length}");
            _items.Add((item.Id, Normalize(item.Vector)));
        }
    }

    public void AddItem(string id, float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector, nameof(vector));
        ValidateVector(vector);

        if (_items.Count > 0 && vector.Length != _dimensions)
            throw new ArgumentException($"Vector dimension mismatch: expected {_dimensions}, got {vector.Length}");

        if (_items.Count == 0)
            _dimensions = vector.Length;

        _items.Add((id, Normalize(vector)));
    }

    public List<(string Id, float Score)> Search(float[] query, int topK = 10)
    {
        ArgumentNullException.ThrowIfNull(query, nameof(query));
        ValidateVector(query);

        if (_items.Count == 0)
            return new List<(string, float)>();

        var normalizedQuery = Normalize(query);
        var scores = new List<(string Id, float Score)>(_items.Count);

        foreach (var (id, vector) in _items)
        {
            var similarity = DotProduct(normalizedQuery, vector);
            scores.Add((id, similarity));
        }

        return scores
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
    }

    public void SaveToDisk(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        // Header: magic, version, dimensions, count
        writer.Write(0x56454349); // "VECI" magic bytes
        writer.Write(1);          // version
        writer.Write(_dimensions);
        writer.Write(_items.Count);

        foreach (var (id, vector) in _items)
        {
            writer.Write(id);
            foreach (var val in vector)
                writer.Write(val);
        }
    }

    public void LoadFromDisk(string path)
    {
        _items.Clear();

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadInt32();
        if (magic != 0x56454349)
            throw new InvalidDataException("Invalid vector index file format.");

        var version = reader.ReadInt32();
        _dimensions = reader.ReadInt32();
        var count = reader.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            var id = reader.ReadString();
            var vector = new float[_dimensions];
            for (int j = 0; j < _dimensions; j++)
                vector[j] = reader.ReadSingle();
            _items.Add((id, vector));
        }
    }

    private static float DotProduct(float[] a, float[] b)
    {
        float sum = 0;
        int i = 0;

        // Use SIMD if available
        int vectorSize = Vector<float>.Count;
        int simdEnd = a.Length - (a.Length % vectorSize);

        for (; i < simdEnd; i += vectorSize)
        {
            var va = new Vector<float>(a, i);
            var vb = new Vector<float>(b, i);
            sum += Vector.Dot(va, vb);
        }

        for (; i < a.Length; i++)
            sum += a[i] * b[i];

        return sum;
    }

    private static void ValidateVector(float[] vector)
    {
        for (int i = 0; i < vector.Length; i++)
        {
            if (float.IsNaN(vector[i]))
                throw new ArgumentException($"Vector contains NaN at index {i}");
            if (float.IsInfinity(vector[i]))
                throw new ArgumentException($"Vector contains Infinity at index {i}");
        }
    }

    private static float[] Normalize(float[] vector)
    {
        float magnitude = 0;
        for (int i = 0; i < vector.Length; i++)
            magnitude += vector[i] * vector[i];

        magnitude = MathF.Sqrt(magnitude);
        if (magnitude == 0) return vector;

        var normalized = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
            normalized[i] = vector[i] / magnitude;
        return normalized;
    }
}
