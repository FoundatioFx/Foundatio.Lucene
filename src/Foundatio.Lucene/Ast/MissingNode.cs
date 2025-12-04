namespace Foundatio.Lucene.Ast;

/// <summary>
/// Represents a missing query (_missing_:field).
/// </summary>
public class MissingNode : QueryNode
{
    private ReadOnlyMemory<char> _field;
    
    /// <summary>
    /// The field that must be missing as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> FieldMemory
    {
        get => _field;
        set => _field = value;
    }
    
    /// <summary>
    /// The field that must be missing as a string. Use FieldMemory for zero-allocation access.
    /// </summary>
    public string Field
    {
        get => _field.Span.ToString();
        set => _field = value.AsMemory();
    }
}
