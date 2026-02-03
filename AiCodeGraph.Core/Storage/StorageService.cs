using AiCodeGraph.Core.Analysis;
using AiCodeGraph.Core.Architecture;
using AiCodeGraph.Core.Duplicates;
using AiCodeGraph.Core.Models.CodeGraph;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Core.Storage;

public class StorageService : IStorageService
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public StorageService(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine("ai-code-graph", "graph.db");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_dbPath != ":memory:")
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
            if (dir != null)
                Directory.CreateDirectory(dir);
        }

        _connection = new SqliteConnection($"Data Source={_dbPath};Foreign Keys=False");
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var pragmaCmd = _connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=OFF;";
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var drop in SchemaDefinition.DropTables)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = drop;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var create in SchemaDefinition.CreateTables)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = create;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var index in SchemaDefinition.CreateIndexes)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = index;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveCodeModelAsync(List<ExtractionResult> results, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();

        try
        {
            foreach (var result in results)
            {
                await InsertProject(result.Model, transaction, cancellationToken).ConfigureAwait(false);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task InsertProject(ProjectModel project, SqliteTransaction transaction, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO Projects (Id, Name, FilePath) VALUES (@id, @name, @path)";
        cmd.Parameters.AddWithValue("@id", project.Id);
        cmd.Parameters.AddWithValue("@name", project.Name);
        cmd.Parameters.AddWithValue("@path", project.FilePath);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        foreach (var ns in project.Namespaces)
            await InsertNamespace(ns, project.Id, transaction, ct).ConfigureAwait(false);
    }

    private async Task InsertNamespace(NamespaceModel ns, string projectId, SqliteTransaction transaction, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT OR IGNORE INTO Namespaces (Id, FullName, ProjectId) VALUES (@id, @name, @pid)";
        cmd.Parameters.AddWithValue("@id", ns.Id);
        cmd.Parameters.AddWithValue("@name", ns.FullName);
        cmd.Parameters.AddWithValue("@pid", projectId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        foreach (var type in ns.Types)
            await InsertType(type, ns.Id, transaction, ct).ConfigureAwait(false);

        foreach (var child in ns.ChildNamespaces)
            await InsertNamespace(child, projectId, transaction, ct).ConfigureAwait(false);
    }

    private async Task InsertType(TypeModel type, string namespaceId, SqliteTransaction transaction, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR IGNORE INTO Types (Id, Name, FullName, Kind, NamespaceId, IsStatic, IsAbstract, IsSealed)
            VALUES (@id, @name, @fullName, @kind, @nsId, @isStatic, @isAbstract, @isSealed)
            """;
        cmd.Parameters.AddWithValue("@id", type.Id);
        cmd.Parameters.AddWithValue("@name", type.Name);
        cmd.Parameters.AddWithValue("@fullName", type.FullName);
        cmd.Parameters.AddWithValue("@kind", type.Kind.ToString());
        cmd.Parameters.AddWithValue("@nsId", namespaceId);
        cmd.Parameters.AddWithValue("@isStatic", type.IsStatic ? 1 : 0);
        cmd.Parameters.AddWithValue("@isAbstract", type.IsAbstract ? 1 : 0);
        cmd.Parameters.AddWithValue("@isSealed", type.IsSealed ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        foreach (var iface in type.ImplementedInterfaces)
        {
            using var implCmd = _connection!.CreateCommand();
            implCmd.Transaction = transaction;
            implCmd.CommandText = "INSERT OR IGNORE INTO TypeImplements (TypeId, InterfaceId) VALUES (@tid, @iid)";
            implCmd.Parameters.AddWithValue("@tid", type.Id);
            implCmd.Parameters.AddWithValue("@iid", iface);
            await implCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var method in type.Methods)
            await InsertMethod(method, type.Id, transaction, ct).ConfigureAwait(false);

        foreach (var nested in type.NestedTypes)
            await InsertType(nested, namespaceId, transaction, ct).ConfigureAwait(false);
    }

    private async Task InsertMethod(MethodModel method, string typeId, SqliteTransaction transaction, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR IGNORE INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, FilePath, IsStatic, IsAsync, IsVirtual, IsOverride, IsAbstract, Accessibility)
            VALUES (@id, @name, @fullName, @ret, @tid, @start, @end, @path, @isStatic, @isAsync, @isVirtual, @isOverride, @isAbstract, @accessibility)
            """;
        cmd.Parameters.AddWithValue("@id", method.Id);
        cmd.Parameters.AddWithValue("@name", method.Name);
        cmd.Parameters.AddWithValue("@fullName", method.FullName);
        cmd.Parameters.AddWithValue("@ret", method.ReturnType);
        cmd.Parameters.AddWithValue("@tid", typeId);
        cmd.Parameters.AddWithValue("@start", method.StartLine);
        cmd.Parameters.AddWithValue("@end", method.EndLine);
        cmd.Parameters.AddWithValue("@path", (object?)method.FilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isStatic", method.IsStatic ? 1 : 0);
        cmd.Parameters.AddWithValue("@isAsync", method.IsAsync ? 1 : 0);
        cmd.Parameters.AddWithValue("@isVirtual", method.IsVirtual ? 1 : 0);
        cmd.Parameters.AddWithValue("@isOverride", method.IsOverride ? 1 : 0);
        cmd.Parameters.AddWithValue("@isAbstract", method.IsAbstract ? 1 : 0);
        cmd.Parameters.AddWithValue("@accessibility", method.Accessibility.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveCallGraphAsync(List<(string CallerId, string CalleeId)> calls, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();

        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO MethodCalls (CallerId, CalleeId) VALUES (@caller, @callee)";
            var callerParam = cmd.Parameters.Add("@caller", SqliteType.Text);
            var calleeParam = cmd.Parameters.Add("@callee", SqliteType.Text);

            foreach (var (callerId, calleeId) in calls)
            {
                callerParam.Value = callerId;
                calleeParam.Value = calleeId;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task SaveMetricsAsync(List<(string MethodId, int CognitiveComplexity, int LinesOfCode, int NestingDepth)> metrics, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();

        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES (@id, @cc, @loc, @nd)";
            var idParam = cmd.Parameters.Add("@id", SqliteType.Text);
            var ccParam = cmd.Parameters.Add("@cc", SqliteType.Integer);
            var locParam = cmd.Parameters.Add("@loc", SqliteType.Integer);
            var ndParam = cmd.Parameters.Add("@nd", SqliteType.Integer);

            foreach (var (methodId, cc, loc, nd) in metrics)
            {
                idParam.Value = methodId;
                ccParam.Value = cc;
                locParam.Value = loc;
                ndParam.Value = nd;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<(string Id, string Name, string FullName, string ReturnType, string? FilePath, int StartLine)>> GetHotspotsAsync(int top = 20, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT m.Id, m.Name, m.FullName, m.ReturnType, m.FilePath, m.StartLine, met.CognitiveComplexity
            FROM Methods m JOIN Metrics met ON m.Id = met.MethodId
            ORDER BY met.CognitiveComplexity DESC
            LIMIT @top
            """;
        cmd.Parameters.AddWithValue("@top", top);

        var results = new List<(string, string, string, string, string?, int)>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5)
            ));
        }
        return results;
    }

    public async Task<List<string>> GetCalleesAsync(string methodId, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT CalleeId FROM MethodCalls WHERE CallerId = @id";
        cmd.Parameters.AddWithValue("@id", methodId);

        var results = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(reader.GetString(0));
        return results;
    }

    public async Task<List<string>> GetCallersAsync(string methodId, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT CallerId FROM MethodCalls WHERE CalleeId = @id";
        cmd.Parameters.AddWithValue("@id", methodId);

        var results = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(reader.GetString(0));
        return results;
    }

    public async Task<List<(string Id, string FullName)>> SearchMethodsAsync(string pattern, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT Id, FullName FROM Methods WHERE FullName LIKE @pattern";
        cmd.Parameters.AddWithValue("@pattern", $"%{pattern}%");

        var results = new List<(string, string)>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }

    public async Task SaveEmbeddingsAsync(List<(string MethodId, float[] Vector, string ModelVersion)> embeddings, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();

        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO Embeddings (MethodId, Vector, ModelVersion) VALUES (@id, @vec, @model)";
            var idParam = cmd.Parameters.Add("@id", SqliteType.Text);
            var vecParam = cmd.Parameters.Add("@vec", SqliteType.Blob);
            var modelParam = cmd.Parameters.Add("@model", SqliteType.Text);

            foreach (var (methodId, vector, modelVersion) in embeddings)
            {
                idParam.Value = methodId;
                vecParam.Value = VectorToBytes(vector);
                modelParam.Value = modelVersion;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<(string MethodId, float[] Vector)>> GetEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT MethodId, Vector FROM Embeddings";

        var results = new List<(string, float[])>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetString(0);
            var bytes = (byte[])reader[1];
            results.Add((id, BytesToVector(bytes)));
        }
        return results;
    }

    private static byte[] VectorToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    public async Task SaveNormalizedMethodsAsync(List<(string MethodId, string StructuralSignature, string SemanticPayload)> normalized, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();

        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR IGNORE INTO NormalizedMethods (MethodId, StructuralSignature, SemanticPayload) VALUES (@id, @sig, @payload)";
            var idParam = cmd.Parameters.Add("@id", SqliteType.Text);
            var sigParam = cmd.Parameters.Add("@sig", SqliteType.Text);
            var payloadParam = cmd.Parameters.Add("@payload", SqliteType.Text);

            foreach (var (methodId, sig, payload) in normalized)
            {
                idParam.Value = methodId;
                sigParam.Value = sig;
                payloadParam.Value = payload;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        _connection = new SqliteConnection($"Data Source={_dbPath};Foreign Keys=False");
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<(string Id, string Name, string FullName, string? FilePath, int StartLine)?> GetMethodInfoAsync(string methodId, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, FullName, FilePath, StartLine FROM Methods WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", methodId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)
            );
        }
        return null;
    }

    public async Task<List<(string Id, string Name, string FullName, int Complexity, int Loc, int Nesting, string? FilePath, int StartLine, int BlastRadius, int BlastDepth)>> GetHotspotsWithThresholdAsync(int top = 20, int? threshold = null, string sortBy = "complexity", CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        var where = threshold.HasValue ? "WHERE met.CognitiveComplexity >= @threshold" : "";

        var orderBy = sortBy?.ToLowerInvariant() switch
        {
            "blast-radius" or "blast" => "met.BlastRadius DESC",
            "risk" => "(met.CognitiveComplexity * (1 + log(met.BlastRadius + 1))) DESC",
            _ => "met.CognitiveComplexity DESC"
        };

        cmd.CommandText = $"""
            SELECT m.Id, m.Name, m.FullName, met.CognitiveComplexity, met.LinesOfCode, met.NestingDepth, m.FilePath, m.StartLine, met.BlastRadius, met.BlastDepth
            FROM Methods m JOIN Metrics met ON m.Id = met.MethodId
            {where}
            ORDER BY {orderBy}
            LIMIT @top
            """;
        cmd.Parameters.AddWithValue("@top", top);
        if (threshold.HasValue)
            cmd.Parameters.AddWithValue("@threshold", threshold.Value);

        var results = new List<(string, string, string, int, int, int, string?, int, int, int)>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9)
            ));
        }
        return results;
    }

    public async Task<List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType, string Accessibility)>> GetTreeAsync(string? namespaceFilter = null, string? typeFilter = null, bool includePrivate = false, bool includeConstructors = false, bool skipTests = false, bool skipInterfaces = false, string? excludeNamespaces = null, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        var conditions = new List<string>();
        if (namespaceFilter != null)
            conditions.Add("n.FullName LIKE @ns");
        if (typeFilter != null)
            conditions.Add("t.Name LIKE @type");

        // Always exclude constructors unless explicitly requested
        if (!includeConstructors)
            conditions.Add("m.Name NOT IN ('.ctor', '.cctor')");

        // Filter by accessibility - public only by default
        if (!includePrivate)
            conditions.Add("m.Accessibility = 'Public'");

        // Skip test projects
        if (skipTests)
            conditions.Add("p.Name NOT LIKE '%.Tests' AND p.Name NOT LIKE '%.Test'");

        // Skip interface types
        if (skipInterfaces)
            conditions.Add("t.Kind != 'Interface'");

        // Exclude specific namespaces
        var excludePatterns = new List<string>();
        if (!string.IsNullOrEmpty(excludeNamespaces))
        {
            excludePatterns = excludeNamespaces.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            for (int i = 0; i < excludePatterns.Count; i++)
            {
                conditions.Add($"n.FullName NOT LIKE @exns{i}");
            }
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        cmd.CommandText = $"""
            SELECT p.Name, n.FullName, t.Name, t.Kind, m.Name, m.ReturnType, m.Accessibility
            FROM Projects p
            JOIN Namespaces n ON n.ProjectId = p.Id
            JOIN Types t ON t.NamespaceId = n.Id
            JOIN Methods m ON m.TypeId = t.Id
            {where}
            ORDER BY p.Name, n.FullName, t.Name, m.Name
            """;
        if (namespaceFilter != null)
            cmd.Parameters.AddWithValue("@ns", $"{namespaceFilter}%");
        if (typeFilter != null)
            cmd.Parameters.AddWithValue("@type", $"%{typeFilter}%");
        for (int i = 0; i < excludePatterns.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@exns{i}", $"%{excludePatterns[i]}%");
        }

        var results = new List<(string, string, string, string, string, string, string)>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)
            ));
        }
        return results;
    }

    public async Task SaveClonePairsAsync(List<ClonePair> clonePairs, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();

        try
        {
            // Clear existing
            using (var delCmd = _connection!.CreateCommand())
            {
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM ClonePairs";
                await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using var cmd = _connection!.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO ClonePairs (MethodIdA, MethodIdB, StructuralSimilarity, SemanticSimilarity, HybridScore, CloneType)
                VALUES (@a, @b, @ss, @sem, @hs, @type)
                """;
            var aParam = cmd.Parameters.Add("@a", SqliteType.Text);
            var bParam = cmd.Parameters.Add("@b", SqliteType.Text);
            var ssParam = cmd.Parameters.Add("@ss", SqliteType.Real);
            var semParam = cmd.Parameters.Add("@sem", SqliteType.Real);
            var hsParam = cmd.Parameters.Add("@hs", SqliteType.Real);
            var typeParam = cmd.Parameters.Add("@type", SqliteType.Text);

            foreach (var pair in clonePairs)
            {
                aParam.Value = pair.MethodIdA;
                bParam.Value = pair.MethodIdB;
                ssParam.Value = pair.StructuralSimilarity;
                semParam.Value = pair.SemanticSimilarity;
                hsParam.Value = pair.HybridScore;
                typeParam.Value = pair.Type.ToString();
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<ClonePair>> GetClonePairsAsync(float minThreshold = 0f, CloneType? typeFilter = null, string? conceptFilter = null, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        var conditions = new List<string>();
        if (minThreshold > 0)
            conditions.Add("cp.HybridScore >= @threshold");
        if (typeFilter.HasValue)
            conditions.Add("cp.CloneType = @type");

        string query;
        if (conceptFilter != null)
        {
            conditions.Add("ic.Label LIKE @concept");
            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            query = $"""
                SELECT DISTINCT cp.MethodIdA, cp.MethodIdB, cp.StructuralSimilarity, cp.SemanticSimilarity, cp.HybridScore, cp.CloneType
                FROM ClonePairs cp
                JOIN MethodClusterMap mcm ON mcm.MethodId = cp.MethodIdA OR mcm.MethodId = cp.MethodIdB
                JOIN IntentClusters ic ON ic.Id = mcm.ClusterId
                {where}
                ORDER BY cp.HybridScore DESC
                """;
            cmd.Parameters.AddWithValue("@concept", $"%{conceptFilter}%");
        }
        else
        {
            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            query = $"SELECT cp.MethodIdA, cp.MethodIdB, cp.StructuralSimilarity, cp.SemanticSimilarity, cp.HybridScore, cp.CloneType FROM ClonePairs cp {where} ORDER BY cp.HybridScore DESC";
        }

        cmd.CommandText = query;
        if (minThreshold > 0)
            cmd.Parameters.AddWithValue("@threshold", minThreshold);
        if (typeFilter.HasValue)
            cmd.Parameters.AddWithValue("@type", typeFilter.Value.ToString());

        var results = new List<ClonePair>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ClonePair(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFloat(2),
                reader.GetFloat(3),
                reader.GetFloat(4),
                Enum.Parse<CloneType>(reader.GetString(5))));
        }
        return results;
    }

    public async Task SaveClustersAsync(List<IntentCluster> clusters, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();

        try
        {
            // Clear existing
            using (var delCmd = _connection!.CreateCommand())
            {
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM MethodClusterMap; DELETE FROM IntentClusters;";
                await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using var clusterCmd = _connection!.CreateCommand();
            clusterCmd.Transaction = transaction;
            clusterCmd.CommandText = """
                INSERT INTO IntentClusters (Id, Label, Description, Cohesion, MemberCount)
                VALUES (@id, @label, @desc, @cohesion, @count)
                """;
            var idParam = clusterCmd.Parameters.Add("@id", SqliteType.Text);
            var labelParam = clusterCmd.Parameters.Add("@label", SqliteType.Text);
            var descParam = clusterCmd.Parameters.Add("@desc", SqliteType.Text);
            var cohesionParam = clusterCmd.Parameters.Add("@cohesion", SqliteType.Real);
            var countParam = clusterCmd.Parameters.Add("@count", SqliteType.Integer);

            using var mapCmd = _connection!.CreateCommand();
            mapCmd.Transaction = transaction;
            mapCmd.CommandText = "INSERT INTO MethodClusterMap (MethodId, ClusterId) VALUES (@mid, @cid)";
            var midParam = mapCmd.Parameters.Add("@mid", SqliteType.Text);
            var cidParam = mapCmd.Parameters.Add("@cid", SqliteType.Text);

            foreach (var cluster in clusters)
            {
                idParam.Value = cluster.Id;
                labelParam.Value = cluster.Label;
                descParam.Value = (object?)cluster.Description ?? DBNull.Value;
                cohesionParam.Value = cluster.Cohesion;
                countParam.Value = cluster.MethodIds.Count;
                await clusterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                cidParam.Value = cluster.Id;
                foreach (var methodId in cluster.MethodIds)
                {
                    midParam.Value = methodId;
                    await mapCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task SaveMetadataAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT);
            INSERT OR REPLACE INTO Metadata (Key, Value) VALUES (@key, @value)
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureConnection();

        // Check if Metadata table exists
        using var checkCmd = _connection!.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Metadata'";
        var exists = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
        if (!exists) return null;

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Metadata WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    public async Task SaveLayerAssignmentsAsync(List<LayerAssignment> assignments, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();
        try
        {
            // Clear existing assignments
            using (var clearCmd = _connection.CreateCommand())
            {
                clearCmd.Transaction = transaction;
                clearCmd.CommandText = "DELETE FROM TypeLayers";
                await clearCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // Insert new assignments
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT INTO TypeLayers (TypeId, Layer, Confidence, Reason) VALUES (@typeId, @layer, @confidence, @reason)";
            var typeIdParam = cmd.Parameters.Add("@typeId", SqliteType.Text);
            var layerParam = cmd.Parameters.Add("@layer", SqliteType.Text);
            var confidenceParam = cmd.Parameters.Add("@confidence", SqliteType.Real);
            var reasonParam = cmd.Parameters.Add("@reason", SqliteType.Text);

            foreach (var assignment in assignments)
            {
                typeIdParam.Value = assignment.TypeId;
                layerParam.Value = assignment.Layer.ToString();
                confidenceParam.Value = assignment.Confidence;
                reasonParam.Value = assignment.Reason ?? (object)DBNull.Value;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<LayerAssignment>> GetLayerAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        var assignments = new List<LayerAssignment>();

        // Check if table exists
        using var checkCmd = _connection!.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TypeLayers'";
        var exists = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
        if (!exists) return assignments;

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT TypeId, Layer, Confidence, Reason FROM TypeLayers ORDER BY Layer, Confidence DESC";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var layer = Enum.TryParse<ArchitecturalLayer>(reader.GetString(1), out var l) ? l : ArchitecturalLayer.Unknown;
            assignments.Add(new LayerAssignment(
                reader.GetString(0),
                layer,
                reader.GetFloat(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3)));
        }
        return assignments;
    }

    public async Task<LayerAssignment?> GetLayerForTypeAsync(string typeId, CancellationToken cancellationToken = default)
    {
        EnsureConnection();

        // Check if table exists
        using var checkCmd = _connection!.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TypeLayers'";
        var exists = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
        if (!exists) return null;

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT TypeId, Layer, Confidence, Reason FROM TypeLayers WHERE TypeId = @typeId";
        cmd.Parameters.AddWithValue("@typeId", typeId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var layer = Enum.TryParse<ArchitecturalLayer>(reader.GetString(1), out var l) ? l : ArchitecturalLayer.Unknown;
            return new LayerAssignment(
                reader.GetString(0),
                layer,
                reader.GetFloat(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3));
        }
        return null;
    }

    public async Task SaveBlastRadiusAsync(Dictionary<string, BlastRadiusInfo> blastRadius, CancellationToken cancellationToken = default)
    {
        if (blastRadius.Count == 0) return;

        EnsureConnection();
        using var transaction = _connection!.BeginTransaction();
        try
        {
            // Update blast radius on existing Metrics rows using INSERT OR REPLACE
            // This handles both update and insert cases
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth, BlastRadius, BlastDepth)
                VALUES (@methodId, 0, 0, 0, @blastRadius, @blastDepth)
                ON CONFLICT(MethodId) DO UPDATE SET
                    BlastRadius = @blastRadius,
                    BlastDepth = @blastDepth
                """;

            var methodIdParam = cmd.Parameters.Add("@methodId", SqliteType.Text);
            var blastRadiusParam = cmd.Parameters.Add("@blastRadius", SqliteType.Integer);
            var blastDepthParam = cmd.Parameters.Add("@blastDepth", SqliteType.Integer);

            foreach (var (methodId, info) in blastRadius)
            {
                methodIdParam.Value = methodId;
                blastRadiusParam.Value = info.TransitiveCallers;
                blastDepthParam.Value = info.Depth;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<List<IntentCluster>> GetClustersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnection();

        // Load clusters
        var clusters = new Dictionary<string, (string Label, string? Desc, float Cohesion, int Count)>();
        using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Label, Description, Cohesion, MemberCount FROM IntentClusters ORDER BY MemberCount DESC";
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                clusters[reader.GetString(0)] = (
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetFloat(3),
                    reader.GetInt32(4));
            }
        }

        // Load members
        var members = new Dictionary<string, List<string>>();
        using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT ClusterId, MethodId FROM MethodClusterMap";
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var cid = reader.GetString(0);
                if (!members.ContainsKey(cid))
                    members[cid] = new List<string>();
                members[cid].Add(reader.GetString(1));
            }
        }

        return clusters.Select(kv => new IntentCluster(
            kv.Key,
            kv.Value.Label,
            kv.Value.Desc ?? "",
            members.GetValueOrDefault(kv.Key, new List<string>()),
            kv.Value.Cohesion
        )).ToList();
    }

    public async Task<List<(string Id, string Name, string FullName, string ReturnType, string? FilePath, int StartLine, int Complexity, int Loc, int Nesting, string? ClusterLabel)>> GetMethodsForExportAsync(string? conceptFilter = null, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();

        if (conceptFilter != null)
        {
            cmd.CommandText = """
                SELECT m.Id, m.Name, m.FullName, m.ReturnType, m.FilePath, m.StartLine,
                       COALESCE(met.CognitiveComplexity, 0), COALESCE(met.LinesOfCode, 0), COALESCE(met.NestingDepth, 0),
                       ic.Label
                FROM Methods m
                JOIN MethodClusterMap mcm ON mcm.MethodId = m.Id
                JOIN IntentClusters ic ON ic.Id = mcm.ClusterId
                LEFT JOIN Metrics met ON met.MethodId = m.Id
                WHERE ic.Label LIKE @concept
                ORDER BY m.Id
                """;
            cmd.Parameters.AddWithValue("@concept", $"%{conceptFilter}%");
        }
        else
        {
            cmd.CommandText = """
                SELECT m.Id, m.Name, m.FullName, m.ReturnType, m.FilePath, m.StartLine,
                       COALESCE(met.CognitiveComplexity, 0), COALESCE(met.LinesOfCode, 0), COALESCE(met.NestingDepth, 0),
                       ic.Label
                FROM Methods m
                LEFT JOIN Metrics met ON met.MethodId = m.Id
                LEFT JOIN MethodClusterMap mcm ON mcm.MethodId = m.Id
                LEFT JOIN IntentClusters ic ON ic.Id = mcm.ClusterId
                ORDER BY m.Id
                """;
        }

        var results = new List<(string, string, string, string, string?, int, int, int, int, string?)>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }
        return results;
    }

    public async Task<List<(string CallerId, string CalleeId)>> GetCallGraphForMethodsAsync(HashSet<string> methodIds, CancellationToken cancellationToken = default)
    {
        EnsureConnection();

        if (methodIds.Count == 0)
            return new List<(string, string)>();

        var seen = new HashSet<(string, string)>();
        var results = new List<(string, string)>();
        var idList = methodIds.ToList();
        const int chunkSize = 450; // 450 * 2 = 900 params (under SQLite's 999 limit)

        for (int i = 0; i < idList.Count; i += chunkSize)
        {
            var chunk = idList.GetRange(i, Math.Min(chunkSize, idList.Count - i));
            using var cmd = _connection!.CreateCommand();

            var callerParams = string.Join(",", chunk.Select((_, idx) => $"@c{idx}"));
            var calleeParams = string.Join(",", chunk.Select((_, idx) => $"@e{idx}"));
            cmd.CommandText = $"SELECT CallerId, CalleeId FROM MethodCalls WHERE CallerId IN ({callerParams}) OR CalleeId IN ({calleeParams})";

            for (int j = 0; j < chunk.Count; j++)
            {
                cmd.Parameters.AddWithValue($"@c{j}", chunk[j]);
                cmd.Parameters.AddWithValue($"@e{j}", chunk[j]);
            }

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var pair = (reader.GetString(0), reader.GetString(1));
                if (seen.Add(pair))
                    results.Add(pair);
            }
        }

        return results;
    }

    public async Task<(int CognitiveComplexity, int LinesOfCode, int NestingDepth, int BlastRadius, int BlastDepth)?> GetMethodMetricsAsync(string methodId, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT CognitiveComplexity, LinesOfCode, NestingDepth, BlastRadius, BlastDepth FROM Metrics WHERE MethodId = @id";
        cmd.Parameters.AddWithValue("@id", methodId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
        return null;
    }

    public async Task<(string Label, int MemberCount, float Cohesion)?> GetMethodClusterAsync(string methodId, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT ic.Label, ic.MemberCount, ic.Cohesion
            FROM MethodClusterMap mcm JOIN IntentClusters ic ON mcm.ClusterId = ic.Id
            WHERE mcm.MethodId = @id
            """;
        cmd.Parameters.AddWithValue("@id", methodId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (reader.GetString(0), reader.GetInt32(1), reader.GetFloat(2));
        return null;
    }

    public async Task<List<(string OtherMethodId, string OtherFullName, float HybridScore, CloneType Type)>> GetMethodDuplicatesAsync(string methodId, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT cp.MethodIdA, cp.MethodIdB, cp.HybridScore, cp.CloneType, ma.FullName, mb.FullName
            FROM ClonePairs cp
            LEFT JOIN Methods ma ON cp.MethodIdA = ma.Id
            LEFT JOIN Methods mb ON cp.MethodIdB = mb.Id
            WHERE cp.MethodIdA = @id OR cp.MethodIdB = @id
            ORDER BY cp.HybridScore DESC
            """;
        cmd.Parameters.AddWithValue("@id", methodId);
        var results = new List<(string, string, float, CloneType)>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var idA = reader.GetString(0);
            var idB = reader.GetString(1);
            var score = reader.GetFloat(2);
            var type = Enum.Parse<CloneType>(reader.GetString(3));
            var nameA = reader.IsDBNull(4) ? idA : reader.GetString(4);
            var nameB = reader.IsDBNull(5) ? idB : reader.GetString(5);
            var otherId = idA == methodId ? idB : idA;
            var otherName = idA == methodId ? nameB : nameA;
            results.Add((otherId, otherName, score, type));
        }
        return results;
    }

    public async Task<List<(string Id, string FullName, string? FilePath, int StartLine, int Complexity)>> GetDeadCodeAsync(bool includeOverrides = false, CancellationToken cancellationToken = default)
    {
        EnsureConnection();
        using var cmd = _connection!.CreateCommand();

        cmd.CommandText = """
            SELECT m.Id, m.FullName, m.FilePath, m.StartLine, COALESCE(met.CognitiveComplexity, 0)
            FROM Methods m
            LEFT JOIN MethodCalls mc ON m.Id = mc.CalleeId
            LEFT JOIN Metrics met ON met.MethodId = m.Id
            JOIN Types t ON t.Id = m.TypeId
            JOIN Namespaces n ON n.Id = t.NamespaceId
            WHERE mc.CallerId IS NULL
              AND m.Name NOT IN ('.ctor', '.cctor', 'Main', 'Dispose', 'DisposeAsync')
              AND m.Name NOT LIKE '%Test%'
              AND m.Name NOT LIKE '%_Test'
              AND t.Name NOT LIKE '%Tests'
              AND t.Name NOT LIKE '%Test'
              AND n.FullName NOT LIKE '%Tests%'
            """;

        if (!includeOverrides)
            cmd.CommandText += "\n  AND m.IsOverride = 0 AND m.IsAbstract = 0";

        cmd.CommandText += "\n  ORDER BY COALESCE(met.CognitiveComplexity, 0) DESC";

        var results = new List<(string, string, string?, int, int)>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }
        return results;
    }

    private void EnsureConnection()
    {
        if (_connection == null)
            throw new InvalidOperationException("Storage not initialized. Call InitializeAsync first.");
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
