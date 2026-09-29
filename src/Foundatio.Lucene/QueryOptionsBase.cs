using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene;

/// <summary>
/// Per-request (or per-scope, such as per-tenant) settings that override the parser configuration. Instances are
/// immutable and can be cached and shared.
/// </summary>
public abstract record QueryOptionsBase
{
    /// <summary>
    /// Overrides the operator used between juxtaposed clauses.
    /// </summary>
    public BooleanOperator? DefaultOperator { get; init; }

    /// <summary>
    /// Overrides the fields searched by terms that have no field.
    /// </summary>
    public string[]? DefaultFields { get; init; }

    /// <summary>
    /// Overrides the field aliases.
    /// </summary>
    public FieldMap? FieldMap { get; init; }

    /// <summary>
    /// Overrides the synchronous field resolver.
    /// </summary>
    public QueryFieldResolver? FieldResolver { get; init; }

    /// <summary>
    /// Overrides the asynchronous field resolver.
    /// </summary>
    public AsyncQueryFieldResolver? AsyncFieldResolver { get; init; }

    /// <summary>
    /// Overrides the query text for <c>@include:name</c> references.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Includes { get; init; }

    /// <summary>
    /// Overrides the asynchronous include resolver.
    /// </summary>
    public IncludeResolver? IncludeResolver { get; init; }

    /// <summary>
    /// Overrides the validation rules.
    /// </summary>
    public QueryValidationOptions? ValidationOptions { get; init; }
}
