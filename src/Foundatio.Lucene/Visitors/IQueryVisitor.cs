using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Interface for a chainable query visitor that can modify query nodes.
/// </summary>
public interface IQueryVisitor
{
    /// <summary>
    /// Visits a query node and returns the (potentially modified) node.
    /// </summary>
    /// <param name="node">The node to visit.</param>
    /// <param name="context">The visitor context for sharing state.</param>
    /// <returns>The original or modified node.</returns>
    QueryNode Accept(QueryNode node, IQueryVisitorContext context);
}
