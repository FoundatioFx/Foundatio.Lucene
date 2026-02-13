using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// Base configuration class for query parsers.
/// </summary>
public abstract class QueryParserConfigurationBase
{
    /// <summary>
    /// Default fields to search when no field is specified.
    /// </summary>
    public string[]? DefaultFields { get; set; }

    /// <summary>
    /// Field map for resolving field aliases.
    /// </summary>
    public FieldMap? FieldMap { get; set; }

    /// <summary>
    /// Pre-resolved includes dictionary mapping include names to their query content.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Includes { get; set; }

    /// <summary>
    /// Query validation options.
    /// </summary>
    public QueryValidationOptions? ValidationOptions { get; set; }

    /// <summary>
    /// Additional visitors to run before building the query.
    /// </summary>
    public List<QueryVisitor> Visitors { get; } = [];
}
