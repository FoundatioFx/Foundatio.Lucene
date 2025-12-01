using Foundatio.LuceneQuery.Ast;

namespace Foundatio.LuceneQuery.Visitors;

/// <summary>
/// Interface for a chainable query visitor that can modify query nodes asynchronously.
/// </summary>
public interface IQueryNodeVisitor
{
    /// <summary>
    /// Visits a query node asynchronously and returns the (potentially modified) node.
    /// </summary>
    /// <param name="node">The node to visit.</param>
    /// <param name="context">The visitor context for sharing state.</param>
    /// <returns>The original or modified node.</returns>
    Task<QueryNode> AcceptAsync(QueryNode node, IQueryVisitorContext context);
}
