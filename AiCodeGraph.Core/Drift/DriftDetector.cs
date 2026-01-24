using AiCodeGraph.Core.Duplicates;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Core.Drift;

public class DriftDetectorOptions
{
    public double ComplexityPercentageThreshold { get; set; } = 0.25;
    public int ComplexityAbsoluteThreshold { get; set; } = 15;
}

public class DriftDetector
{
    private readonly DriftDetectorOptions _options;

    public DriftDetector(DriftDetectorOptions? options = null)
    {
        _options = options ?? new DriftDetectorOptions();
    }

    public async Task<DriftReport> CompareAsync(string currentDbPath, string baselineDbPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(currentDbPath))
            throw new FileNotFoundException("Current database not found.", currentDbPath);
        if (!File.Exists(baselineDbPath))
            throw new FileNotFoundException("Baseline database not found.", baselineDbPath);

        SqliteConnection? currentConn = null;
        SqliteConnection? baselineConn = null;
        try
        {
            currentConn = new SqliteConnection($"Data Source={currentDbPath}");
            baselineConn = new SqliteConnection($"Data Source={baselineDbPath}");

            await currentConn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await baselineConn.OpenAsync(cancellationToken).ConfigureAwait(false);

            var newMethods = await DetectNewMethods(currentConn, baselineConn, cancellationToken).ConfigureAwait(false);
            var removedMethods = await DetectRemovedMethods(currentConn, baselineConn, cancellationToken).ConfigureAwait(false);
            var regressions = await DetectComplexityRegressions(currentConn, baselineConn, cancellationToken).ConfigureAwait(false);
            var newDuplicates = await DetectNewDuplicates(currentConn, baselineConn, cancellationToken).ConfigureAwait(false);
            var scattering = await DetectIntentScattering(currentConn, baselineConn, cancellationToken).ConfigureAwait(false);

            return new DriftReport(newMethods, removedMethods, regressions, newDuplicates, scattering);
        }
        finally
        {
            if (baselineConn != null) await baselineConn.DisposeAsync().ConfigureAwait(false);
            if (currentConn != null) await currentConn.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<List<MethodDiff>> DetectNewMethods(SqliteConnection current, SqliteConnection baseline, CancellationToken ct)
    {
        var baselineIds = await GetMethodIds(baseline, ct).ConfigureAwait(false);
        var currentMethods = await GetMethodDetails(current, ct).ConfigureAwait(false);

        return currentMethods
            .Where(m => !baselineIds.Contains(m.MethodId))
            .OrderBy(m => m.FullName)
            .ToList();
    }

    private static async Task<List<MethodDiff>> DetectRemovedMethods(SqliteConnection current, SqliteConnection baseline, CancellationToken ct)
    {
        var currentIds = await GetMethodIds(current, ct).ConfigureAwait(false);
        var baselineMethods = await GetMethodDetails(baseline, ct).ConfigureAwait(false);

        return baselineMethods
            .Where(m => !currentIds.Contains(m.MethodId))
            .OrderBy(m => m.FullName)
            .ToList();
    }

    private async Task<List<ComplexityRegression>> DetectComplexityRegressions(SqliteConnection current, SqliteConnection baseline, CancellationToken ct)
    {
        var currentMetrics = await GetMetrics(current, ct).ConfigureAwait(false);
        var baselineMetrics = await GetMetrics(baseline, ct).ConfigureAwait(false);

        var regressions = new List<ComplexityRegression>();

        foreach (var (methodId, currentComplexity) in currentMetrics)
        {
            if (!baselineMetrics.TryGetValue(methodId, out var baselineComplexity))
                continue;

            if (currentComplexity <= baselineComplexity)
                continue; // improvement, not regression

            var percentIncrease = baselineComplexity > 0
                ? (double)(currentComplexity - baselineComplexity) / baselineComplexity
                : 1.0;

            var crossedThreshold = baselineComplexity <= _options.ComplexityAbsoluteThreshold
                && currentComplexity > _options.ComplexityAbsoluteThreshold;

            if (percentIncrease >= _options.ComplexityPercentageThreshold || crossedThreshold)
            {
                var fullName = await GetMethodFullName(current, methodId, ct).ConfigureAwait(false);
                regressions.Add(new ComplexityRegression(
                    methodId,
                    fullName ?? methodId,
                    baselineComplexity,
                    currentComplexity,
                    percentIncrease,
                    crossedThreshold));
            }
        }

        return regressions.OrderByDescending(r => r.PercentageIncrease).ToList();
    }

    private static async Task<List<ClonePair>> DetectNewDuplicates(SqliteConnection current, SqliteConnection baseline, CancellationToken ct)
    {
        var baselinePairs = await GetClonePairKeys(baseline, ct).ConfigureAwait(false);
        var currentPairs = await GetClonePairs(current, ct).ConfigureAwait(false);

        return currentPairs
            .Where(p => !baselinePairs.Contains((p.MethodIdA, p.MethodIdB)))
            .OrderByDescending(p => p.HybridScore)
            .ToList();
    }

    private async Task<List<ScatteringAlert>> DetectIntentScattering(SqliteConnection current, SqliteConnection baseline, CancellationToken ct)
    {
        var baselineClusters = await GetClusterNamespaces(baseline, ct).ConfigureAwait(false);
        var currentClusters = await GetClusterNamespaces(current, ct).ConfigureAwait(false);

        var alerts = new List<ScatteringAlert>();

        foreach (var (label, currentInfo) in currentClusters)
        {
            if (!baselineClusters.TryGetValue(label, out var baselineInfo))
                continue; // New cluster, not scattering

            var newNamespaces = currentInfo.Namespaces.Except(baselineInfo.Namespaces).ToList();
            if (newNamespaces.Count == 0)
                continue;

            var newMembers = currentInfo.Methods.Except(baselineInfo.Methods).ToList();

            alerts.Add(new ScatteringAlert(
                label,
                baselineInfo.Namespaces.ToList(),
                newNamespaces,
                newMembers,
                currentInfo.Methods.Count));
        }

        return alerts.OrderByDescending(a => a.NewNamespaces.Count).ToList();
    }

    private static async Task<HashSet<string>> GetMethodIds(SqliteConnection conn, CancellationToken ct)
    {
        var ids = new HashSet<string>();
        if (!await TableExists(conn, "Methods", ct).ConfigureAwait(false)) return ids;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Methods";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            ids.Add(reader.GetString(0));
        return ids;
    }

    private static async Task<List<MethodDiff>> GetMethodDetails(SqliteConnection conn, CancellationToken ct)
    {
        var methods = new List<MethodDiff>();
        if (!await TableExists(conn, "Methods", ct).ConfigureAwait(false)) return methods;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.Id, m.FullName, n.FullName, m.FilePath
            FROM Methods m
            JOIN Types t ON t.Id = m.TypeId
            JOIN Namespaces n ON n.Id = t.NamespaceId
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            methods.Add(new MethodDiff(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return methods;
    }

    private static async Task<Dictionary<string, int>> GetMetrics(SqliteConnection conn, CancellationToken ct)
    {
        var metrics = new Dictionary<string, int>();
        if (!await TableExists(conn, "Metrics", ct).ConfigureAwait(false)) return metrics;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MethodId, CognitiveComplexity FROM Metrics";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            metrics[reader.GetString(0)] = reader.GetInt32(1);
        return metrics;
    }

    private static async Task<string?> GetMethodFullName(SqliteConnection conn, string methodId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FullName FROM Methods WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", methodId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result as string;
    }

    private static async Task<HashSet<(string, string)>> GetClonePairKeys(SqliteConnection conn, CancellationToken ct)
    {
        var keys = new HashSet<(string, string)>();
        if (!await TableExists(conn, "ClonePairs", ct).ConfigureAwait(false)) return keys;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MethodIdA, MethodIdB FROM ClonePairs";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            keys.Add((reader.GetString(0), reader.GetString(1)));
        return keys;
    }

    private static async Task<List<ClonePair>> GetClonePairs(SqliteConnection conn, CancellationToken ct)
    {
        var pairs = new List<ClonePair>();
        if (!await TableExists(conn, "ClonePairs", ct).ConfigureAwait(false)) return pairs;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MethodIdA, MethodIdB, StructuralSimilarity, SemanticSimilarity, HybridScore, CloneType FROM ClonePairs";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            pairs.Add(new ClonePair(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFloat(2),
                reader.GetFloat(3),
                reader.GetFloat(4),
                Enum.Parse<CloneType>(reader.GetString(5))));
        }
        return pairs;
    }

