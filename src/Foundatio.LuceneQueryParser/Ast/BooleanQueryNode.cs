namespace Foundatio.LuceneQueryParser.Ast;

/// <summary>
/// Represents a boolean query with AND/OR operators.
/// </summary>
public class BooleanQueryNode : QueryNode
{
    /// <summary>
    /// The list of boolean clauses.
    /// </summary>
    public List<BooleanClause> Clauses { get; set; } = [];
}

/// <summary>
/// Represents a single clause in a boolean query.
/// </summary>
public sealed record BooleanClause
{
    /// <summary>
    /// The query for this clause.
    /// </summary>
    public QueryNode? Query { get; set; }

    /// <summary>
    /// The occurrence type (MUST, SHOULD, MUST_NOT).
    /// </summary>
    public Occur Occur { get; set; } = Occur.Should;

    /// <summary>
    /// The operator used to combine with the previous clause (AND, OR, implicit).
    /// </summary>
    public BooleanOperator Operator { get; set; } = BooleanOperator.Implicit;
}

/// <summary>
/// Defines how a clause occurs in a boolean query.
/// </summary>
public enum Occur
{
    /// <summary>The clause must match (+ prefix or AND).</summary>
    Must,
    /// <summary>The clause should match (default).</summary>
    Should,
    /// <summary>The clause must not match (- prefix or NOT).</summary>
    MustNot
}

/// <summary>
/// Defines the boolean operator between clauses.
/// </summary>
public enum BooleanOperator
{
    /// <summary>Implicit operator (default OR in Elasticsearch).</summary>
    Implicit,
    /// <summary>Explicit AND operator.</summary>
    And,
    /// <summary>Explicit OR operator.</summary>
    Or
}
