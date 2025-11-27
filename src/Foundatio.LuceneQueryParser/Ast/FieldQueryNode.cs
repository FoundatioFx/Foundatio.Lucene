namespace Foundatio.LuceneQueryParser.Ast;

/// <summary>
/// Represents a field-specific query.
/// </summary>
public class FieldQueryNode : QueryNode
{
    private ReadOnlyMemory<char> _field;
    
    /// <summary>
    /// The field name as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> FieldMemory
    {
        get => _field;
        set => _field = value;
    }
    
    /// <summary>
    /// The field name as a string. Use FieldMemory for zero-allocation access.
    /// </summary>
    public string Field
    {
        get => _field.Span.ToString();
        set => _field = value.AsMemory();
    }

    /// <summary>
    /// Whether the field uses exists syntax (field:*).
    /// </summary>
    public bool IsExists { get; set; }

    /// <summary>
    /// The query to apply to the field.
    /// </summary>
    public QueryNode? Query { get; set; }
}
