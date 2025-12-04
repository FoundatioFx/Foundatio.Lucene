using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Ast;

/// <summary>
/// Interface for a chainable query visitor that can modify query nodes asynchronously.
/// </summary>
public abstract class QueryNodeVisitor : IQueryNodeVisitor
{
    /// <summary>
    /// Entry point for accepting a node. Dispatches to the appropriate typed Visit method.
    /// </summary>
    public virtual Task<QueryNode> AcceptAsync(QueryNode node, IQueryVisitorContext context)
    {
        return node switch
        {
            QueryDocument doc => VisitAsync(doc, context),
            GroupNode group => VisitAsync(group, context),
            BooleanQueryNode boolQuery => VisitAsync(boolQuery, context),
            FieldQueryNode fieldQuery => VisitAsync(fieldQuery, context),
            TermNode term => VisitAsync(term, context),
            PhraseNode phrase => VisitAsync(phrase, context),
            RegexNode regex => VisitAsync(regex, context),
            RangeNode range => VisitAsync(range, context),
            NotNode not => VisitAsync(not, context),
            ExistsNode exists => VisitAsync(exists, context),
            MissingNode missing => VisitAsync(missing, context),
            MatchAllNode matchAll => VisitAsync(matchAll, context),
            MultiTermNode multiTerm => VisitAsync(multiTerm, context),
            _ => Task.FromResult(node)
        };
    }

    /// <summary>
    /// Visits a QueryDocument node.
    /// </summary>
    public virtual async Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = await AcceptAsync(node.Query, context).ConfigureAwait(false);
        return node;
    }

    /// <summary>
    /// Visits a GroupNode.
    /// </summary>
    public virtual async Task<QueryNode> VisitAsync(GroupNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = await AcceptAsync(node.Query, context).ConfigureAwait(false);
        return node;
    }

    /// <summary>
    /// Visits a BooleanQueryNode.
    /// </summary>
    public virtual async Task<QueryNode> VisitAsync(BooleanQueryNode node, IQueryVisitorContext context)
    {
        foreach (var clause in node.Clauses)
        {
            if (clause.Query is not null)
                clause.Query = await AcceptAsync(clause.Query, context).ConfigureAwait(false);
        }
        return node;
    }

    /// <summary>
    /// Visits a FieldQueryNode.
    /// </summary>
    public virtual async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = await AcceptAsync(node.Query, context).ConfigureAwait(false);
        return node;
    }

    /// <summary>
    /// Visits a TermNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);

    /// <summary>
    /// Visits a PhraseNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);

    /// <summary>
    /// Visits a RegexNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(RegexNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);

    /// <summary>
    /// Visits a RangeNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);

    /// <summary>
    /// Visits a NotNode.
    /// </summary>
    public virtual async Task<QueryNode> VisitAsync(NotNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = await AcceptAsync(node.Query, context).ConfigureAwait(false);
        return node;
    }

    /// <summary>
    /// Visits an ExistsNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(ExistsNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);

    /// <summary>
    /// Visits a MissingNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(MissingNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);

    /// <summary>
    /// Visits a MatchAllNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(MatchAllNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);

    /// <summary>
    /// Visits a MultiTermNode.
    /// </summary>
    public virtual Task<QueryNode> VisitAsync(MultiTermNode node, IQueryVisitorContext context) => Task.FromResult<QueryNode>(node);
}

/// <summary>
/// A visitor that chains multiple visitors together, running them in sequence.
/// Each visitor is run with a priority (lower numbers run first).
/// </summary>
public class ChainedQueryVisitor : IQueryNodeVisitor
{
    private readonly List<VisitorWithPriority> _visitors = [];
    private VisitorWithPriority[]? _sortedVisitors;
    private bool _isDirty = true;

    /// <summary>
    /// Adds a visitor with the specified priority.
    /// </summary>
    /// <param name="visitor">The visitor to add.</param>
    /// <param name="priority">The priority (lower runs first). Default is 0.</param>
    public ChainedQueryVisitor AddVisitor(IQueryNodeVisitor visitor, int priority = 0)
    {
        _visitors.Add(new VisitorWithPriority(visitor, priority));
        _isDirty = true;
        return this;
    }

