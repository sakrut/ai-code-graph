# Task ID: 55

**Title:** Improve Intent Cluster Label Generation with Context-Aware Patterns

**Status:** done

**Dependencies:** 32 ✓, 21 ✓, 9 ✓

**Priority:** medium

**Description:** Rewrite the GenerateLabel method in IntentClusterer to produce more descriptive, actionable cluster labels by using verb+noun pair frequency analysis, namespace context extraction, and special handling for test method clusters.

**Details:**

File: AiCodeGraph.Core/Duplicates/IntentClusterer.cs, method GenerateLabel() (line 174)

The current implementation independently counts top verbs and top nouns, producing generic labels like "save/method operations" or "single/returns operations". The fix requires three improvements:

### 1. Verb+Noun Pair Frequency (Primary Improvement)

Instead of counting verbs and nouns independently, track co-occurring verb+noun pairs from each method name:

```csharp
private static string GenerateLabel(List<string> memberIds, Dictionary<string, NormalizedMethod> methodMap)
{
    var pairCounts = new Dictionary<(string Verb, string Noun), int>(new VerbNounComparer());
    var namespaceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    bool allTests = true;

    foreach (var id in memberIds)
    {
        if (!methodMap.TryGetValue(id, out _)) continue;

        var shortName = ExtractShortName(id);
        var namespacePart = ExtractNamespaceContext(id);
        if (!string.IsNullOrEmpty(namespacePart))
        {
            namespaceCounts.TryGetValue(namespacePart, out var nc);
            namespaceCounts[namespacePart] = nc + 1;
        }

        // Detect test methods
        if (!IsTestMethod(shortName, id))
            allTests = false;

        var segments = SplitPascalCase(shortName);
        if (segments.Count < 2) continue;

        var verb = segments[0];
        if (Stopwords.Contains(verb) || verb.Length <= 1) continue;

        // Pair verb with each subsequent meaningful noun
        for (int i = 1; i < segments.Count; i++)
        {
            var noun = segments[i];
            if (Stopwords.Contains(noun) || noun.Length <= 2) continue;
            var key = (verb, noun);
            pairCounts.TryGetValue(key, out var pc);
            pairCounts[key] = pc + 1;
            break; // Use first meaningful noun only for tighter labels
        }
    }

    // Handle test clusters specially
    if (allTests && memberIds.Count > 1)
        return GenerateTestLabel(memberIds, methodMap, namespaceCounts);

    return FormatLabel(pairCounts, namespaceCounts, memberIds.Count);
}
```

### 2. Namespace Context Extraction

Add a helper to extract the innermost meaningful namespace/class context:

```csharp
private static string ExtractNamespaceContext(string methodId)
{
    var parenIdx = methodId.IndexOf('(');
    var nameOnly = parenIdx >= 0 ? methodId[..parenIdx] : methodId;
    var parts = nameOnly.Split('.');
    // Return the containing type name (second-to-last segment)
    if (parts.Length >= 2)
        return parts[^2]; // e.g., "StorageService" from "Namespace.StorageService.SaveAsync"
    return string.Empty;
}
```

### 3. Test Method Detection and Labeling

```csharp
private static bool IsTestMethod(string shortName, string fullId)
{
    // Common test prefixes/suffixes
    var testIndicators = new[] { "Test", "Tests", "Should", "Verify", "Assert", "Fact", "Theory" };
    var segments = SplitPascalCase(shortName);
    if (segments.Any(s => testIndicators.Contains(s, StringComparer.OrdinalIgnoreCase)))
        return true;
    // Check if containing class ends with "Tests"
    return fullId.Contains("Tests.", StringComparison.OrdinalIgnoreCase) ||
           fullId.Contains("Test.", StringComparison.OrdinalIgnoreCase);
}

private static string GenerateTestLabel(List<string> memberIds, Dictionary<string, NormalizedMethod> methodMap, Dictionary<string, int> namespaceCounts)
{
    // For test clusters, use the containing class/subject as context
    var topNamespace = namespaceCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
    if (!string.IsNullOrEmpty(topNamespace))
    {
        // Remove "Tests" suffix for cleaner label
        var subject = topNamespace.Replace("Tests", "").Replace("Test", "");
        if (!string.IsNullOrEmpty(subject))
            return $"{subject} unit tests";
    }
    return "unit tests";
}
```

