using AiCodeGraph.Core.Models.CodeGraph;
using Microsoft.Data.Sqlite;

namespace AiCodeGraph.Core.Storage;

public class StorageService : IAsyncDisposable, IDisposable
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

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync(cancellationToken);

        using var pragmaCmd = _connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);

        foreach (var drop in SchemaDefinition.DropTables)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = drop;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var create in SchemaDefinition.CreateTables)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = create;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var index in SchemaDefinition.CreateIndexes)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = index;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
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
                await InsertProject(result.Model, transaction, cancellationToken);
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
        await cmd.ExecuteNonQueryAsync(ct);

        foreach (var ns in project.Namespaces)
            await InsertNamespace(ns, project.Id, transaction, ct);
    }

    private async Task InsertNamespace(NamespaceModel ns, string projectId, SqliteTransaction transaction, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO Namespaces (Id, FullName, ProjectId) VALUES (@id, @name, @pid)";
        cmd.Parameters.AddWithValue("@id", ns.Id);
        cmd.Parameters.AddWithValue("@name", ns.FullName);
        cmd.Parameters.AddWithValue("@pid", projectId);
        await cmd.ExecuteNonQueryAsync(ct);

        foreach (var type in ns.Types)
            await InsertType(type, ns.Id, transaction, ct);

        foreach (var child in ns.ChildNamespaces)
            await InsertNamespace(child, projectId, transaction, ct);
    }

    private async Task InsertType(TypeModel type, string namespaceId, SqliteTransaction transaction, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Types (Id, Name, FullName, Kind, NamespaceId, IsStatic, IsAbstract, IsSealed)
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
        await cmd.ExecuteNonQueryAsync(ct);

        foreach (var iface in type.ImplementedInterfaces)
        {
            using var implCmd = _connection!.CreateCommand();
            implCmd.Transaction = transaction;
            implCmd.CommandText = "INSERT OR IGNORE INTO TypeImplements (TypeId, InterfaceId) VALUES (@tid, @iid)";
            implCmd.Parameters.AddWithValue("@tid", type.Id);
            implCmd.Parameters.AddWithValue("@iid", iface);
            await implCmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var method in type.Methods)
            await InsertMethod(method, type.Id, transaction, ct);

        foreach (var nested in type.NestedTypes)
            await InsertType(nested, namespaceId, transaction, ct);
    }

    private async Task InsertMethod(MethodModel method, string typeId, SqliteTransaction transaction, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Methods (Id, Name, FullName, ReturnType, TypeId, StartLine, EndLine, FilePath, IsStatic, IsAsync, IsVirtual, IsOverride, IsAbstract)
            VALUES (@id, @name, @fullName, @ret, @tid, @start, @end, @path, @isStatic, @isAsync, @isVirtual, @isOverride, @isAbstract)
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
        await cmd.ExecuteNonQueryAsync(ct);
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
                await cmd.ExecuteNonQueryAsync(cancellationToken);
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
            cmd.CommandText = "INSERT OR REPLACE INTO Metrics (MethodId, CognitiveComplexity, LinesOfCode, NestingDepth) VALUES (@id, @cc, @loc, @nd)";
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
                await cmd.ExecuteNonQueryAsync(cancellationToken);
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
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
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
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
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
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
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
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add((reader.GetString(0), reader.GetString(1)));
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
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
