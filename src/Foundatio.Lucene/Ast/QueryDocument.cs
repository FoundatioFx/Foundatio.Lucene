namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents the root of a parsed query.
/// </summary>
public class QueryDocument : QueryNode
{
    /// <summary>
    /// The root query expression. Null for an empty query.
    /// </summary>
    public QueryNode? Query { get; set; }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new QueryDocument { Query = Query?.Clone() });

    /// <summary>
    /// Creates a deep copy of this document.
    /// </summary>
    public QueryDocument CloneDocument() => (QueryDocument)Clone();
}
