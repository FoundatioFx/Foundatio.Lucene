using System.Linq.Expressions;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Lets custom visitors replace the predicate built for a query node.
/// </summary>
public static class EntityFrameworkQueryNodeExtensions
{
    /// <summary>
    /// The <see cref="QueryNode.Data"/> key holding a replacement predicate.
    /// </summary>
    public const string FilterExpressionKey = "@EntityFrameworkFilter";

    /// <summary>
    /// Uses <paramref name="filter"/> (a predicate over the queried entity, such as
    /// <c>(Employee e) =&gt; e.Tags.Any(t =&gt; t.Name == "x")</c>) for this node instead of translating it. Boolean
    /// logic around the node (for example a <c>-</c> prefix) still applies.
    /// </summary>
    public static void SetFilterExpression(this QueryNode node, LambdaExpression filter)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(filter);
        node.SetData(FilterExpressionKey, filter);
    }

    /// <summary>
    /// Gets the replacement predicate set with <see cref="SetFilterExpression"/>, or null.
    /// </summary>
    public static LambdaExpression? GetFilterExpression(this QueryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.GetData<LambdaExpression>(FilterExpressionKey);
    }
}
