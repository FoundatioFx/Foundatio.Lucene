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

}

/// <summary>
/// Non-generic abstract base class for query visitors that work with any context.
/// </summary>
public abstract class QueryVisitor : QueryVisitor<IQueryVisitorContext>, IQueryVisitor;

/// <summary>
/// Runs a sequence of visitors in priority order (lower first; equal priorities run in the order they were added).
/// Each visitor receives the node returned by the previous one. The chain is safe to run concurrently; changes to
/// the chain publish a new snapshot and do not affect runs already in progress.
/// </summary>
/// <typeparam name="TContext">The type of visitor context.</typeparam>
public class ChainedQueryVisitor<TContext> : IQueryVisitor<TContext>
    where TContext : IQueryVisitorContext
{
    private readonly object _lock = new();
    private readonly List<Entry> _entries = [];
    private Entry[] _snapshot = [];
    private long _sequence;

    /// <summary>
    /// The visitors in the order they run.
    /// </summary>
    public IReadOnlyList<IQueryVisitor<TContext>> Visitors => Array.ConvertAll(Volatile.Read(ref _snapshot), e => e.Visitor);

    /// <summary>
    /// Adds a visitor with the specified priority (lower runs first).
    /// </summary>
    public ChainedQueryVisitor<TContext> AddVisitor(IQueryVisitor<TContext> visitor, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        lock (_lock)
        {
            _entries.Add(new Entry(visitor, priority, _sequence++));
            Publish();
        }

        return this;
    }

    /// <summary>
    /// Removes all visitors of type <typeparamref name="T"/>.
    /// </summary>
    public ChainedQueryVisitor<TContext> RemoveVisitor<T>() where T : IQueryVisitor<TContext>
    {
        lock (_lock)
        {
            if (_entries.RemoveAll(e => e.Visitor is T) > 0)
                Publish();
        }

        return this;
    }

    /// <summary>
    /// Replaces the visitors of type <typeparamref name="T"/> with <paramref name="visitor"/>, keeping the
    /// priority of the first one replaced unless <paramref name="newPriority"/> is specified. When there is no
    /// visitor of type <typeparamref name="T"/>, <paramref name="visitor"/> is added.
    /// </summary>
    public ChainedQueryVisitor<TContext> ReplaceVisitor<T>(IQueryVisitor<TContext> visitor, int? newPriority = null) where T : IQueryVisitor<TContext>
    {
        ArgumentNullException.ThrowIfNull(visitor);
        lock (_lock)
        {
            int index = _entries.FindIndex(e => e.Visitor is T);
            int priority = newPriority ?? (index >= 0 ? _entries[index].Priority : 0);
            long sequence = index >= 0 ? _entries[index].Sequence : _sequence++;
            _entries.RemoveAll(e => e.Visitor is T);
            _entries.Add(new Entry(visitor, priority, sequence));
            Publish();
        }

        return this;
    }

    /// <summary>
    /// Adds a visitor that runs immediately before the first visitor of type <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no visitor of type <typeparamref name="T"/>.</exception>
    public ChainedQueryVisitor<TContext> AddVisitorBefore<T>(IQueryVisitor<TContext> visitor) where T : IQueryVisitor<TContext>
    {
        return AddRelative<T>(visitor, before: true);
    }

    /// <summary>
    /// Adds a visitor that runs immediately after the last visitor of type <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no visitor of type <typeparamref name="T"/>.</exception>
    public ChainedQueryVisitor<TContext> AddVisitorAfter<T>(IQueryVisitor<TContext> visitor) where T : IQueryVisitor<TContext>
    {
        return AddRelative<T>(visitor, before: false);
    }

    private ChainedQueryVisitor<TContext> AddRelative<T>(IQueryVisitor<TContext> visitor, bool before) where T : IQueryVisitor<TContext>
    {
        ArgumentNullException.ThrowIfNull(visitor);
        lock (_lock)
        {
            var ordered = Volatile.Read(ref _snapshot);
            int index = before ? Array.FindIndex(ordered, e => e.Visitor is T) : Array.FindLastIndex(ordered, e => e.Visitor is T);
            if (index < 0)
                throw new InvalidOperationException($"The chain does not contain a visitor of type {typeof(T).Name}.");

            var reference = ordered[index];
            _entries.Add(before
                ? new Entry(visitor, reference.Priority, reference.Sequence, BeforeReference: true)
                : new Entry(visitor, reference.Priority, reference.Sequence, AfterReference: true));
            Publish();
        }

        return this;
    }

    private void Publish()
    {
        var ordered = _entries.ToArray();
        Array.Sort(ordered, static (a, b) =>
        {
            int result = a.Priority.CompareTo(b.Priority);
            if (result != 0)
                return result;

            result = a.Sequence.CompareTo(b.Sequence);
            if (result != 0)
                return result;

            return a.Order.CompareTo(b.Order);
        });
        Volatile.Write(ref _snapshot, ordered);
    }

    /// <summary>
    /// Runs the visitors in order.
    /// </summary>
    public QueryNode Accept(QueryNode node, TContext context)
    {
        foreach (var entry in Volatile.Read(ref _snapshot))
            node = entry.Visitor.Accept(node, context);

        return node;
    }

    private sealed record Entry(IQueryVisitor<TContext> Visitor, int Priority, long Sequence, bool BeforeReference = false, bool AfterReference = false)
    {
        public int Order => BeforeReference ? -1 : AfterReference ? 1 : 0;
    }
}

/// <summary>
/// A chain of visitors that work with any context.
/// </summary>
public class ChainedQueryVisitor : ChainedQueryVisitor<IQueryVisitorContext>, IQueryVisitor;

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
