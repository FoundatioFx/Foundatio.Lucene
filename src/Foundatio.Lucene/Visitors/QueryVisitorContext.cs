using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Visitors;

/// <summary>
/// Resolves a field name used in a query to the name used by the data store. Returns null when the field
/// cannot be resolved.
/// </summary>
public delegate string? QueryFieldResolver(string field, IQueryVisitorContext context);

/// <summary>
/// Asynchronously resolves a field name. Async resolvers run in the resolution phase before the synchronous
/// visitor pipeline, and their results are applied by <see cref="FieldResolverQueryVisitor"/>.
/// </summary>
public delegate ValueTask<string?> AsyncQueryFieldResolver(string field, IQueryVisitorContext context, CancellationToken cancellationToken);

/// <summary>
/// Asynchronously resolves the query text for an <c>@include:name</c> reference. Returns null when the include
/// does not exist.
/// </summary>
public delegate ValueTask<string?> IncludeResolver(string name, IQueryVisitorContext context, CancellationToken cancellationToken);

/// <summary>
/// Decides whether an <c>@include</c> reference should be left unexpanded.
/// </summary>
public delegate bool ShouldSkipIncludeFunc(FieldQueryNode node, IQueryVisitorContext context);

/// <summary>
/// The kind of expression being processed.
/// </summary>
public enum QueryType
{
    /// <summary>A filter or search query.</summary>
    Query,
    /// <summary>A sort expression such as <c>-created +name</c>.</summary>
    Sort,
    /// <summary>An aggregation expression such as <c>terms:(status min:created)</c>.</summary>
    Aggregation
}

/// <summary>
/// State shared between visitors while a query is processed.
/// </summary>
public interface IQueryVisitorContext
{
    /// <summary>
    /// Arbitrary values that visitors can use to share state.
    /// </summary>
    IDictionary<string, object?> Data { get; }

    /// <summary>
    /// The kind of expression being processed.
    /// </summary>
    QueryType QueryType { get; set; }

    /// <summary>
    /// The options used to parse query text, including the default operator. Used when parsing include text.
    /// </summary>
    LuceneParserOptions ParserOptions { get; set; }

    /// <summary>
    /// Fields searched by terms that have no field.
    /// </summary>
    string[]? DefaultFields { get; set; }

    /// <summary>
    /// Field aliases applied by <see cref="FieldResolverQueryVisitor"/>.
    /// </summary>
    FieldMap? FieldMap { get; set; }

    /// <summary>
    /// A field resolver applied by <see cref="FieldResolverQueryVisitor"/> before <see cref="FieldMap"/>.
    /// </summary>
    QueryFieldResolver? FieldResolver { get; set; }

    /// <summary>
    /// Query text for <c>@include:name</c> references, keyed by name.
    /// </summary>
    IReadOnlyDictionary<string, string>? Includes { get; set; }

    /// <summary>
    /// Decides whether an <c>@include</c> reference should be left unexpanded.
    /// </summary>
    ShouldSkipIncludeFunc? ShouldSkipInclude { get; set; }

    /// <summary>
    /// The validation rules to apply. Null means no restrictions beyond the defaults.
    /// </summary>
    QueryValidationOptions? ValidationOptions { get; set; }

    /// <summary>
    /// Validation findings and query statistics collected while processing.
    /// </summary>
    QueryValidationResult ValidationResult { get; }

    /// <summary>
    /// The clock used for relative dates such as <c>now-1d</c>.
    /// </summary>
    TimeProvider TimeProvider { get; set; }

    /// <summary>
    /// Gets a value from <see cref="Data"/>.
    /// </summary>
    T? GetValue<T>(string key);

    /// <summary>
    /// Sets a value in <see cref="Data"/>.
    /// </summary>
    void SetValue(string key, object? value);
}

/// <summary>
/// The default <see cref="IQueryVisitorContext"/> implementation.
/// </summary>
public class QueryVisitorContext : IQueryVisitorContext
{
    private Dictionary<string, object?>? _data;
    private QueryValidationResult? _validationResult;

    /// <inheritdoc/>
    public IDictionary<string, object?> Data => _data ??= new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public QueryType QueryType { get; set; }

    /// <inheritdoc/>
    public LuceneParserOptions ParserOptions { get; set; } = LuceneParserOptions.Default;

    /// <summary>
    /// The operator used between juxtaposed clauses. Shortcut for <see cref="ParserOptions"/>.
    /// </summary>
    public BooleanOperator DefaultOperator
    {
        get => ParserOptions.DefaultOperator;
        set => ParserOptions = ParserOptions with { DefaultOperator = value };
    }

    /// <inheritdoc/>
    public string[]? DefaultFields { get; set; }

    /// <inheritdoc/>
    public FieldMap? FieldMap { get; set; }

    /// <inheritdoc/>
    public QueryFieldResolver? FieldResolver { get; set; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string>? Includes { get; set; }

    /// <inheritdoc/>
    public ShouldSkipIncludeFunc? ShouldSkipInclude { get; set; }

    /// <summary>
    /// An asynchronous include resolver used by the resolution phase of the <c>Async</c> build methods.
    /// </summary>
    public IncludeResolver? IncludeResolver { get; set; }

    /// <summary>
    /// An asynchronous field resolver used by the resolution phase of the <c>Async</c> build methods.
    /// </summary>
    public AsyncQueryFieldResolver? AsyncFieldResolver { get; set; }

    /// <summary>
    /// Whether the asynchronous resolution phase has run for this context.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <inheritdoc/>
    public QueryValidationOptions? ValidationOptions { get; set; }

    /// <inheritdoc/>
    public QueryValidationResult ValidationResult => _validationResult ??= new QueryValidationResult { QueryType = QueryType };

    /// <inheritdoc/>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <inheritdoc/>
    public T? GetValue<T>(string key)
    {
        return _data is not null && _data.TryGetValue(key, out var value) && value is T typed ? typed : default;
    }

    /// <inheritdoc/>
    public void SetValue(string key, object? value)
    {
        Data[key] = value;
    }
}