### 4. Improved Label Formatting

```csharp
private static string FormatLabel(Dictionary<(string Verb, string Noun), int> pairCounts, Dictionary<string, int> namespaceCounts, int memberCount)
{
    var topPair = pairCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();
    var topNamespace = namespaceCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;

    if (topPair.Key != default && topPair.Value >= 2)
    {
        // Strong verb+noun pattern: "Save User operations" or "StorageService Save operations"
        var label = $"{topPair.Key.Verb} {topPair.Key.Noun}";
        // Add namespace context if it provides additional info and is dominant
        if (!string.IsNullOrEmpty(topNamespace) && 
            !label.Contains(topNamespace, StringComparison.OrdinalIgnoreCase))
            return $"{topNamespace} {label.ToLowerInvariant()} operations";
        return $"{label} operations";
    }

    // Fallback: use just top verb with namespace context
    var topVerb = pairCounts.Keys.GroupBy(k => k.Verb)
        .OrderByDescending(g => g.Count())
        .FirstOrDefault()?.Key;

    if (topVerb != null && !string.IsNullOrEmpty(topNamespace))
        return $"{topNamespace} {topVerb.ToLowerInvariant()} operations";
    if (topVerb != null)
        return $"{topVerb} operations";
    if (!string.IsNullOrEmpty(topNamespace))
        return $"{topNamespace} operations";

    return "miscellaneous";
}
```

### 5. VerbNounComparer for Case-Insensitive Tuple Keys

```csharp
private class VerbNounComparer : IEqualityComparer<(string Verb, string Noun)>
{
    public bool Equals((string Verb, string Noun) x, (string Verb, string Noun) y) =>
        string.Equals(x.Verb, y.Verb, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Noun, y.Noun, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string Verb, string Noun) obj) =>
        HashCode.Combine(
            obj.Verb.ToLowerInvariant().GetHashCode(),
            obj.Noun.ToLowerInvariant().GetHashCode());
}
```

### Expected Label Improvements

| Methods | Before | After |
|---------|--------|-------|
| SaveUser, SaveOrder, SaveProfile | save/user operations | Save User operations |
| GetName_Test, GetId_Test (in UserTests class) | get/name operations | User unit tests |
| ValidateInput, ValidateEmail, ValidateAddress | validate/input operations | Validate Input operations |
| StorageService.SaveAsync, StorageService.LoadAsync | save/async operations | StorageService save operations |

**Test Strategy:**

Update and expand IntentClusterer tests in AiCodeGraph.Tests/DuplicateDetectionTests.cs:

1. **Verb+Noun pair labels**: Create cluster with method IDs like "Namespace.Type.SaveUser()", "Namespace.Type.SaveOrder()", "Namespace.Type.SaveProfile()" and verify label contains "Save User" or "Save" with a meaningful noun, not generic "save/user operations".

2. **Test method cluster detection**: Create cluster with method IDs containing "Tests." in the namespace (e.g., "AiCodeGraph.Tests.CognitiveComplexityTests.SingleIf_ReturnsOne()") and verify label ends with "unit tests" and includes the subject (e.g., "CognitiveComplexity unit tests").

3. **Namespace context inclusion**: Create cluster with methods all from the same class (e.g., "StorageService.SaveAsync", "StorageService.LoadAsync", "StorageService.DeleteAsync") and verify the class name appears in the label.

4. **Mixed verb cluster**: Create cluster with methods like "GetUser", "GetOrder", "SetUser" and verify the most common verb ("Get") dominates the label.

5. **Single-method fallback**: Verify graceful handling when cluster has one method or methods without PascalCase names (should produce "miscellaneous" or namespace-based label).

6. **Edge cases**: Methods without meaningful nouns (single-word names like "Execute"), methods with acronyms (e.g., "ParseJSON"), and methods starting with stopwords.

7. **Regression tests**: Ensure existing IntentClusterer tests (ClusterMethods_SimilarMethods_GroupsTogether, ClusterMethods_GeneratesLabels, etc.) still pass with the new label format - labels should still be non-empty strings.

8. **Integration test**: Run the full analyze pipeline on the test fixture solution and verify that cluster labels in the output are descriptive (not matching the old "verb/noun operations" pattern for most clusters).
