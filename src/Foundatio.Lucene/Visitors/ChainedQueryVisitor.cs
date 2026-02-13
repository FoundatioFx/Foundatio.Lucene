using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Ast;

/// <summary>
/// Abstract base class for query visitors that can modify query nodes.
/// </summary>
public abstract class QueryVisitor : IQueryVisitor
{
    /// <summary>
    /// Entry point for accepting a node. Dispatches to the appropriate typed Visit method.
    /// </summary>
    public virtual QueryNode Accept(QueryNode node, IQueryVisitorContext context)
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
    protected virtual QueryNode Visit(QueryDocument node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a GroupNode.
    /// </summary>
    protected virtual QueryNode Visit(GroupNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a BooleanQueryNode.
    /// </summary>
    protected virtual QueryNode Visit(BooleanQueryNode node, IQueryVisitorContext context)
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
    protected virtual QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits a TermNode.
    /// </summary>
    protected virtual QueryNode Visit(TermNode node, IQueryVisitorContext context) => node;

    /// <summary>
    /// Visits a PhraseNode.
    /// </summary>
    protected virtual QueryNode Visit(PhraseNode node, IQueryVisitorContext context) => node;

    /// <summary>
    /// Visits a RegexNode.
    /// </summary>
    protected virtual QueryNode Visit(RegexNode node, IQueryVisitorContext context) => node;

    /// <summary>
    /// Visits a RangeNode.
    /// </summary>
    protected virtual QueryNode Visit(RangeNode node, IQueryVisitorContext context) => node;

    /// <summary>
    /// Visits a NotNode.
    /// </summary>
    protected virtual QueryNode Visit(NotNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
            node.Query = Accept(node.Query, context);
        return node;
    }

    /// <summary>
    /// Visits an ExistsNode.
    /// </summary>
    protected virtual QueryNode Visit(ExistsNode node, IQueryVisitorContext context) => node;

    /// <summary>
    /// Visits a MissingNode.
    /// </summary>
    protected virtual QueryNode Visit(MissingNode node, IQueryVisitorContext context) => node;

    /// <summary>
    /// Visits a MatchAllNode.
    /// </summary>
    protected virtual QueryNode Visit(MatchAllNode node, IQueryVisitorContext context) => node;

    /// <summary>
    /// Visits a MultiTermNode.
    /// </summary>
    protected virtual QueryNode Visit(MultiTermNode node, IQueryVisitorContext context) => node;
}

/// <summary>
/// A visitor that chains multiple visitors together, running them in sequence.
/// Each visitor is run with a priority (lower numbers run first).
/// </summary>
public class ChainedQueryVisitor : IQueryVisitor
{
    private readonly List<VisitorWithPriority> _visitors = [];
    private VisitorWithPriority[]? _sortedVisitors;
    private bool _isDirty = true;

    /// <summary>
    /// Adds a visitor with the specified priority.
    /// </summary>
    /// <param name="visitor">The visitor to add.</param>
    /// <param name="priority">The priority (lower runs first). Default is 0.</param>
    public ChainedQueryVisitor AddVisitor(IQueryVisitor visitor, int priority = 0)
    {
        _visitors.Add(new VisitorWithPriority(visitor, priority));
        _isDirty = true;
        return this;
    }

    /// <summary>
    /// Removes a visitor of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of visitor to remove.</typeparam>
    public ChainedQueryVisitor RemoveVisitor<T>() where T : IQueryVisitor
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
    public ChainedQueryVisitor ReplaceVisitor<T>(IQueryVisitor visitor, int? newPriority = null) where T : IQueryVisitor
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
    public ChainedQueryVisitor AddVisitorBefore<T>(IQueryVisitor visitor) where T : IQueryVisitor
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
    public ChainedQueryVisitor AddVisitorAfter<T>(IQueryVisitor visitor) where T : IQueryVisitor
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
    public QueryNode Accept(QueryNode node, IQueryVisitorContext context)
    {
        EnsureSorted();

        foreach (var visitorEntry in _sortedVisitors!)
        {
            node = visitorEntry.Visitor.Accept(node, context);
        }

        return node;
    }

    private record VisitorWithPriority(IQueryVisitor Visitor, int Priority);
}

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
    public static QueryDocument Run(this IQueryVisitor visitor, QueryDocument document, IQueryVisitorContext context)
    {
        return (QueryDocument)visitor.Accept(document, context);
    }
}
