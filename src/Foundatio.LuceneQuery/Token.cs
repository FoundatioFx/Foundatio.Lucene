namespace Foundatio.LuceneQuery;

/// <summary>
/// Represents a single token in the Lucene query language with position tracking.
/// Uses zero-copy memory slices for values.
/// </summary>
/// <param name="Type">The type of this token.</param>
/// <param name="Value">The text value of this token as a memory slice of the source.</param>
/// <param name="Line">The line number where this token starts (1-based).</param>
/// <param name="Column">The column number where this token starts (1-based).</param>
/// <param name="Position">The absolute character position in the source text (0-based).</param>
/// <param name="Length">The length of this token in characters.</param>
public readonly record struct Token(
    TokenType Type,
    ReadOnlyMemory<char> Value,
    int Line,
    int Column,
    int Position,
    int Length)
{
    /// <summary>
    /// Gets the token value as a span (zero allocation).
    /// </summary>
    public ReadOnlySpan<char> Span => Value.Span;
    
    /// <summary>
    /// Gets the token value as a string. Only call when string is actually needed.
    /// </summary>
    public string GetString() => Value.Span.ToString();
    
    /// <summary>
    /// Checks if the token value equals the specified string (zero allocation).
    /// </summary>
    public bool ValueEquals(string other) => Value.Span.SequenceEqual(other.AsSpan());
    
    /// <summary>
    /// Checks if the token value equals the specified string, ignoring case (zero allocation).
    /// </summary>
    public bool ValueEqualsIgnoreCase(string other) => Value.Span.Equals(other.AsSpan(), StringComparison.OrdinalIgnoreCase);
    
    /// <inheritdoc/>
    public override string ToString() => $"{Type}({GetString()}) at {Line}:{Column}";
}
