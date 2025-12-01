using Foundatio.LuceneQuery.Ast;

namespace Foundatio.LuceneQuery;

/// <summary>
/// Represents the result of parsing a Lucene query.
/// </summary>
public class LuceneParseResult
{
    /// <summary>
    /// Cached empty error list to avoid allocations for successful parses.
    /// </summary>
    private static readonly List<ParseError> EmptyErrors = [];
    
    /// <summary>
    /// The parsed query document. Always non-null after parsing, even if errors occurred.
    /// </summary>
    public QueryDocument Document { get; set; }

    /// <summary>
    /// List of errors encountered during parsing. Empty list means successful parse.
    /// </summary>
    public List<ParseError> Errors { get; set; }

    /// <summary>
    /// Indicates whether the parsing was successful (no errors).
    /// </summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>
    /// Indicates whether parsing encountered any errors.
    /// </summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>
    /// Creates a new LuceneParseResult with an empty document.
    /// </summary>
    public LuceneParseResult()
    {
        Document = new QueryDocument();
        Errors = EmptyErrors;
    }

    /// <summary>
    /// Creates a parse result with a document and optional errors.
    /// </summary>
    public LuceneParseResult(QueryDocument document, List<ParseError>? errors = null)
    {
        Document = document ?? new QueryDocument();
        Errors = errors ?? EmptyErrors;
    }

    /// <summary>
    /// Creates a successful parse result with a document and no errors.
    /// </summary>
    public static LuceneParseResult Success(QueryDocument document)
    {
        return new LuceneParseResult
        {
            Document = document ?? new QueryDocument(),
            Errors = EmptyErrors
        };
    }

    /// <summary>
    /// Creates a partial parse result with a document and errors.
    /// </summary>
    public static LuceneParseResult Partial(QueryDocument document, List<ParseError> errors)
    {
        return new LuceneParseResult
        {
            Document = document ?? new QueryDocument(),
            Errors = errors ?? EmptyErrors
        };
    }

    /// <summary>
    /// Creates a failed parse result with errors and an empty document.
    /// </summary>
    public static LuceneParseResult Failure(List<ParseError> errors)
    {
        return new LuceneParseResult
        {
            Document = new QueryDocument(),
            Errors = errors ?? EmptyErrors
        };
    }

    /// <summary>
    /// Creates a failed parse result with a single error and an empty document.
    /// </summary>
    public static LuceneParseResult Failure(ParseError error)
    {
        return new LuceneParseResult
        {
            Document = new QueryDocument(),
            Errors = [error]
        };
    }
}
