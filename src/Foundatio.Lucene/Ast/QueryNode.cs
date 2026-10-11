namespace Foundatio.Lucene.Ast;

/// <summary>
/// Base class for all AST nodes in the Lucene query tree.
/// </summary>
public abstract class QueryNode
{
    private Dictionary<string, object?>? _data;

    /// <summary>
    /// The start position in the source text (0-based).
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>
    /// The end position in the source text (exclusive).
    /// </summary>
    public int EndPosition { get; set; }

    /// <summary>
    /// The line number where this node starts (1-based).
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// The column number where this node starts (1-based).
    /// </summary>
    public int StartColumn { get; set; }

    /// <summary>
    /// Arbitrary metadata attached to this node by visitors and resolvers (for example the original field name
    /// before alias resolution, or a resolved field type). Allocated on first use.
    /// </summary>
    public IDictionary<string, object?> Data => _data ??= new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Whether any metadata has been attached to this node.
    /// </summary>
    public bool HasData => _data is { Count: > 0 };

    /// <summary>
    /// Gets a metadata value attached to this node.
    /// </summary>
    public T? GetData<T>(string key)
    {
        return _data is not null && _data.TryGetValue(key, out var value) && value is T typed ? typed : default;
    }

    /// <summary>
    /// Attaches a metadata value to this node, or removes it when <paramref name="value"/> is null.
    /// </summary>
    public void SetData(string key, object? value)
    {
        if (value is null)
            _data?.Remove(key);
        else
            Data[key] = value;
    }

    /// <summary>
    /// Creates a deep copy of this node and all of its descendants, including attached metadata.
    /// </summary>
    public abstract QueryNode Clone();

    /// <summary>
    /// Copies source position information and metadata from this node to <paramref name="target"/>.
    /// </summary>
    protected T CopyCommonTo<T>(T target) where T : QueryNode
    {
        target.StartPosition = StartPosition;
        target.EndPosition = EndPosition;
        target.StartLine = StartLine;
        target.StartColumn = StartColumn;
        if (_data is { Count: > 0 })
            target._data = new Dictionary<string, object?>(_data, StringComparer.Ordinal);

        return target;
    }
}

/// <summary>
/// A node that names a field: <see cref="FieldQueryNode"/>, <see cref="ExistsNode"/>, or <see cref="MissingNode"/>.
/// </summary>
public interface IFieldNode
{
    /// <summary>
    /// The field name.
    /// </summary>
    string Field { get; set; }

    /// <summary>
    /// The field name as a memory slice.
    /// </summary>
    ReadOnlyMemory<char> FieldMemory { get; set; }
}

/// <summary>
/// A node that can carry a boost modifier (<c>^value</c>).
/// </summary>
public interface IBoostable
{
    /// <summary>
    /// The raw boost modifier text, without the leading <c>^</c> or any quotes. Null when no boost was specified.
    /// Non-numeric values are meaningful to some consumers (for example a time zone on a date range).
    /// </summary>
    string? BoostText { get; set; }

    /// <summary>
    /// The numeric boost, or null when no boost was specified or <see cref="BoostText"/> is not a number.
    /// </summary>
    float? Boost { get; set; }
}

/// <summary>
/// A node that can carry a proximity modifier (<c>~value</c>) such as a fuzzy distance, phrase slop,
/// geo distance, or aggregation interval.
/// </summary>
public interface IProximityModifiable
{
    /// <summary>
    /// The raw proximity modifier text, without the leading <c>~</c>. Null when no modifier was specified,
    /// empty when a bare <c>~</c> was specified.
    /// </summary>
    string? ProximityText { get; set; }
}
