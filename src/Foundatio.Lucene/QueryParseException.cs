namespace Foundatio.Lucene;

/// <summary>
/// Exception thrown when a query fails to parse.
/// </summary>
public class QueryParseException : Exception
{
    public QueryParseException(string message) : base(message) { }

    public QueryParseException(string message, Exception innerException) : base(message, innerException) { }
}
