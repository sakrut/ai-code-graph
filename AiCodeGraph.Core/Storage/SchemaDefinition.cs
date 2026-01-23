namespace AiCodeGraph.Core.Storage;

internal static class SchemaDefinition
{
    internal static readonly string[] DropTables =
    [
        "DROP TABLE IF EXISTS MethodClusterMap;",
        "DROP TABLE IF EXISTS IntentClusters;",
        "DROP TABLE IF EXISTS Embeddings;",
        "DROP TABLE IF EXISTS NormalizedMethods;",
        "DROP TABLE IF EXISTS Metrics;",
        "DROP TABLE IF EXISTS TypeImplements;",
        "DROP TABLE IF EXISTS MethodCalls;",
        "DROP TABLE IF EXISTS Methods;",
        "DROP TABLE IF EXISTS Types;",
        "DROP TABLE IF EXISTS Namespaces;",
        "DROP TABLE IF EXISTS Projects;"
    ];

    internal static readonly string[] CreateTables =
    [
        """
        CREATE TABLE Projects (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            FilePath TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE Namespaces (
            Id TEXT PRIMARY KEY,
            FullName TEXT NOT NULL,
            ProjectId TEXT NOT NULL REFERENCES Projects(Id)
        );
        """,
        """
        CREATE TABLE Types (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            FullName TEXT NOT NULL,
            Kind TEXT NOT NULL,
            NamespaceId TEXT NOT NULL REFERENCES Namespaces(Id),
            IsStatic INTEGER NOT NULL DEFAULT 0,
            IsAbstract INTEGER NOT NULL DEFAULT 0,
            IsSealed INTEGER NOT NULL DEFAULT 0
        );
        """,
        """
        CREATE TABLE Methods (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            FullName TEXT NOT NULL,
            ReturnType TEXT NOT NULL,
            TypeId TEXT NOT NULL REFERENCES Types(Id),
            StartLine INTEGER NOT NULL DEFAULT 0,
            EndLine INTEGER NOT NULL DEFAULT 0,
            FilePath TEXT,
            IsStatic INTEGER NOT NULL DEFAULT 0,
            IsAsync INTEGER NOT NULL DEFAULT 0,
            IsVirtual INTEGER NOT NULL DEFAULT 0,
            IsOverride INTEGER NOT NULL DEFAULT 0,
            IsAbstract INTEGER NOT NULL DEFAULT 0
        );
        """,
        """
        CREATE TABLE MethodCalls (
            CallerId TEXT NOT NULL REFERENCES Methods(Id),
            CalleeId TEXT NOT NULL,
            PRIMARY KEY (CallerId, CalleeId)
        );
        """,
        """
        CREATE TABLE TypeImplements (
            TypeId TEXT NOT NULL REFERENCES Types(Id),
            InterfaceId TEXT NOT NULL,
            PRIMARY KEY (TypeId, InterfaceId)
        );
        """,
        """
        CREATE TABLE Metrics (
            MethodId TEXT PRIMARY KEY REFERENCES Methods(Id),
            CognitiveComplexity INTEGER NOT NULL DEFAULT 0,
            LinesOfCode INTEGER NOT NULL DEFAULT 0,
            NestingDepth INTEGER NOT NULL DEFAULT 0
        );
        """,
        """
        CREATE TABLE NormalizedMethods (
            MethodId TEXT PRIMARY KEY REFERENCES Methods(Id),
            NormalizedSource TEXT,
            TokenHash TEXT
        );
        """,
        """
        CREATE TABLE Embeddings (
            MethodId TEXT PRIMARY KEY REFERENCES Methods(Id),
            Vector BLOB,
            ModelVersion TEXT
        );
        """,
        """
        CREATE TABLE IntentClusters (
            Id TEXT PRIMARY KEY,
            Label TEXT NOT NULL,
            Description TEXT
        );
        """,
        """
        CREATE TABLE MethodClusterMap (
            MethodId TEXT NOT NULL REFERENCES Methods(Id),
            ClusterId TEXT NOT NULL REFERENCES IntentClusters(Id),
            Score REAL NOT NULL DEFAULT 0.0,
            PRIMARY KEY (MethodId, ClusterId)
        );
        """
    ];

    internal static readonly string[] CreateIndexes =
    [
        "CREATE INDEX IX_Methods_FullName ON Methods(FullName);",
        "CREATE INDEX IX_Methods_TypeId ON Methods(TypeId);",
        "CREATE INDEX IX_Types_FullName ON Types(FullName);",
        "CREATE INDEX IX_Types_NamespaceId ON Types(NamespaceId);",
        "CREATE INDEX IX_Namespaces_ProjectId ON Namespaces(ProjectId);",
        "CREATE INDEX IX_Metrics_CognitiveComplexity ON Metrics(CognitiveComplexity DESC);",
        "CREATE INDEX IX_MethodCalls_CalleeId ON MethodCalls(CalleeId);",
        "CREATE INDEX IX_NormalizedMethods_TokenHash ON NormalizedMethods(TokenHash);"
    ];
}
