using AiCodeGraph.Core.Models.CodeGraph;

namespace AiCodeGraph.Cli.Helpers;

/// <summary>
/// Helper methods for counting types and methods in the code model.
/// </summary>
public static class ModelCountHelpers
{
    public static int CountTypes(ProjectModel project)
    {
        return project.Namespaces.Sum(ns => CountTypesInNamespace(ns));
    }

    public static int CountTypesInNamespace(NamespaceModel ns)
    {
        return ns.Types.Count
            + ns.Types.Sum(t => CountNestedTypes(t))
            + ns.ChildNamespaces.Sum(c => CountTypesInNamespace(c));
    }

    public static int CountNestedTypes(TypeModel type)
    {
        return type.NestedTypes.Count + type.NestedTypes.Sum(CountNestedTypes);
    }

    public static int CountMethods(ProjectModel project)
    {
        return project.Namespaces.Sum(ns => CountMethodsInNamespace(ns));
    }

    public static int CountMethodsInNamespace(NamespaceModel ns)
    {
        return ns.Types.Sum(t => t.Methods.Count + t.NestedTypes.Sum(CountMethodsInType))
            + ns.ChildNamespaces.Sum(c => CountMethodsInNamespace(c));
    }

    public static int CountMethodsInType(TypeModel type)
    {
        return type.Methods.Count + type.NestedTypes.Sum(CountMethodsInType);
    }
}
