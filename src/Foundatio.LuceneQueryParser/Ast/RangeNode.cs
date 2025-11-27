namespace Foundatio.LuceneQueryParser.Ast;

/// <summary>
/// Represents a range query.
/// </summary>
public class RangeNode : QueryNode
{
    private ReadOnlyMemory<char> _field;
    private ReadOnlyMemory<char> _min;
    private ReadOnlyMemory<char> _max;
    
    /// <summary>
    /// The field name as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> FieldMemory
    {
        get => _field;
        set => _field = value;
    }
    
    /// <summary>
    /// The field name (optional, may be set at FieldQueryNode level).
    /// Use FieldMemory for zero-allocation access.
    /// </summary>
    public string? Field
    {
        get => _field.Length == 0 ? null : _field.Span.ToString();
        set => _field = value?.AsMemory() ?? ReadOnlyMemory<char>.Empty;
    }

    /// <summary>
    /// The lower bound as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> MinMemory
    {
        get => _min;
        set => _min = value;
    }
    
    /// <summary>
    /// The lower bound of the range (null for unbounded).
    /// Use MinMemory for zero-allocation access.
    /// </summary>
    public string? Min
    {
        get => _min.Length == 0 ? null : _min.Span.ToString();
        set => _min = value?.AsMemory() ?? ReadOnlyMemory<char>.Empty;
    }

    /// <summary>
    /// The upper bound as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> MaxMemory
    {
        get => _max;
        set => _max = value;
    }
    
    /// <summary>
    /// The upper bound of the range (null for unbounded).
    /// Use MaxMemory for zero-allocation access.
    /// </summary>
    public string? Max
    {
        get => _max.Length == 0 ? null : _max.Span.ToString();
        set => _max = value?.AsMemory() ?? ReadOnlyMemory<char>.Empty;
    }

    /// <summary>
    /// Whether the lower bound is inclusive.
    /// </summary>
    public bool MinInclusive { get; set; } = true;

    /// <summary>
    /// Whether the upper bound is inclusive.
    /// </summary>
    public bool MaxInclusive { get; set; } = true;

    /// <summary>
    /// Optional boost value.
    /// </summary>
    public float? Boost { get; set; }

    /// <summary>
    /// The operator used for short-form ranges (>, >=, <, <=).
    /// </summary>
    public RangeOperator? Operator { get; set; }
}

/// <summary>
/// Defines the operator for short-form range queries.
/// </summary>
public enum RangeOperator
{
    /// <summary>Greater than (&gt;)</summary>
    GreaterThan,
    /// <summary>Greater than or equal (&gt;=)</summary>
    GreaterThanOrEqual,
    /// <summary>Less than (&lt;)</summary>
    LessThan,
    /// <summary>Less than or equal (&lt;=)</summary>
    LessThanOrEqual
}
