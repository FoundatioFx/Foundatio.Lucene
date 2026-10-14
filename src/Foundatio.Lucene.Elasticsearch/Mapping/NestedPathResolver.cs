namespace Foundatio.Lucene.Elasticsearch;

/// <summary>Resolves the <c>nested</c> paths that enclose a field.</summary>
public static class NestedPathResolver
{
    private const string NestedAggregationPrefix = "nested_";

    /// <summary>Returns the standard nested aggregation name for a path (for example <c>parent</c> becomes <c>nested_parent</c>).</summary>
    public static string GetNestedAggName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return NestedAggregationPrefix + path;
    }

    /// <summary>
    /// Returns the canonical path of the deepest <c>nested</c> property enclosing <paramref name="fullName"/>
    /// (including the field itself when it is nested), or <see langword="null"/> when it is not nested.
    /// </summary>
    /// <remarks>Nested ancestors are found even when the field itself is not mapped.</remarks>
    public static string? GetDeepestNestedPath(string fullName, ElasticMappingResolver mappingResolver)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        ArgumentNullException.ThrowIfNull(mappingResolver);

        return mappingResolver.GetMapping(fullName, followAlias: true).NestedPath;
    }

    /// <summary>
    /// Returns the canonical nested paths from outermost to innermost that enclose <paramref name="deepestPath"/>,
    /// or a single-element list containing <paramref name="deepestPath"/> when none are mapped.
    /// </summary>
    public static IReadOnlyList<string> GetNestedPathChain(string deepestPath, ElasticMappingResolver mappingResolver)
    {
        ArgumentNullException.ThrowIfNull(deepestPath);
        ArgumentNullException.ThrowIfNull(mappingResolver);

        var chain = mappingResolver.GetMapping(deepestPath, followAlias: true).NestedPathChain;
        return chain.Count > 0 ? chain : [deepestPath];
    }
}
