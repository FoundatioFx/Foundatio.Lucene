namespace Foundatio.Lucene;

/// <summary>
/// Error codes for query exceptions.
/// </summary>
public enum QueryErrorCode
{
    /// <summary>
    /// Unknown or unspecified error.
    /// </summary>
    Unknown = 0,

    // Parse errors (100-199)
    /// <summary>
    /// General parse error.
    /// </summary>
    ParseError = 100,

    /// <summary>
    /// Unexpected token encountered during parsing.
    /// </summary>
    UnexpectedToken = 101,

    /// <summary>
    /// Missing closing bracket or parenthesis.
    /// </summary>
    UnmatchedBracket = 102,

    /// <summary>
    /// Invalid range syntax.
    /// </summary>
    InvalidRange = 103,

    /// <summary>
    /// Invalid field name.
    /// </summary>
    InvalidFieldName = 104,

    /// <summary>
    /// Invalid date math expression.
    /// </summary>
    InvalidDateMath = 105,

    // Validation errors (200-299)
    /// <summary>
    /// General validation error.
    /// </summary>
    ValidationError = 200,

    /// <summary>
    /// Field is not allowed.
    /// </summary>
    FieldNotAllowed = 201,

    /// <summary>
    /// Field is restricted.
    /// </summary>
    FieldRestricted = 202,

    /// <summary>
    /// Field could not be resolved.
    /// </summary>
    UnresolvedField = 203,

    /// <summary>
    /// Operation is not allowed.
    /// </summary>
    OperationNotAllowed = 204,

    /// <summary>
    /// Operation is restricted.
    /// </summary>
    OperationRestricted = 205,

    /// <summary>
    /// Leading wildcards are not allowed.
    /// </summary>
    LeadingWildcardNotAllowed = 206,

    /// <summary>
    /// Query exceeds maximum allowed depth.
    /// </summary>
    MaxDepthExceeded = 207,

    /// <summary>
    /// Include reference could not be resolved.
    /// </summary>
    UnresolvedInclude = 208,

    // Build errors (300-399)
    /// <summary>
    /// General build error.
    /// </summary>
    BuildError = 300,

    /// <summary>
    /// Unsupported query type.
    /// </summary>
    UnsupportedQueryType = 301,

    /// <summary>
    /// Type conversion failed.
    /// </summary>
    TypeConversionError = 302,

    /// <summary>
    /// Expression building failed.
    /// </summary>
    ExpressionBuildError = 303
}

/// <summary>
/// Base exception for all query-related errors.
/// </summary>
public class QueryException : Exception
{
    /// <summary>
    /// Creates a new query exception.
    /// </summary>
    public QueryException(string message, QueryErrorCode errorCode = QueryErrorCode.Unknown)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new query exception with an inner exception.
    /// </summary>
    public QueryException(string message, QueryErrorCode errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// The error code identifying the type of error.
    /// </summary>
    public QueryErrorCode ErrorCode { get; }

    /// <summary>
    /// Optional position in the query string where the error occurred.
    /// </summary>
    public int Position { get; init; } = -1;

    /// <summary>
    /// Optional length of the problematic segment.
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// Optional field name associated with the error.
    /// </summary>
    public string? FieldName { get; init; }

    public override string ToString()
    {
        var result = $"[{ErrorCode}] {Message}";
        if (Position >= 0)
            result += $" at position {Position}";
        if (!string.IsNullOrEmpty(FieldName))
            result += $" (field: {FieldName})";
        return result;
    }
}

/// <summary>
/// Exception thrown when a query fails to parse.
/// </summary>
public class QueryParseException : QueryException
{
    public QueryParseException(string message)
        : base(message, QueryErrorCode.ParseError) { }

    public QueryParseException(string message, QueryErrorCode errorCode)
        : base(message, errorCode) { }

    public QueryParseException(string message, Exception innerException)
        : base(message, QueryErrorCode.ParseError, innerException) { }

    public QueryParseException(string message, QueryErrorCode errorCode, Exception innerException)
        : base(message, errorCode, innerException) { }

    /// <summary>
    /// Collection of all parse errors encountered.
    /// </summary>
    public IReadOnlyList<ParseError> Errors { get; init; } = [];
}

/// <summary>
/// Exception thrown when query validation fails.
/// </summary>
public class QueryValidationException : QueryException
{
    public QueryValidationException(string message, QueryValidationResult? result = null, Exception? inner = null)
        : base(message, QueryErrorCode.ValidationError, inner!)
    {
        Result = result ?? new QueryValidationResult();
    }

    public QueryValidationException(string message, QueryErrorCode errorCode, QueryValidationResult? result = null)
        : base(message, errorCode)
    {
        Result = result ?? new QueryValidationResult();
    }

    /// <summary>
    /// The validation result containing details about the failure.
    /// </summary>
    public QueryValidationResult Result { get; }

    /// <summary>
    /// The validation errors.
    /// </summary>
    public ICollection<QueryValidationError> Errors => Result.ValidationErrors;
}

/// <summary>
/// Exception thrown when query building fails.
/// </summary>
public class QueryBuildException : QueryException
{
    public QueryBuildException(string message)
        : base(message, QueryErrorCode.BuildError) { }

    public QueryBuildException(string message, QueryErrorCode errorCode)
        : base(message, errorCode) { }

    public QueryBuildException(string message, Exception innerException)
        : base(message, QueryErrorCode.BuildError, innerException) { }

    public QueryBuildException(string message, QueryErrorCode errorCode, Exception innerException)
        : base(message, errorCode, innerException) { }
}
