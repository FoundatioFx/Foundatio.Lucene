namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents an exists query (field:* or _exists_:field).
/// </summary>
public class ExistsNode : QueryNode
{
    private ReadOnlyMemory<char> _field;
    
    /// <summary>
    /// The field that must exist as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> FieldMemory
    {
        get => _field;
        set => _field = value;
    }
    
    /// <summary>
    /// The field that must exist as a string. Use FieldMemory for zero-allocation access.
    /// </summary>
    public string Field
    {
        get => _field.Span.ToString();
        set => _field = value.AsMemory();
    }

    /// <summary>
    /// Whether this was parsed from _exists_:field syntax (true) or field:* syntax (false).
    /// </summary>
    public bool IsExistsSyntax { get; set; }
}
