# Task ID: 24

**Title:** VectorIndex Null/NaN Validation

**Status:** done

**Dependencies:** None

**Priority:** high

**Description:** Add input validation to VectorIndex.AddItem() and BuildIndex() to reject null arrays and vectors containing NaN or Infinity values.

**Details:**

File: AiCodeGraph.Core/Embeddings/VectorIndex.cs lines 26-35

Add validation in AddItem and BuildIndex:

```csharp
public void AddItem(string id, float[] vector)
{
    ArgumentNullException.ThrowIfNull(vector, nameof(vector));
    ValidateVector(vector);
    
    if (_items.Count == 0)
        _dimensions = vector.Length;
    else if (vector.Length != _dimensions)
        throw new ArgumentException($"Vector dimension {vector.Length} does not match expected {_dimensions}");
    
    _items.Add((id, Normalize(vector)));
}

public void BuildIndex(List<(string Id, float[] Vector)> items)
{
    _items.Clear();
    foreach (var (id, vector) in items)
    {
        ArgumentNullException.ThrowIfNull(vector, nameof(vector));
        ValidateVector(vector);
    }
    // ... existing dimension check and normalization ...
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
```

Also validate in Search method for the query vector.

**Test Strategy:**

Add tests in EmbeddingsTests.cs: (1) AddItem with null vector throws ArgumentNullException. (2) AddItem with NaN value throws ArgumentException. (3) AddItem with Infinity throws ArgumentException. (4) AddItem with -Infinity throws. (5) BuildIndex with list containing null vector throws. (6) Search with null query throws. (7) Search with NaN query throws. (8) Valid vectors still work correctly.

## Subtasks

### 24.1. Add ValidateVector private helper method to VectorIndex

**Status:** pending  
**Dependencies:** None  

Create a private static ValidateVector(float[] vector) method in VectorIndex.cs that iterates through the vector array and throws ArgumentException if any element is NaN or Infinity (positive or negative).

**Details:**

Add the following private static method to VectorIndex.cs (after the existing Normalize method at line 126):

```csharp
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
```

This method checks both positive and negative infinity via float.IsInfinity() which covers both cases. The error message includes the specific index for debugging purposes.

### 24.2. Add null and NaN/Infinity validation to AddItem method

**Status:** pending  
**Dependencies:** 24.1  

Add ArgumentNullException.ThrowIfNull for the vector parameter and call ValidateVector before any processing in the AddItem method at line 26 of VectorIndex.cs.

**Details:**

Modify the AddItem method (currently at lines 26-35) to add null check and vector validation before the existing dimension check logic:

```csharp
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
```

The null check comes first (fast fail), then NaN/Infinity validation, then the existing dimension check. This ensures invalid data never enters the index.

### 24.3. Add null and NaN/Infinity validation to BuildIndex method

**Status:** pending  
**Dependencies:** 24.1  

Add null check and ValidateVector call for each vector in the items list within the BuildIndex method at line 12 of VectorIndex.cs, ensuring invalid vectors are rejected before any items are added.

**Details:**

Modify the BuildIndex method (currently at lines 12-24) to validate all vectors before processing any of them. This ensures atomicity - either all items are valid and get indexed, or an exception is thrown with no partial state:

```csharp
public void BuildIndex(List<(string Id, float[] Vector)> items)
{
    _items.Clear();
    if (items.Count == 0) return;

    // Validate all vectors first (atomic check)
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
```

Note: Validation is done in a separate first pass to avoid partial index population if a later item has invalid data.

### 24.4. Add null and NaN/Infinity validation to Search method query vector

**Status:** pending  
**Dependencies:** 24.1  

Add ArgumentNullException.ThrowIfNull and ValidateVector call for the query parameter in the Search method at line 37 of VectorIndex.cs, before the empty index early-return check.

**Details:**

Modify the Search method (currently at lines 37-55) to validate the query vector:

```csharp
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
```

The null and NaN/Infinity checks are placed before the empty-index check so that invalid query vectors are always rejected regardless of index state.

### 24.5. Add comprehensive unit tests for VectorIndex validation

**Status:** pending  
**Dependencies:** 24.2, 24.3, 24.4  

Add unit tests to EmbeddingsTests.cs covering all null, NaN, and Infinity validation scenarios for AddItem, BuildIndex, and Search methods.

**Details:**

Add the following test methods to the existing VectorIndexTests class in AiCodeGraph.Tests/EmbeddingsTests.cs:

```csharp
[Fact]
public void AddItem_NullVector_ThrowsArgumentNullException()
{
    var index = new VectorIndex();
    Assert.Throws<ArgumentNullException>(() => index.AddItem("a", null!));
}

[Fact]
public void AddItem_NaNVector_ThrowsArgumentException()
{
    var index = new VectorIndex();
    Assert.Throws<ArgumentException>(() => index.AddItem("a", new float[] { 1.0f, float.NaN, 0.5f }));
}

[Fact]
public void AddItem_PositiveInfinityVector_ThrowsArgumentException()
{
    var index = new VectorIndex();
    Assert.Throws<ArgumentException>(() => index.AddItem("a", new float[] { float.PositiveInfinity, 0.5f, 0.5f }));
}

[Fact]
public void AddItem_NegativeInfinityVector_ThrowsArgumentException()
{
    var index = new VectorIndex();
    Assert.Throws<ArgumentException>(() => index.AddItem("a", new float[] { 0.5f, float.NegativeInfinity, 0.5f }));
}

[Fact]
public void BuildIndex_NullVector_ThrowsArgumentNullException()
{
    var index = new VectorIndex();
    var items = new List<(string Id, float[] Vector)>
    {
        ("a", CreateVector(1, 0, 0)),
        ("b", null!)
    };
    Assert.Throws<ArgumentNullException>(() => index.BuildIndex(items));
}

[Fact]
public void BuildIndex_NaNVector_ThrowsArgumentException()
{
    var index = new VectorIndex();
    var items = new List<(string Id, float[] Vector)>
    {
        ("a", CreateVector(1, 0, 0)),
        ("b", new float[] { float.NaN, 0, 0 })
    };
    Assert.Throws<ArgumentException>(() => index.BuildIndex(items));
}

[Fact]
public void BuildIndex_NaNVector_IndexRemainsEmpty()
{
    var index = new VectorIndex();
    var items = new List<(string Id, float[] Vector)>
    {
        ("a", CreateVector(1, 0, 0)),
        ("b", new float[] { float.NaN, 0, 0 })
    };
    try { index.BuildIndex(items); } catch { }
    Assert.Equal(0, index.Count);
}

[Fact]
public void Search_NullQuery_ThrowsArgumentNullException()
{
    var index = new VectorIndex();
    index.AddItem("a", CreateVector(1, 0, 0));
    Assert.Throws<ArgumentNullException>(() => index.Search(null!));
}

[Fact]
public void Search_NaNQuery_ThrowsArgumentException()
{
    var index = new VectorIndex();
    index.AddItem("a", CreateVector(1, 0, 0));
    Assert.Throws<ArgumentException>(() => index.Search(new float[] { float.NaN, 0, 0 }));
}

[Fact]
public void Search_InfinityQuery_ThrowsArgumentException()
{
    var index = new VectorIndex();
    index.AddItem("a", CreateVector(1, 0, 0));
    Assert.Throws<ArgumentException>(() => index.Search(new float[] { float.PositiveInfinity, 0, 0 }));
}
```

Run all tests with `dotnet test` to verify both new validation tests pass and all existing tests remain green.
