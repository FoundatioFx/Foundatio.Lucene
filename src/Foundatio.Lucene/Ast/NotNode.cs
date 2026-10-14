namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a boolean negation, produced for <c>NOT x</c> when it is an alternative of an explicit OR
/// (for example <c>a OR NOT b</c>). Elsewhere <c>NOT x</c> excludes like <c>-x</c> and is represented as a
/// <see cref="Occur.MustNot"/> clause.
/// </summary>
public class NotNode : QueryNode
{
    /// <summary>
    /// The negated query.
    /// </summary>
    public QueryNode? Query { get; set; }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new NotNode { Query = Query?.Clone() });
}
