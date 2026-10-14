using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// The result of parsing a Lucene query: the (possibly partial) document and any errors.
/// </summary>
public sealed class LuceneParseResult
{
    /// <summary>
    /// Creates a parse result.
    /// </summary>
    public LuceneParseResult(QueryDocument document, IReadOnlyList<ParseError>? errors = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Errors = errors ?? [];
    }

    /// <summary>
    /// The parsed query document. Always non-null; when there are errors it contains everything that could be parsed.
    /// </summary>
    public QueryDocument Document { get; }

    /// <summary>
    /// The errors encountered while parsing. Empty when parsing succeeded.
    /// </summary>
    public IReadOnlyList<ParseError> Errors { get; }

    /// <summary>
    /// Whether parsing succeeded without errors.
    /// </summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>
    /// Whether parsing encountered any errors.
    /// </summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>
    /// Throws a <see cref="QueryParseException"/> describing the errors when parsing did not succeed.
    /// </summary>
    /// <returns>The parsed document.</returns>
    public QueryDocument GetDocumentOrThrow()
    {
        if (IsSuccess)
            return Document;

        throw new QueryParseException($"Failed to parse query: {string.Join("; ", Errors.Select(e => e.ToString()))}")
        {
            Errors = Errors
        };
    }
}
