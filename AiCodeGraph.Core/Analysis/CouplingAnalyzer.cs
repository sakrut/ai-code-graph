using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core.Analysis;

public record CouplingMetrics(
    string Name,
    int AfferentCoupling,     // Ca - incoming dependencies
    int EfferentCoupling,     // Ce - outgoing dependencies
    float Instability,        // I = Ce / (Ca + Ce)
    float Abstractness,       // A = abstract types / total types
    float DistanceFromMain);  // D = |A + I - 1|

public class CouplingAnalyzer
{
    public async Task<List<CouplingMetrics>> AnalyzeAsync(
        IStorageService storage,
        string level,
        CancellationToken ct)
    {
        var methods = await storage.GetMethodsForExportAsync(null, ct).ConfigureAwait(false);
        if (methods.Count == 0) return new List<CouplingMetrics>();

        var allMethodIds = methods.Select(m => m.Id).ToHashSet();
        var allCalls = await storage.GetCallGraphForMethodsAsync(allMethodIds, ct).ConfigureAwait(false);

        // Build method -> group mapping
        var methodGroup = new Dictionary<string, string>();
        var groupMembers = new Dictionary<string, HashSet<string>>();

        foreach (var m in methods)
        {
            var group = GetGroup(m.FullName, level);
            methodGroup[m.Id] = group;

            if (!groupMembers.ContainsKey(group))
                groupMembers[group] = new HashSet<string>();
            groupMembers[group].Add(m.Id);
        }

        // Get type abstractness info from the tree data
        var treeData = await storage.GetTreeAsync(null, null, ct).ConfigureAwait(false);
        var groupAbstractness = ComputeAbstractness(treeData, level);

        // Compute Ca and Ce for each group
        var metrics = new List<CouplingMetrics>();
        foreach (var (groupName, memberIds) in groupMembers)
        {
            int ca = 0, ce = 0;

            foreach (var (callerId, calleeId) in allCalls)
            {
                if (!methodGroup.ContainsKey(callerId) || !methodGroup.ContainsKey(calleeId))
                    continue;

                var callerGroup = methodGroup[callerId];
                var calleeGroup = methodGroup[calleeId];

                if (callerGroup == groupName && calleeGroup != groupName) ce++;
                if (callerGroup != groupName && calleeGroup == groupName) ca++;
            }

            var instability = (ca + ce) > 0 ? (float)ce / (ca + ce) : 0f;
            var abstractness = groupAbstractness.GetValueOrDefault(groupName, 0f);
            var distance = MathF.Abs(abstractness + instability - 1f);

            metrics.Add(new CouplingMetrics(groupName, ca, ce, instability, abstractness, distance));
        }

        return metrics.OrderByDescending(m => m.EfferentCoupling).ToList();
    }

    private static string GetGroup(string fullName, string level)
    {
        // FullName format: Namespace.SubNamespace.Type.Method(params)
        var parenIdx = fullName.IndexOf('(');
        var nameOnly = parenIdx >= 0 ? fullName[..parenIdx] : fullName;
        var parts = nameOnly.Split('.');

        if (level == "type")
        {
            // Return Namespace.Type (everything except last part which is the method)
            return parts.Length >= 2 ? string.Join(".", parts[..^1]) : parts[0];
        }
        else
        {
            // namespace level: everything except last two parts (type.method)
            return parts.Length >= 3 ? string.Join(".", parts[..^2]) : parts[0];
        }
    }

    private static Dictionary<string, float> ComputeAbstractness(
        List<(string ProjectName, string NamespaceName, string TypeName, string TypeKind, string MethodName, string ReturnType)> treeData,
        string level)
    {
        var groupTypes = new Dictionary<string, (int Total, int Abstract)>();

        foreach (var entry in treeData)
        {
            var group = level == "type"
                ? $"{entry.NamespaceName}.{entry.TypeName}"
                : entry.NamespaceName;

            if (!groupTypes.ContainsKey(group))
                groupTypes[group] = (0, 0);

            var current = groupTypes[group];
            var isAbstract = entry.TypeKind is "Interface" or "Abstract";
            groupTypes[group] = (current.Total + 1, current.Abstract + (isAbstract ? 1 : 0));
        }

        // Deduplicate type counting (tree data has one row per method)
        var typesByGroup = new Dictionary<string, Dictionary<string, bool>>();
        foreach (var entry in treeData)
        {
            var group = level == "type"
                ? $"{entry.NamespaceName}.{entry.TypeName}"
                : entry.NamespaceName;

            if (!typesByGroup.ContainsKey(group))
                typesByGroup[group] = new Dictionary<string, bool>();

            var typeKey = $"{entry.NamespaceName}.{entry.TypeName}";
            var isAbstract = entry.TypeKind is "Interface" or "Abstract";
            typesByGroup[group][typeKey] = isAbstract;
        }

        return typesByGroup.ToDictionary(
            g => g.Key,
            g =>
            {
                var total = g.Value.Count;
                var abstractCount = g.Value.Values.Count(v => v);
                return total > 0 ? (float)abstractCount / total : 0f;
            });
    }
}
