using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

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
            InsertByPriority(new Entry(visitor, priority));
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
    /// position and priority of the first one replaced unless a different <paramref name="newPriority"/> is
    /// specified. When there is no visitor of type <typeparamref name="T"/>, <paramref name="visitor"/> is added.
    /// </summary>
    public ChainedQueryVisitor<TContext> ReplaceVisitor<T>(IQueryVisitor<TContext> visitor, int? newPriority = null) where T : IQueryVisitor<TContext>
    {
        ArgumentNullException.ThrowIfNull(visitor);
        lock (_lock)
        {
            int index = _entries.FindIndex(e => e.Visitor is T);
            if (index < 0)
            {
                InsertByPriority(new Entry(visitor, newPriority ?? 0));
            }
            else
            {
                int priority = _entries[index].Priority;
                _entries.RemoveAll(e => e.Visitor is T);
                if (newPriority is null || newPriority == priority)
                    _entries.Insert(index, new Entry(visitor, priority));
                else
                    InsertByPriority(new Entry(visitor, newPriority.Value));
            }

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
            int index = before ? _entries.FindIndex(e => e.Visitor is T) : _entries.FindLastIndex(e => e.Visitor is T);
            if (index < 0)
                throw new InvalidOperationException($"The chain does not contain a visitor of type {typeof(T).Name}.");

            _entries.Insert(before ? index : index + 1, new Entry(visitor, _entries[index].Priority));
            Publish();
        }

        return this;
    }

    private void InsertByPriority(Entry entry)
    {
        int index = _entries.FindLastIndex(e => e.Priority <= entry.Priority);
        _entries.Insert(index + 1, entry);
    }

    private void Publish()
    {
        Volatile.Write(ref _snapshot, _entries.ToArray());
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

    private sealed record Entry(IQueryVisitor<TContext> Visitor, int Priority);
}

/// <summary>
/// A chain of visitors that work with any context.
/// </summary>
public class ChainedQueryVisitor : ChainedQueryVisitor<IQueryVisitorContext>, IQueryVisitor;
