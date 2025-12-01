namespace Foundatio.LuceneQuery.Ast;

/// <summary>
/// Represents a phrase query (quoted string).
/// </summary>
public class PhraseNode : QueryNode
{
    private ReadOnlyMemory<char> _phrase;
    
    /// <summary>
    /// The phrase value as a memory slice (zero allocation).
    /// </summary>
    public ReadOnlyMemory<char> PhraseMemory
    {
        get => _phrase;
        set => _phrase = value;
    }
    
    /// <summary>
    /// The phrase value (without quotes) as a string. Use PhraseMemory for zero-allocation access.
    /// </summary>
    public string Phrase
    {
        get => _phrase.Span.ToString();
        set => _phrase = value.AsMemory();
    }

    /// <summary>
    /// Optional boost value.
    /// </summary>
    public float? Boost { get; set; }

    /// <summary>
    /// Optional slop value for proximity queries (phrase~N).
    /// </summary>
    public int? Slop { get; set; }
}
