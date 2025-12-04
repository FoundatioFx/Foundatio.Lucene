namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a grouping of queries with parentheses.
/// </summary>
public class GroupNode : QueryNode
{
    /// <summary>
    /// The inner query.
    /// </summary>
    public QueryNode? Query { get; set; }

    /// <summary>
    /// Optional boost value.
    /// </summary>
    public float? Boost { get; set; }
}
