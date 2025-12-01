namespace Foundatio.LuceneQuery.Ast;

/// <summary>
/// Represents a regular expression query.
/// </summary>
public class RegexNode : QueryNode
{
    private ReadOnlyMemory<char> _pattern;
    
    /// <summary>
    /// The regex pattern as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> PatternMemory
    {
        get => _pattern;
        set => _pattern = value;
    }
    
    /// <summary>
    /// The regex pattern (without enclosing /) as a string. Use PatternMemory for zero-allocation access.
    /// </summary>
    public string Pattern
    {
        get => _pattern.Span.ToString();
        set => _pattern = value.AsMemory();
    }

    /// <summary>
    /// Optional boost value.
    /// </summary>
    public float? Boost { get; set; }
}
