namespace Foundatio.LuceneQuery.Ast;

/// <summary>
/// Represents a NOT query (negation).
/// </summary>
public class NotNode : QueryNode
{
    /// <summary>
    /// The negated query.
    /// </summary>
    public QueryNode? Query { get; set; }
}
