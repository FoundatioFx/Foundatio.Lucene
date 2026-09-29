namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a boolean combination of clauses. Evaluation follows Lucene semantics: every
/// <see cref="Occur.Must"/> clause must match, no <see cref="Occur.MustNot"/> clause may match, and when there
/// are no <see cref="Occur.Must"/> clauses at least one <see cref="Occur.Should"/> clause must match (otherwise
/// should clauses only affect scoring). A node with only <see cref="Occur.MustNot"/> clauses matches everything
/// the clauses exclude.
/// </summary>
public class BooleanQueryNode : QueryNode
{
    /// <summary>
    /// The list of boolean clauses.
    /// </summary>
    public List<BooleanClause> Clauses { get; set; } = [];

    /// <inheritdoc/>
    public override QueryNode Clone()
    {
        var clauses = new List<BooleanClause>(Clauses.Count);
        foreach (var clause in Clauses)
            clauses.Add(clause.Clone());

        return CopyCommonTo(new BooleanQueryNode { Clauses = clauses });
    }
}

/// <summary>
/// Represents a single clause in a boolean query.
/// </summary>
public sealed class BooleanClause
{
    /// <summary>
    /// Creates an empty clause.
    /// </summary>
    public BooleanClause()
    {
    }

    /// <summary>
    /// Creates a clause for the specified query and occurrence.
    /// </summary>
    public BooleanClause(QueryNode? query, Occur occur)
    {
        Query = query;
        Occur = occur;
    }

    /// <summary>
    /// The query for this clause.
    /// </summary>
    public QueryNode? Query { get; set; }

    /// <summary>
    /// How the clause participates in matching. This is the semantic meaning of the clause.
    /// </summary>
    public Occur Occur { get; set; } = Occur.Should;

    /// <summary>
    /// The operator written before this clause (syntax only, used for round-tripping).
    /// </summary>
    public BooleanOperator Operator { get; set; } = BooleanOperator.Implicit;

    /// <summary>
    /// The prefix written before this clause (syntax only, used for round-tripping).
    /// </summary>
    public ClauseModifier Modifier { get; set; }

    /// <summary>
    /// Creates a deep copy of this clause.
    /// </summary>
    public BooleanClause Clone() => new(Query?.Clone(), Occur) { Operator = Operator, Modifier = Modifier };

    /// <inheritdoc/>
    public override string ToString() => $"{Occur}: {Query}";
}

/// <summary>
/// Defines how a clause occurs in a boolean query.
/// </summary>
public enum Occur
{
    /// <summary>The clause must match (<c>+</c> prefix or AND).</summary>
    Must,
    /// <summary>The clause should match (OR).</summary>
    Should,
    /// <summary>The clause must not match (<c>-</c> prefix, or NOT outside of an explicit OR).</summary>
    MustNot
}

/// <summary>
/// Defines the boolean operator between clauses.
/// </summary>
public enum BooleanOperator
{
    /// <summary>No operator was written; the default operator applies.</summary>
    Implicit,
    /// <summary>Explicit AND (or &amp;&amp;) operator.</summary>
    And,
    /// <summary>Explicit OR (or ||) operator.</summary>
    Or
}

/// <summary>
/// The prefix written before a clause.
/// </summary>
public enum ClauseModifier
{
    /// <summary>No prefix.</summary>
    None,
    /// <summary>The <c>+</c> prefix.</summary>
    Plus,
    /// <summary>The <c>-</c> prefix.</summary>
    Minus,
    /// <summary>The <c>NOT</c> keyword.</summary>
    Not,
    /// <summary>The <c>!</c> prefix.</summary>
    Bang
}
