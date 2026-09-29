using System.Diagnostics;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Extensions;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Tests;

public class IncludeVisitorTests
{
    [Theory]
    [InlineData("@include:simple", "(group status:active)")]
    [InlineData("@include:complex", "(group (bool ?a:1 ?b:2))")]
    [InlineData("x:1 @include:complex", "(bool +x:1 +(group (bool ?a:1 ?b:2)))")]
    [InlineData("@include:simple OR @include:complex", "(bool ?(group status:active) ?(group (bool ?a:1 ?b:2)))")]
    [InlineData("-@include:complex", "(bool -(group (bool ?a:1 ?b:2)))")]
    [InlineData("NOT @include:complex", "(bool -(group (bool ?a:1 ?b:2)))")]
    [InlineData("x:1 OR NOT @include:simple", "(bool ?x:1 ?(not (group status:active)))")]
    [InlineData("(@include:simple)", "(group (group status:active))")]
    [InlineData("@include:\"with space\"", "(group spaced:yes)")]
    [InlineData("@INCLUDE:SIMPLE", "(group status:active)")]
    [InlineData("@include:simple^2", "(group status:active^2)")]
    [InlineData("@include:nested", "(group (bool +(group status:active) +c:3))")]
    [InlineData("@include:deep", "(group (group (bool +(group status:active) +c:3)))")]
    [InlineData("plain:query", "plain:query")]
    public void ExpandIncludes_ValidIncludes_ExpandsInPlace(string query, string expected)
    {
        // Arrange
        var document = LuceneQuery.Parse(query).Document;
        var context = new QueryVisitorContext();

        // Act
        var result = document.ExpandIncludes(CreateIncludes(), context);

        // Assert
        Assert.Equal(expected, result.ToDebugString());
        Assert.True(context.ValidationResult.IsValid, context.ValidationResult.Message);
    }

    [Fact]
    public void ExpandIncludes_ExcludedInclude_ExcludesWholeExpansion()
    {
        // Arrange
        var document = LuceneQuery.Parse("x:1 -@include:either").Document;
        var includes = new Dictionary<string, string> { ["either"] = "a:1 OR b:2" };

        // Act
        var result = document.ExpandIncludes(includes);

        // Assert
        var boolean = Assert.IsType<BooleanQueryNode>(result.Query);
        Assert.Equal(Occur.MustNot, boolean.Clauses[1].Occur);
        var group = Assert.IsType<GroupNode>(boolean.Clauses[1].Query);
        Assert.Equal("either", group.GetData<string>(IncludeVisitor.IncludeNameKey));
        Assert.Equal("x:1 -(a:1 OR b:2)", QueryStringBuilder.ToQueryString(result));
    }

    [Fact]
    public void ExpandIncludes_ExpansionGroup_KeepsReferencePosition()
    {
        // Arrange
        var document = LuceneQuery.Parse("x:1 @include:simple").Document;
        var reference = (FieldQueryNode)((BooleanQueryNode)document.Query!).Clauses[1].Query!;

        // Act
        var result = document.ExpandIncludes(CreateIncludes());

        // Assert
        var group = (GroupNode)((BooleanQueryNode)result.Query!).Clauses[1].Query!;
        Assert.Equal(reference.StartPosition, group.StartPosition);
        Assert.Equal(reference.EndPosition, group.EndPosition);
    }

    [Fact]
    public void ExpandIncludes_IncludeUsesDefaultOperatorFromContext_ParsesIncludeWithIt()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:pair").Document;
        var context = new QueryVisitorContext { DefaultOperator = BooleanOperator.Or };

        // Act
        var result = document.ExpandIncludes(new Dictionary<string, string> { ["pair"] = "a b" }, context);

