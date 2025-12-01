namespace Foundatio.LuceneQuery.Ast;

/// <summary>
/// Represents a multi-term query where multiple consecutive terms are grouped together.
/// This is used when splitOnWhitespace is false, allowing the entire term sequence
/// to be sent to analysis as a single unit.
/// </summary>
/// <remarks>
/// In Lucene's QueryParser, this handles cases like "New York" being parsed as a single
/// multi-word term rather than two separate terms. The terms are stored both individually
/// and as a combined text for flexibility in how they are processed.
/// </remarks>
public class MultiTermNode : QueryNode
{
    private ReadOnlyMemory<char> _combinedText;
    
    /// <summary>
    /// The individual terms that make up this multi-term query as memory slices.
    /// </summary>
    public List<ReadOnlyMemory<char>> TermsMemory { get; set; } = [];

    /// <summary>
    /// The individual terms that make up this multi-term query as strings.
    /// Use TermsMemory for zero-allocation access.
    /// </summary>
    public List<string> Terms
    {
        get => TermsMemory.ConvertAll(m => m.Span.ToString());
        set => TermsMemory = value.ConvertAll(s => s.AsMemory());
    }

    /// <summary>
    /// The combined text as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> CombinedTextMemory
    {
        get => _combinedText;
        set => _combinedText = value;
    }
    
    /// <summary>
    /// The combined text of all terms separated by spaces.
    /// This is the text that would be sent to analysis.
    /// Use CombinedTextMemory for zero-allocation access.
    /// </summary>
    public string CombinedText
    {
        get => _combinedText.Span.ToString();
        set => _combinedText = value.AsMemory();
    }

    /// <summary>
    /// Optional boost value.
    /// </summary>
    public float? Boost { get; set; }

    /// <summary>
    /// Optional fuzzy distance (for fuzzy queries with ~).
    /// </summary>
    public int? FuzzyDistance { get; set; }
}
