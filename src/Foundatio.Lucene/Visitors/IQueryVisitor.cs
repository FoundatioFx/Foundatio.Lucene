using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Generic interface for a query visitor with a typed context.
/// Contravariant in TContext: a visitor that accepts any context can be used
/// where a more specific context is expected.
/// </summary>
/// <typeparam name="TContext">The type of visitor context.</typeparam>
public interface IQueryVisitor<in TContext> where TContext : IQueryVisitorContext
{
    /// <summary>
    /// Visits a query node and returns the (potentially modified) node.
    /// </summary>
    /// <param name="node">The node to visit.</param>
    /// <param name="context">The visitor context for sharing state.</param>
    /// <returns>The original or modified node.</returns>
    QueryNode Accept(QueryNode node, TContext context);
}

/// <summary>
/// Non-generic interface for visitors that work with any context.
/// </summary>
public interface IQueryVisitor : IQueryVisitor<IQueryVisitorContext>;