    /// <summary>
    /// Removes a visitor of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of visitor to remove.</typeparam>
    public ChainedQueryVisitor RemoveVisitor<T>() where T : IQueryNodeVisitor
    {
        var visitor = _visitors.Find(v => v.Visitor is T);
        if (visitor is not null)
        {
            _visitors.Remove(visitor);
            _isDirty = true;
        }
        return this;
    }

    /// <summary>
    /// Replaces a visitor of the specified type with a new visitor.
    /// </summary>
    /// <typeparam name="T">The type of visitor to replace.</typeparam>
    /// <param name="visitor">The new visitor.</param>
    /// <param name="newPriority">Optional new priority. If not specified, keeps the original priority.</param>
    public ChainedQueryVisitor ReplaceVisitor<T>(IQueryNodeVisitor visitor, int? newPriority = null) where T : IQueryNodeVisitor
    {
        var existing = _visitors.Find(v => v.Visitor is T);
        if (existing is not null)
        {
            int priority = newPriority ?? existing.Priority;
            _visitors.Remove(existing);
            _visitors.Add(new VisitorWithPriority(visitor, priority));
            _isDirty = true;
        }
        else
        {
            AddVisitor(visitor, newPriority ?? 0);
        }
        return this;
    }

    /// <summary>
    /// Adds a visitor to run before a specific visitor type.
    /// </summary>
    /// <typeparam name="T">The type of visitor to run before.</typeparam>
    /// <param name="visitor">The visitor to add.</param>
    public ChainedQueryVisitor AddVisitorBefore<T>(IQueryNodeVisitor visitor) where T : IQueryNodeVisitor
    {
        var reference = _visitors.Find(v => v.Visitor is T);
        int priority = reference?.Priority - 1 ?? 0;
        return AddVisitor(visitor, priority);
    }

    /// <summary>
    /// Adds a visitor to run after a specific visitor type.
    /// </summary>
    /// <typeparam name="T">The type of visitor to run after.</typeparam>
    /// <param name="visitor">The visitor to add.</param>
    public ChainedQueryVisitor AddVisitorAfter<T>(IQueryNodeVisitor visitor) where T : IQueryNodeVisitor
    {
        var reference = _visitors.Find(v => v.Visitor is T);
        int priority = reference?.Priority + 1 ?? 0;
        return AddVisitor(visitor, priority);
    }

    private void EnsureSorted()
    {
        if (_isDirty)
        {
            _sortedVisitors = [.. _visitors.OrderBy(v => v.Priority)];
            _isDirty = false;
        }
    }

    /// <summary>
    /// Visits a node asynchronously by running all chained visitors in priority order.
    /// </summary>
    public async Task<QueryNode> AcceptAsync(QueryNode node, IQueryVisitorContext context)
    {
        EnsureSorted();

        foreach (var visitorEntry in _sortedVisitors!)
        {
            node = await visitorEntry.Visitor.AcceptAsync(node, context).ConfigureAwait(false);
        }

        return node;
    }

    private record VisitorWithPriority(IQueryNodeVisitor Visitor, int Priority);
}

/// <summary>
/// Extension methods for <see cref="IQueryNodeVisitor"/>.
/// </summary>
public static class QueryNodeVisitorExtensions
{
    /// <summary>
    /// Runs the visitor on a QueryDocument asynchronously with a new context.
    /// </summary>
    public static async Task<QueryDocument> RunAsync(this IQueryNodeVisitor visitor, QueryDocument document)
    {
        var context = new QueryVisitorContext();
        return (QueryDocument)await visitor.AcceptAsync(document, context).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the visitor on a QueryDocument asynchronously with the provided context.
    /// </summary>
    public static async Task<QueryDocument> RunAsync(this IQueryNodeVisitor visitor, QueryDocument document, IQueryVisitorContext context)
    {
        return (QueryDocument)await visitor.AcceptAsync(document, context).ConfigureAwait(false);
    }
}
