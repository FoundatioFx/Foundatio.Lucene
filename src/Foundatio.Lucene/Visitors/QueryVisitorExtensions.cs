using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Extension methods for <see cref="IQueryVisitor"/>.
/// </summary>
public static class QueryVisitorExtensions
{
    /// <summary>
    /// Runs the visitor on a node with a new context.
    /// </summary>
    public static QueryNode Run(this IQueryVisitor visitor, QueryNode node)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentNullException.ThrowIfNull(node);
        return visitor.Accept(node, new QueryVisitorContext());
    }

    /// <summary>
    /// Runs the visitor on a node with the provided context.
    /// </summary>
    public static QueryNode Run<TContext>(this IQueryVisitor<TContext> visitor, QueryNode node, TContext context)
        where TContext : IQueryVisitorContext
    {
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentNullException.ThrowIfNull(node);
        return visitor.Accept(node, context);
    }
}
