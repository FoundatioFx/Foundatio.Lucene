using System.Linq.Expressions;
using System.Reflection;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// A sort built from a sort expression such as <c>-created +name</c>. It is immutable and can be applied to any
/// number of queries.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class EntityFrameworkSort<T> where T : class
{
    private readonly SortKey[] _keys;

    internal EntityFrameworkSort(IReadOnlyList<SortField> fields, SortKey[] keys)
    {
        Fields = fields;
        _keys = keys;
    }

    /// <summary>
    /// The fields to sort by, in order.
    /// </summary>
    public IReadOnlyList<SortField> Fields { get; }

    /// <summary>
    /// Orders <paramref name="source"/> by the sort fields using <c>OrderBy</c>, <c>OrderByDescending</c>,
    /// <c>ThenBy</c>, and <c>ThenByDescending</c>.
    /// </summary>
    public IOrderedQueryable<T> Apply(IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var expression = source.Expression;
        for (int i = 0; i < _keys.Length; i++)
        {
            var key = _keys[i];
            bool descending = Fields[i].Direction == SortDirection.Descending;
            var method = i == 0
                ? descending ? key.OrderByDescending : key.OrderBy
                : descending ? key.ThenByDescending : key.ThenBy;

            expression = Expression.Call(null, method, expression, Expression.Quote(key.Selector));
        }

        return (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(expression);
    }
}

/// <summary>
/// A key selector for one sortable field and the closed <see cref="Queryable"/> ordering methods for it.
/// </summary>
internal sealed class SortKey
{
    private static readonly MethodInfo OrderByDefinition = GetDefinition(nameof(Queryable.OrderBy));
    private static readonly MethodInfo OrderByDescendingDefinition = GetDefinition(nameof(Queryable.OrderByDescending));
    private static readonly MethodInfo ThenByDefinition = GetDefinition(nameof(Queryable.ThenBy));
    private static readonly MethodInfo ThenByDescendingDefinition = GetDefinition(nameof(Queryable.ThenByDescending));

    public SortKey(LambdaExpression selector)
    {
        Selector = selector;
        var source = selector.Parameters[0].Type;
        var key = selector.ReturnType;
        OrderBy = OrderByDefinition.MakeGenericMethod(source, key);
        OrderByDescending = OrderByDescendingDefinition.MakeGenericMethod(source, key);
        ThenBy = ThenByDefinition.MakeGenericMethod(source, key);
        ThenByDescending = ThenByDescendingDefinition.MakeGenericMethod(source, key);
    }

    public LambdaExpression Selector { get; }

    public MethodInfo OrderBy { get; }

    public MethodInfo OrderByDescending { get; }

    public MethodInfo ThenBy { get; }

    public MethodInfo ThenByDescending { get; }

    private static MethodInfo GetDefinition(string name)
    {
        return typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name && m.GetParameters().Length == 2);
    }
}
