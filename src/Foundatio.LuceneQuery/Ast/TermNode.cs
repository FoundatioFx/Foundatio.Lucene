namespace Foundatio.LuceneQuery.Ast;

/// <summary>
/// Represents a simple term query.
/// </summary>
public class TermNode : QueryNode
{
    private ReadOnlyMemory<char> _term;
    private ReadOnlyMemory<char> _unescapedTerm;
    
    /// <summary>
    /// The term value as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> TermMemory
    {
        get => _term;
        set => _term = value;
    }
    
    /// <summary>
    /// The term value as a string. Use TermMemory for zero-allocation access.
    /// </summary>
    public string Term
    {
        get => _term.Span.ToString();
        set => _term = value.AsMemory();
    }

    /// <summary>
    /// The unescaped term value as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> UnescapedTermMemory
    {
        get => _unescapedTerm;
        set => _unescapedTerm = value;
    }
    
    /// <summary>
    /// The unescaped term value as a string. Use UnescapedTermMemory for zero-allocation access.
    /// </summary>
    public string UnescapedTerm
    {
        get => _unescapedTerm.Span.ToString();
        set => _unescapedTerm = value.AsMemory();
    }

    /// <summary>
    /// Optional boost value.
    /// </summary>
    public float? Boost { get; set; }

    /// <summary>
    /// Optional fuzzy distance (for fuzzy queries with ~).
    /// </summary>
    public int? FuzzyDistance { get; set; }

    /// <summary>
    /// Whether this is a prefix query (ends with *).
    /// </summary>
    public bool IsPrefix { get; set; }

    /// <summary>
    /// Whether this is a wildcard query (contains * or ?).
    /// </summary>
    public bool IsWildcard { get; set; }
}
