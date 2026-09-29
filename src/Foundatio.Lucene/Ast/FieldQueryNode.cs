namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a query scoped to a field (<c>field:value</c> or <c>field:(...)</c>).
/// </summary>
public class FieldQueryNode : QueryNode, IFieldNode
{
    private TextValue _field;

    /// <summary>
    /// The field name (with escape sequences processed) as a memory slice.
    /// </summary>
    public ReadOnlyMemory<char> FieldMemory
    {
        get => _field.Memory;
        set => _field.Memory = value;
    }

    /// <summary>
    /// The field name.
    /// </summary>
    public string Field
    {
        get => _field.GetString();
        set => _field.SetString(value);
    }

    /// <summary>
    /// Whether the field name contains wildcard characters (for example <c>book.*:value</c>).
    /// </summary>
    public bool IsWildcardField => _field.Span.IndexOfAny('*', '?') >= 0;

    /// <summary>
    /// The query to apply to the field.
    /// </summary>
    public QueryNode? Query { get; set; }

    /// <inheritdoc/>
    public override QueryNode Clone() => CopyCommonTo(new FieldQueryNode { FieldMemory = FieldMemory, Query = Query?.Clone() });
}