        // Assert
        Assert.Equal("(group (bool ?a ?b))", result.ToDebugString());
    }

    [Fact]
    public void ExpandIncludes_TracksReferencedIncludes()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:deep @include:simple").Document;
        var context = new QueryVisitorContext();

        // Act
        document.ExpandIncludes(CreateIncludes(), context);

        // Assert
        Assert.Equal(["deep", "nested", "simple"], context.ValidationResult.ReferencedIncludes.Order());
    }

    [Theory]
    [InlineData("@include:missing", "missing")]
    [InlineData("@include:empty", "empty")]
    [InlineData("@include:whitespace", "whitespace")]
    public void ExpandIncludes_UnresolvableInclude_RecordsUnresolvedAndLeavesReference(string query, string name)
    {
        // Arrange
        var document = LuceneQuery.Parse(query).Document;
        var context = new QueryVisitorContext();
        var includes = new Dictionary<string, string> { ["empty"] = "", ["whitespace"] = "   " };

        // Act
        var result = document.ExpandIncludes(includes, context);

        // Assert
        Assert.Equal($"@include:{name}", result.ToDebugString());
        Assert.Equal([name], context.ValidationResult.UnresolvedIncludes);
        Assert.True(context.ValidationResult.IsValid);
    }

    [Fact]
    public void Accept_NoIncludesConfigured_RecordsAllAsUnresolved()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:a @include:b").Document;
        var context = new QueryVisitorContext();

        // Act
        IncludeVisitor.Instance.Run(document, context);

        // Assert
        Assert.Equal(["a", "b"], context.ValidationResult.UnresolvedIncludes.Order());
    }

    [Fact]
    public void ExpandIncludes_MissingName_AddsError()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:(a b)").Document;
        var context = new QueryVisitorContext();

        // Act
        document.ExpandIncludes(CreateIncludes(), context);

        // Assert
        var error = Assert.Single(context.ValidationResult.ValidationErrors);
        Assert.Equal(QueryErrorCode.UnresolvedInclude, error.Code);
    }

    [Fact]
    public void ExpandIncludes_InvalidIncludeSyntax_AddsError()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:broken").Document;
        var context = new QueryVisitorContext();

        // Act
        var result = document.ExpandIncludes(new Dictionary<string, string> { ["broken"] = "a:(b" }, context);

        // Assert
        Assert.Equal("@include:broken", result.ToDebugString());
        var error = Assert.Single(context.ValidationResult.ValidationErrors);
        Assert.Contains("broken", error.Message);
        Assert.Equal(QueryErrorCode.UnresolvedInclude, error.Code);
    }

    [Theory]
    [InlineData("self")]
    [InlineData("a")]
    [InlineData("B")]
    public void ExpandIncludes_RecursiveIncludes_AddsRecursionError(string start)
    {
        // Arrange
        var document = LuceneQuery.Parse($"@include:{start}").Document;
        var context = new QueryVisitorContext();
        var includes = new Dictionary<string, string>
        {
            ["self"] = "x:1 OR @include:self",
            ["a"] = "x:1 @include:b",
            ["b"] = "y:1 @include:A"
        };

        // Act
        document.ExpandIncludes(includes, context);

        // Assert
        var error = Assert.Single(context.ValidationResult.ValidationErrors);
        Assert.Contains("Recursive", error.Message);
        Assert.Equal(QueryErrorCode.UnresolvedInclude, error.Code);
    }

    [Fact]
    public void ExpandIncludes_SameIncludeUsedTwiceSideBySide_IsNotRecursion()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:simple OR @include:simple").Document;
        var context = new QueryVisitorContext();

        // Act
        var result = document.ExpandIncludes(CreateIncludes(), context);

        // Assert
        Assert.True(context.ValidationResult.IsValid, context.ValidationResult.Message);
        Assert.Equal("(bool ?(group status:active) ?(group status:active))", result.ToDebugString());
    }

    [Fact]
    public void ExpandIncludes_DepthExceedsMaximum_AddsDepthError()
    {
        // Arrange
        var includes = Enumerable.Range(0, 20).ToDictionary(i => $"i{i}", i => $"f{i}:x @include:i{i + 1}");
        includes["i20"] = "end:1";
        var document = LuceneQuery.Parse("@include:i0").Document;
        var context = new QueryVisitorContext { ValidationOptions = new QueryValidationOptions { MaxIncludeDepth = 5 } };

        // Act
        document.ExpandIncludes(includes, context);

        // Assert
        var error = Assert.Single(context.ValidationResult.ValidationErrors);
        Assert.Equal(QueryErrorCode.MaxDepthExceeded, error.Code);
        Assert.Contains("i5", error.Message);
        Assert.Equal(["f0", "f1", "f2", "f3", "f4"], document.GetReferencedFields().Order());
    }

    [Fact]
    public void ExpandIncludes_DefaultDepthLimit_AllowsTenLevels()
    {
        // Arrange
        var includes = Enumerable.Range(0, 10).ToDictionary(i => $"i{i}", i => i == 9 ? "end:1" : $"@include:i{i + 1}");
        var context = new QueryVisitorContext();

        // Act
        var result = LuceneQuery.Parse("@include:i0").Document.ExpandIncludes(includes, context);

        // Assert
        Assert.True(context.ValidationResult.IsValid, context.ValidationResult.Message);
        Assert.Equal(["end"], result.GetReferencedFields());
    }

    [Fact]
    public void ExpandIncludes_FanOutBomb_StopsAtExpansionCapQuickly()
    {
        // Arrange
        string fanOut(string next) => string.Join(" OR ", Enumerable.Repeat($"@include:{next}", 10));
        var includes = new Dictionary<string, string>
        {
            ["l0"] = fanOut("l1"),
            ["l1"] = fanOut("l2"),
            ["l2"] = fanOut("l3"),
            ["l3"] = fanOut("l4"),
            ["l4"] = fanOut("l5"),
            ["l5"] = fanOut("l6"),
            ["l6"] = fanOut("l7"),
            ["l7"] = fanOut("l8"),
            ["l8"] = "leaf:1"
        };
        var document = LuceneQuery.Parse("@include:l0").Document;
        var context = new QueryVisitorContext();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = document.ExpandIncludes(includes, context);

        // Assert
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Expansion took {stopwatch.Elapsed}.");
        var error = Assert.Single(context.ValidationResult.ValidationErrors);
        Assert.Equal(QueryErrorCode.MaxDepthExceeded, error.Code);
        Assert.Contains("100", error.Message);

        int groups = 0;
        result.Walk(n =>
        {
            if (n is GroupNode)
                groups++;
        });
        Assert.Equal(100, groups);
    }

    [Fact]
    public void ExpandIncludes_CustomExpansionLimit_IsEnforced()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:simple @include:simple @include:simple").Document;
        var context = new QueryVisitorContext { ValidationOptions = new QueryValidationOptions { MaxIncludeExpansions = 2 } };

        // Act
        var result = document.ExpandIncludes(CreateIncludes(), context);

        // Assert
        Assert.Equal("(bool +(group status:active) +(group status:active) +@include:simple)", result.ToDebugString());
        Assert.Single(context.ValidationResult.ValidationErrors);
    }

    [Fact]
    public void ExpandIncludes_SameIncludeManyTimes_ParsesIncludeTextOnce()
    {
        // Arrange
        var includes = new CountingDictionary(new Dictionary<string, string> { ["simple"] = "status:active" });
        var document = LuceneQuery.Parse(string.Join(" OR ", Enumerable.Repeat("@include:simple", 20))).Document;

        // Act
        document.ExpandIncludes(includes);

        // Assert
        Assert.Equal(1, includes.Lookups);
    }

    [Fact]
    public void ExpandIncludes_ExpandedIncludes_AreIndependentCopies()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:simple OR @include:simple").Document;

        // Act
        var result = document.ExpandIncludes(CreateIncludes());
        var boolean = (BooleanQueryNode)result.Query!;
        var first = (FieldQueryNode)((GroupNode)boolean.Clauses[0].Query!).Query!;
        first.Field = "changed";

        // Assert
        Assert.Equal("(bool ?(group changed:active) ?(group status:active))", result.ToDebugString());
    }

    [Fact]
    public void ExpandIncludes_ShouldSkipInclude_LeavesReferenceUnexpanded()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:simple @include:complex").Document;
        var context = new QueryVisitorContext
        {
            ShouldSkipInclude = (node, _) => IncludeVisitor.GetIncludeName(node) == "complex"
        };

        // Act
        var result = document.ExpandIncludes(CreateIncludes(), context);

        // Assert
        Assert.Equal("(bool +(group status:active) +@include:complex)", result.ToDebugString());
        Assert.Equal(["complex", "simple"], context.ValidationResult.ReferencedIncludes.Order());
        Assert.Empty(context.ValidationResult.UnresolvedIncludes);
    }

    [Fact]
    public void ExpandIncludes_AggregationExpression_ExpandsOnlyReferencesOutsideAggregations()
    {
        // Arrange
        var document = LuceneQuery.Parse("@include:aggs terms:(status @include:simple)").Document;
        var context = new QueryVisitorContext { QueryType = QueryType.Aggregation };
        var includes = new Dictionary<string, string> { ["aggs"] = "min:(created @include:simple)", ["simple"] = "status:active" };

        // Act
        var result = document.ExpandIncludes(includes, context);

        // Assert
        Assert.Equal("(bool +(group min:(group (bool +created +@include:simple))) +terms:(group (bool +status +@include:simple)))", result.ToDebugString());
        Assert.Equal(["aggs"], context.ValidationResult.ReferencedIncludes);
        Assert.Empty(context.ValidationResult.UnresolvedIncludes);
    }

    [Fact]
    public void ExpandIncludes_CaseInsensitiveDictionaryLookup_FallsBackToScan()
    {
        // Arrange
        var includes = new Dictionary<string, string>(StringComparer.Ordinal) { ["MixedCase"] = "m:1" };
        var document = LuceneQuery.Parse("@include:mixedcase").Document;

        // Act
        var result = document.ExpandIncludes(includes);

        // Assert
        Assert.Equal("(group m:1)", result.ToDebugString());
    }

    [Fact]
    public void IsInclude_And_GetIncludeName_IdentifyReferences()
    {
        var include = (FieldQueryNode)LuceneQuery.Parse("@Include:\"my query\"").Document.Query!;
        var other = (FieldQueryNode)LuceneQuery.Parse("include:x").Document.Query!;

        Assert.True(IncludeVisitor.IsInclude(include));
        Assert.Equal("my query", IncludeVisitor.GetIncludeName(include));
        Assert.False(IncludeVisitor.IsInclude(other));
    }

    [Fact]
    public void ExpandIncludes_NullArguments_ThrowArgumentNullException()
    {
        var document = LuceneQuery.Parse("a").Document;

        Assert.Throws<ArgumentNullException>(() => IncludeVisitor.ExpandIncludes(null!, CreateIncludes()));
        Assert.Throws<ArgumentNullException>(() => IncludeVisitor.ExpandIncludes(document, null!));
    }

    [Fact]
    public void ExpandIncludes_SharedIncludesConcurrently_ProducesConsistentResults()
    {
        // Arrange
        var includes = CreateIncludes();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Act
        Parallel.For(0, 500, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            var context = new QueryVisitorContext();
            string result = LuceneQuery.Parse($"n:{i} @include:deep").Document.ExpandIncludes(includes, context).ToDebugString();
            if (result != $"(bool +n:{i} +(group (group (bool +(group status:active) +c:3))))" || !context.ValidationResult.IsValid)
                failures.Add(result);
        });

        // Assert
        Assert.Empty(failures);
    }

    private static Dictionary<string, string> CreateIncludes() => new()
    {
        ["simple"] = "status:active",
        ["complex"] = "a:1 OR b:2",
        ["with space"] = "spaced:yes",
        ["nested"] = "@include:simple c:3",
        ["deep"] = "@include:nested"
    };

    private sealed class CountingDictionary(Dictionary<string, string> inner) : IReadOnlyDictionary<string, string>
    {
        public int Lookups { get; private set; }

        public string this[string key] => inner[key];

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<string> Values => inner.Values;

        public int Count => inner.Count;

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string value)
        {
            Lookups++;
            return inner.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