    private static async Task<Dictionary<string, (List<string> Namespaces, List<string> Methods)>> GetClusterNamespaces(SqliteConnection conn, CancellationToken ct)
    {
        var result = new Dictionary<string, (List<string> Namespaces, List<string> Methods)>();
        if (!await TableExists(conn, "IntentClusters", ct).ConfigureAwait(false)) return result;
        if (!await TableExists(conn, "MethodClusterMap", ct).ConfigureAwait(false)) return result;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ic.Label, n.FullName, mcm.MethodId
            FROM IntentClusters ic
            JOIN MethodClusterMap mcm ON mcm.ClusterId = ic.Id
            JOIN Methods m ON m.Id = mcm.MethodId
            JOIN Types t ON t.Id = m.TypeId
            JOIN Namespaces n ON n.Id = t.NamespaceId
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var label = reader.GetString(0);
            var ns = reader.GetString(1);
            var methodId = reader.GetString(2);

            if (!result.ContainsKey(label))
                result[label] = (new List<string>(), new List<string>());

            var info = result[label];
            if (!info.Namespaces.Contains(ns))
                info.Namespaces.Add(ns);
            info.Methods.Add(methodId);
        }
        return result;
    }

    private static async Task<bool> TableExists(SqliteConnection conn, string tableName, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result) > 0;
    }
}
