namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a parenthesized group of queries.
/// </summary>
public class GroupNode : QueryNode, IBoostable
{
    /// <summary>
    /// The inner query.
    /// </summary>
    public QueryNode? Query { get; set; }

    /// <inheritdoc/>
    public string? BoostText { get; set; }

    /// <inheritdoc/>
    public float? Boost
    {
        get => Modifiers.ParseBoost(BoostText);
        set => BoostText = Modifiers.FormatBoost(value);
    }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new GroupNode { Query = Query?.Clone(), BoostText = BoostText });
}
