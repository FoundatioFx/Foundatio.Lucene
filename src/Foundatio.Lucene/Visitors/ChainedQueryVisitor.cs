using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Ast;

/// <summary>
/// Generic abstract base class for query visitors with a typed context.
/// </summary>
/// <typeparam name="TContext">The type of visitor context.</typeparam>
public abstract class QueryVisitor<TContext> : IQueryVisitor<TContext>
    where TContext : IQueryVisitorContext
{
    /// <summary>
    /// Entry point for accepting a node. Dispatches to the appropriate typed Visit method.
    /// </summary>
    public virtual QueryNode Accept(QueryNode node, TContext context)
    {
        return node switch
        {
            QueryDocument doc => Visit(doc, context),
            GroupNode group => Visit(group, context),
            BooleanQueryNode boolQuery => Visit(boolQuery, context),
            FieldQueryNode fieldQuery => Visit(fieldQuery, context),
            TermNode term => Visit(term, context),
            PhraseNode phrase => Visit(phrase, context),
            RegexNode regex => Visit(regex, context),
            RangeNode range => Visit(range, context),
            NotNode not => Visit(not, context),
            ExistsNode exists => Visit(exists, context),
            MissingNode missing => Visit(missing, context),
            MatchAllNode matchAll => Visit(matchAll, context),
            MultiTermNode multiTerm => Visit(multiTerm, context),
            _ => node
        };
    }

    /// <summary>
    /// Visits a QueryDocument node.
    /// </summary>
    protected virtual QueryNode Visit(QueryDocument node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a GroupNode.
    /// </summary>
    protected virtual QueryNode Visit(GroupNode node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a BooleanQueryNode.
    /// </summary>
    protected virtual QueryNode Visit(BooleanQueryNode node, TContext context)
    {
        foreach (var clause in node.Clauses)
        {
            if (clause.Query is not null)
                clause.Query = Accept(clause.Query, context);
        }
        return node;
    }

    /// <summary>
    /// Visits a FieldQueryNode.
    /// </summary>
    protected virtual QueryNode Visit(FieldQueryNode node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a TermNode.
    /// </summary>
    protected virtual QueryNode Visit(TermNode node, TContext context) => node;

    /// <summary>
    /// Visits a PhraseNode.
    /// </summary>
    protected virtual QueryNode Visit(PhraseNode node, TContext context) => node;

    /// <summary>
    /// Visits a RegexNode.
    /// </summary>
    protected virtual QueryNode Visit(RegexNode node, TContext context) => node;

    /// <summary>
    /// Visits a RangeNode.
    /// </summary>
    protected virtual QueryNode Visit(RangeNode node, TContext context) => node;

    /// <summary>
    /// Visits a NotNode.
    /// </summary>
    protected virtual QueryNode Visit(NotNode node, TContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits an ExistsNode.
    /// </summary>
    protected virtual QueryNode Visit(ExistsNode node, TContext context) => node;

    /// <summary>
    /// Visits a MissingNode.
    /// </summary>
    protected virtual QueryNode Visit(MissingNode node, TContext context) => node;

    /// <summary>
    /// Visits a MatchAllNode.
    /// </summary>
    protected virtual QueryNode Visit(MatchAllNode node, TContext context) => node;

    /// <summary>
    /// Visits a MultiTermNode.
    /// </summary>
    protected virtual QueryNode Visit(MultiTermNode node, TContext context) => node;
}

/// <summary>
/// Non-generic abstract base class for query visitors that work with any context.
/// </summary>
public abstract class QueryVisitor : QueryVisitor<IQueryVisitorContext>, IQueryVisitor;

/// <summary>
/// A generic visitor that chains multiple visitors together, running them in sequence.
/// Each visitor is run with a priority (lower numbers run first).
/// </summary>
/// <typeparam name="TContext">The type of visitor context.</typeparam>
public class ChainedQueryVisitor<TContext> : IQueryVisitor<TContext>
    where TContext : IQueryVisitorContext
{
    private readonly List<VisitorWithPriority> _visitors = [];
    private VisitorWithPriority[]? _sortedVisitors;
    private bool _isDirty = true;

    /// <summary>
    /// Adds a visitor with the specified priority.
    /// </summary>
    /// <param name="visitor">The visitor to add.</param>
    /// <param name="priority">The priority (lower runs first). Default is 0.</param>
    public ChainedQueryVisitor<TContext> AddVisitor(IQueryVisitor<TContext> visitor, int priority = 0)
    {
        _visitors.Add(new VisitorWithPriority(visitor, priority));
        _isDirty = true;
        return this;
    }

    /// <summary>
    /// Removes a visitor of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of visitor to remove.</typeparam>
    public ChainedQueryVisitor<TContext> RemoveVisitor<T>() where T : IQueryVisitor<TContext>
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
    public ChainedQueryVisitor<TContext> ReplaceVisitor<T>(IQueryVisitor<TContext> visitor, int? newPriority = null) where T : IQueryVisitor<TContext>
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
    public ChainedQueryVisitor<TContext> AddVisitorBefore<T>(IQueryVisitor<TContext> visitor) where T : IQueryVisitor<TContext>
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
    public ChainedQueryVisitor<TContext> AddVisitorAfter<T>(IQueryVisitor<TContext> visitor) where T : IQueryVisitor<TContext>
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
    /// Visits a node by running all chained visitors in priority order.
    /// </summary>
    public QueryNode Accept(QueryNode node, TContext context)
    {
        EnsureSorted();

        foreach (var visitorEntry in _sortedVisitors!)
        {
            node = visitorEntry.Visitor.Accept(node, context);
        }

        return node;
    }

    private record VisitorWithPriority(IQueryVisitor<TContext> Visitor, int Priority);
}

/// <summary>
/// Non-generic chained query visitor that works with any context.
/// </summary>
public class ChainedQueryVisitor : ChainedQueryVisitor<IQueryVisitorContext>, IQueryVisitor;

/// <summary>
/// Extension methods for <see cref="IQueryVisitor"/>.
/// </summary>
public static class QueryVisitorExtensions
{
    /// <summary>
    /// Runs the visitor on a QueryDocument with a new context.
    /// </summary>
    public static QueryDocument Run(this IQueryVisitor visitor, QueryDocument document)
    {
        var context = new QueryVisitorContext();
        return (QueryDocument)visitor.Accept(document, context);
    }

    /// <summary>
    /// Runs the visitor on a QueryDocument with the provided context.
    /// </summary>
    public static QueryDocument Run<TContext>(this IQueryVisitor<TContext> visitor, QueryDocument document, TContext context)
        where TContext : IQueryVisitorContext
    {
        return (QueryDocument)visitor.Accept(document, context);
    }
}
