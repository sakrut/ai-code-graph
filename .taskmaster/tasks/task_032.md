# Task ID: 32

**Title:** Improve Cluster Labels Using Method Signatures

**Status:** done

**Dependencies:** 21 ✓

**Priority:** medium

**Description:** Rewrite GenerateLabel() in IntentClusterer to produce descriptive labels from PascalCase-split method names grouped by leading verb and common noun.

**Details:**

File: AiCodeGraph.Core/Duplicates/IntentClusterer.cs GenerateLabel() method

Replace current token-frequency approach with PascalCase name analysis:

```csharp
private static string GenerateLabel(List<string> memberIds, Dictionary<string, NormalizedMethod> methodMap)
{
    var verbCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var nounCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    
    foreach (var id in memberIds)
    {
        if (!methodMap.TryGetValue(id, out var method)) continue;
        
        // Extract method short name from fully qualified ID
        var shortName = ExtractShortName(id);
        var segments = SplitPascalCase(shortName);
        
        if (segments.Count == 0) continue;
        
        // First segment is typically the verb
        var verb = segments[0];
        if (!Stopwords.Contains(verb))
        {
            verbCounts.TryGetValue(verb, out var vc);
            verbCounts[verb] = vc + 1;
        }
        
        // Remaining segments are nouns/objects
        for (int i = 1; i < segments.Count; i++)
        {
            var noun = segments[i];
            if (!Stopwords.Contains(noun) && noun.Length > 2)
            {
                nounCounts.TryGetValue(noun, out var nc);
                nounCounts[noun] = nc + 1;
            }
        }
    }
    
    var topVerb = verbCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
    var topNoun = nounCounts.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
    
    if (topVerb != null && topNoun != null)
        return $"{topVerb}/{topNoun} operations";
    if (topVerb != null)
        return $"{topVerb} operations";
    if (topNoun != null)
        return $"{topNoun} handlers";
    
    return "miscellaneous";
}

private static List<string> SplitPascalCase(string name)
{
    var segments = new List<string>();
    var current = new System.Text.StringBuilder();
    
    foreach (var ch in name)
    {
        if (char.IsUpper(ch) && current.Length > 0)
        {
            segments.Add(current.ToString());
            current.Clear();
        }
        current.Append(ch);
    }
    if (current.Length > 0)
        segments.Add(current.ToString());
    
    return segments;
}

private static string ExtractShortName(string methodId)
{
    // Method ID format: Namespace.Type.MethodName(params)
    var parenIdx = methodId.IndexOf('(');
    var nameOnly = parenIdx >= 0 ? methodId[..parenIdx] : methodId;
    var lastDot = nameOnly.LastIndexOf('.');
    return lastDot >= 0 ? nameOnly[(lastDot + 1)..] : nameOnly;
}
```

**Test Strategy:**

Update IntentClusterer tests to verify new label format. Test clusters with methods like SaveUser/SaveOrder produce 'Save operations'. Test clusters with GetName/GetId produce 'Get operations'. Test mixed verbs produce most-common verb. Test edge cases: single method clusters, methods without PascalCase.

## Subtasks

### 32.1. Implement SplitPascalCase and ExtractShortName helper methods

**Status:** pending  
**Dependencies:** None  

Add two private static helper methods to IntentClusterer.cs: SplitPascalCase splits a PascalCase method name into individual word segments, and ExtractShortName extracts the short method name from a fully qualified method ID (stripping namespace, type, and parameters).

**Details:**

Add two methods after the existing ComputeCohesion method (after line 157) in AiCodeGraph.Core/Duplicates/IntentClusterer.cs:

1. `private static string ExtractShortName(string methodId)` - Parses the method ID format 'Namespace.Type.MethodName(params)' by finding the opening parenthesis to strip parameters, then finding the last dot to extract just the method name.

2. `private static List<string> SplitPascalCase(string name)` - Iterates through characters, splitting on uppercase letters to produce segments. Uses StringBuilder to accumulate characters between splits. Returns a list of string segments (e.g., 'GetUserById' -> ['Get', 'User', 'By', 'Id']).

Both methods are pure utility functions with no external dependencies beyond the method ID format convention defined by SymbolIdGenerator.GetMethodId().

### 32.2. Rewrite GenerateLabel with verb/noun counting from PascalCase-split names

**Status:** pending  
**Dependencies:** 32.1  

Replace the current token-frequency GenerateLabel implementation (lines 159-194) with the new approach that uses ExtractShortName and SplitPascalCase to split method names, counts verbs (first segment) and nouns (remaining segments), and produces labels in the format 'verb/noun operations'.

**Details:**

Replace the entire GenerateLabel method body in IntentClusterer.cs (lines 159-194). The new implementation:

1. Creates two frequency dictionaries: verbCounts and nounCounts (case-insensitive).
2. For each member ID, calls ExtractShortName to get the method's short name, then SplitPascalCase to get segments.
3. The first segment is treated as the verb; if it's not in the Stopwords set (from task 21's static field), increment its count.
4. Remaining segments longer than 2 characters and not in Stopwords are counted as nouns.
5. Selects topVerb and topNoun by descending frequency.
6. Returns formatted label: both present -> 'verb/noun operations', verb only -> 'verb operations', noun only -> 'noun handlers', neither -> 'miscellaneous'.

Remove the inline stopWords HashSet (lines 161-166) since the class-level static Stopwords field from task 21 will be used instead. The method signature remains unchanged: `private static string GenerateLabel(List<string> memberIds, Dictionary<string, NormalizedMethod> methodMap)`.

### 32.3. Update DuplicateDetectionTests for new label format verification

**Status:** pending  
**Dependencies:** 32.1, 32.2  

Update the existing IntentClustererTests.ClusterMethods_GeneratesLabels test and add new test cases that verify the new verb/noun label format with various method name patterns including PascalCase names, mixed verbs, and edge cases.

**Details:**

In AiCodeGraph.Tests/DuplicateDetectionTests.cs, update the IntentClustererTests class:

1. Update `ClusterMethods_GeneratesLabels` (line 380): Change method IDs from generic 'm1','m2','m3' to fully qualified PascalCase names like 'App.Service.CheckPermission()', 'App.Guard.CheckAccess()', 'App.Auth.CheckRole()' so the new label logic can extract meaningful verbs/nouns.

2. Add `ClusterMethods_LabelFormat_VerbNounOperations`: Create a cluster with methods like 'Ns.T.SaveUser()', 'Ns.T.SaveOrder()', 'Ns.T.SaveConfig()' using identical embeddings. Assert the label contains 'Save' and ends with 'operations'.

3. Add `ClusterMethods_LabelFormat_MixedVerbs_UsesMostCommon`: Create cluster with 3 'Get' methods and 1 'Set' method. Assert label verb is 'Get' (most frequent).

4. Add `ClusterMethods_LabelFormat_NoPascalCase_ReturnsMiscellaneous`: Test with method IDs that have no PascalCase structure (e.g., lowercase names) producing 'miscellaneous' label.

5. Add `ClusterMethods_LabelFormat_SingleMethodCluster`: Verify single-method cluster with PascalCase name still produces a reasonable label.
