namespace Foundatio.LuceneQuery;

/// <summary>
/// Represents an error encountered during parsing.
/// </summary>
/// <param name="Message">The error message describing what went wrong.</param>
/// <param name="Position">The absolute character position where the error occurred (0-based).</param>
/// <param name="Length">The length of the error span in characters.</param>
/// <param name="Line">The line number where the error occurred (1-based).</param>
/// <param name="Column">The column number where the error occurred (1-based).</param>
public readonly record struct ParseError(
    string Message,
    int Position,
    int Length,
    int Line,
    int Column)
{
    /// <summary>
    /// The end position of the error (Position + Length).
    /// </summary>
    public int EndPosition => Position + Length;

    /// <inheritdoc/>
    public override string ToString() => $"Error at line {Line}, column {Column}: {Message}";
}
