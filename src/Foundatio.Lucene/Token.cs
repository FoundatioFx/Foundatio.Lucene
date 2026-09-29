namespace Foundatio.Lucene;

/// <summary>
/// Represents a single token in the Lucene query language with position tracking.
/// Uses zero-copy memory slices of the source text for values.
/// </summary>
/// <param name="Type">The type of this token.</param>
/// <param name="Value">The raw text of this token as a slice of the source. Escape sequences are not processed.</param>
/// <param name="Line">The line number where this token starts (1-based).</param>
/// <param name="Column">The column number where this token starts (1-based).</param>
/// <param name="Position">The absolute character position in the source text (0-based).</param>
/// <param name="Length">The length of this token in the source text, including any delimiters.</param>
/// <param name="Flags">Additional information about the token's raw text.</param>
public readonly record struct Token(
    TokenType Type,
    ReadOnlyMemory<char> Value,
    int Line,
    int Column,
    int Position,
    int Length,
    TokenFlags Flags = TokenFlags.None)
{
    /// <summary>
    /// Gets the token value as a span (zero allocation).
    /// </summary>
    public ReadOnlySpan<char> Span => Value.Span;

    /// <summary>
    /// The end position of the token in the source text (exclusive).
    /// </summary>
    public int EndPosition => Position + Length;

    /// <summary>
    /// Whether the token contains backslash escape sequences.
    /// </summary>
    public bool HasEscapes => (Flags & TokenFlags.HasEscapes) != 0;

    /// <summary>
    /// Whether the token contains an unescaped wildcard character.
    /// </summary>
    public bool HasWildcard => (Flags & TokenFlags.HasWildcard) != 0;

    /// <summary>
    /// Whether the token was preceded by whitespace.
    /// </summary>
    public bool HasLeadingWhitespace => (Flags & TokenFlags.LeadingWhitespace) != 0;

    /// <summary>
    /// Gets the token value as a string. Only call when a string is actually needed.
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
