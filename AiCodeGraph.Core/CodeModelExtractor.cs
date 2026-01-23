using AiCodeGraph.Core.Models.CodeGraph;
using Microsoft.CodeAnalysis;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;
using ModelTypeKind = AiCodeGraph.Core.Models.CodeGraph.TypeKind;

namespace AiCodeGraph.Core;

public class CodeModelExtractor
{
    public ExtractionResult ExtractProject(Compilation compilation, string projectName, string projectFilePath)
    {
        var namespaces = WalkNamespace(compilation.GlobalNamespace);
        var projectId = $"project:{projectName}";
        var model = new ProjectModel(projectId, projectName, projectFilePath, namespaces);

        var relationships = BuildRelationships(model);
        return new ExtractionResult(model, relationships);
    }

    private List<NamespaceModel> WalkNamespace(INamespaceSymbol ns)
    {
        var result = new List<NamespaceModel>();

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            var types = ExtractTypes(childNs);
            var childNamespaces = WalkNamespace(childNs);

            if (types.Count > 0 || childNamespaces.Count > 0)
            {
                var nsId = $"ns:{childNs.ToDisplayString()}";
                result.Add(new NamespaceModel(nsId, childNs.ToDisplayString(), types, childNamespaces));
            }
        }

        return result;
    }

    private List<TypeModel> ExtractTypes(INamespaceSymbol ns)
    {
        var types = new List<TypeModel>();
        foreach (var type in ns.GetTypeMembers())
        {
            if (!type.Locations.Any(l => l.IsInSource))
                continue;

            types.Add(ExtractType(type));
        }
        return types;
    }

    private TypeModel ExtractType(INamedTypeSymbol typeSymbol)
    {
        var methods = new List<MethodModel>();
        foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.IsImplicitlyDeclared)
                continue;
            if (!member.CanBeReferencedByName && member.MethodKind != MethodKind.Constructor)
                continue;
            if (member.MethodKind == MethodKind.PropertyGet || member.MethodKind == MethodKind.PropertySet)
                continue;
            if (member.MethodKind == MethodKind.EventAdd || member.MethodKind == MethodKind.EventRemove)
                continue;

            methods.Add(ExtractMethod(member));
        }

        var nestedTypes = new List<TypeModel>();
        foreach (var nested in typeSymbol.GetTypeMembers())
        {
            if (nested.Locations.Any(l => l.IsInSource))
                nestedTypes.Add(ExtractType(nested));
        }

        var interfaces = typeSymbol.Interfaces
            .Select(i => SymbolIdGenerator.GenerateDisplayString(i))
            .ToList();

        var typeParameters = typeSymbol.TypeParameters
            .Select(tp => tp.Name)
            .ToList();

        return new TypeModel(
            Id: SymbolIdGenerator.GenerateId(typeSymbol),
            Name: typeSymbol.Name,
            FullName: SymbolIdGenerator.GenerateDisplayString(typeSymbol),
            Kind: MapTypeKind(typeSymbol),
            Methods: methods,
            ImplementedInterfaces: interfaces,
            Accessibility: typeSymbol.DeclaredAccessibility,
            IsStatic: typeSymbol.IsStatic,
            IsAbstract: typeSymbol.IsAbstract,
            IsSealed: typeSymbol.IsSealed,
            IsGeneric: typeSymbol.IsGenericType,
            TypeParameters: typeParameters,
            NestedTypes: nestedTypes
        );
    }

    private MethodModel ExtractMethod(IMethodSymbol method)
    {
        var parameters = method.Parameters.Select(p => new ParameterModel(
            p.Name,
            p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            p.IsOptional,
            p.HasExplicitDefaultValue ? p.ExplicitDefaultValue?.ToString() : null
        )).ToList();

        string? filePath = null;
        int startLine = 0, endLine = 0;

        var location = method.Locations.FirstOrDefault(l => l.IsInSource);
        if (location != null)
        {
            var lineSpan = location.GetLineSpan();
            filePath = lineSpan.Path;
            startLine = lineSpan.StartLinePosition.Line + 1;
            endLine = lineSpan.EndLinePosition.Line + 1;
        }

        var name = method.MethodKind == MethodKind.Constructor ? ".ctor" : method.Name;

        return new MethodModel(
            Id: SymbolIdGenerator.GenerateId(method),
            Name: name,
            FullName: SymbolIdGenerator.GenerateDisplayString(method),
            ReturnType: method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Parameters: parameters,
            FilePath: filePath,
            StartLine: startLine,
            EndLine: endLine,
            Accessibility: method.DeclaredAccessibility,
            IsStatic: method.IsStatic,
            IsAsync: method.IsAsync,
            IsVirtual: method.IsVirtual,
            IsOverride: method.IsOverride,
            IsAbstract: method.IsAbstract
        );
    }

    private static ModelTypeKind MapTypeKind(INamedTypeSymbol type)
    {
        if (type.IsRecord)
            return ModelTypeKind.Record;

        return type.TypeKind switch
        {
            RoslynTypeKind.Interface => ModelTypeKind.Interface,
            RoslynTypeKind.Struct => ModelTypeKind.Struct,
            RoslynTypeKind.Enum => ModelTypeKind.Enum,
            RoslynTypeKind.Delegate => ModelTypeKind.Delegate,
            _ => ModelTypeKind.Class
        };
    }

    private List<Relationship> BuildRelationships(ProjectModel project)
    {
        var relationships = new List<Relationship>();
        BuildNamespaceRelationships(project.Id, project.Namespaces, relationships);
        return relationships;
    }

    private void BuildNamespaceRelationships(string parentId, List<NamespaceModel> namespaces, List<Relationship> relationships)
    {
        foreach (var ns in namespaces)
        {
            relationships.Add(new Relationship(parentId, ns.Id, RelationshipKind.Contains));
            BuildNamespaceRelationships(ns.Id, ns.ChildNamespaces, relationships);
            BuildTypeRelationships(ns.Id, ns.Types, relationships);
        }
    }

    private void BuildTypeRelationships(string parentId, List<TypeModel> types, List<Relationship> relationships)
    {
        foreach (var type in types)
        {
            relationships.Add(new Relationship(parentId, type.Id, RelationshipKind.Contains));

            foreach (var iface in type.ImplementedInterfaces)
                relationships.Add(new Relationship(type.Id, iface, RelationshipKind.Implements));

            foreach (var method in type.Methods)
                relationships.Add(new Relationship(type.Id, method.Id, RelationshipKind.Contains));

            BuildTypeRelationships(type.Id, type.NestedTypes, relationships);
        }
    }
}
