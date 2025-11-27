namespace Foundatio.LuceneQueryParser.Ast;

/// <summary>
/// Represents the root of a parsed query.
/// </summary>
public class QueryDocument : QueryNode
{
    /// <summary>
    /// The root query expression.
    /// </summary>
    public QueryNode? Query { get; set; }
}
