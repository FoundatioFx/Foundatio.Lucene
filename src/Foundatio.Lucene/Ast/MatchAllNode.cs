namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents the match all query (<c>*</c> or <c>*:*</c>).
/// </summary>
public class MatchAllNode : QueryNode
{
    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new MatchAllNode());
}
