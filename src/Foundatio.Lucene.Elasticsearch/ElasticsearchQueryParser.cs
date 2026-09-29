using System.Collections.Concurrent;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Builds Elasticsearch queries, aggregations, and sorts from Lucene-syntax expressions.
/// </summary>
/// <remarks>
/// <para>The parser is thread-safe and meant to be created once and shared. Per-request settings (field aliases,
/// includes, validation, the mapping for an index) are passed as <see cref="ElasticsearchQueryOptions"/>.</para>
/// <para>Building is synchronous. When the configuration has asynchronous dependencies (include or field resolvers,
/// a mapping loaded from the server, geo location, runtime field, or nested filter resolvers) use the <c>Async</c>
/// methods, which resolve them before building.</para>
/// </remarks>
public class ElasticsearchQueryParser : QueryParserBase<ElasticsearchQueryVisitorContext>
{
    private const string MappingResolutionInstalledKey = "@MappingResolutionInstalled";

    private readonly ConcurrentDictionary<string, ElasticsearchQueryOptions> _registeredOptions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a parser.
    /// </summary>
    public ElasticsearchQueryParser(Action<ElasticsearchQueryParserConfiguration>? configure = null)
        : this(CreateConfiguration(configure))
    {
    }

    private ElasticsearchQueryParser(ElasticsearchQueryParserConfiguration configuration) : base(configuration)
    {
        Configuration = configuration;
    }

    private static ElasticsearchQueryParserConfiguration CreateConfiguration(Action<ElasticsearchQueryParserConfiguration>? configure)
    {
        var configuration = new ElasticsearchQueryParserConfiguration();
        configure?.Invoke(configuration);
        return configuration;
    }

    /// <summary>
    /// The parser configuration.
    /// </summary>
    public ElasticsearchQueryParserConfiguration Configuration { get; }

    /// <summary>
    /// Registers options by name (typically an index name). Requests select them with <see cref="ElasticsearchQueryOptions.Index"/>.
    /// </summary>
    public ElasticsearchQueryParser SetOptions(string index, ElasticsearchQueryOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(index);
        _registeredOptions[index] = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    /// Gets registered options, or null.
    /// </summary>
    public ElasticsearchQueryOptions? GetOptions(string index) => _registeredOptions.TryGetValue(index, out var options) ? options : null;

    /// <summary>
    /// Removes registered options.
    /// </summary>
    public bool RemoveOptions(string index) => _registeredOptions.TryRemove(index, out _);

    /// <summary>
    /// Creates a context for a request. Pass it to the context overloads to inspect it after building, for example to
    /// read <see cref="ElasticsearchQueryVisitorContext.RuntimeFields"/>.
    /// </summary>
    public ElasticsearchQueryVisitorContext CreateContext(ElasticsearchQueryOptions? options = null)
    {
        var registered = options?.Index is { } index ? GetOptions(index) : null;
        var context = new ElasticsearchQueryVisitorContext();
        ApplyOptions(context, registered, options);

        var config = Configuration;
        context.UseScoring = options?.UseScoring ?? registered?.UseScoring ?? config.UseScoring;
        context.MappingResolver = options?.MappingResolver ?? registered?.MappingResolver ?? config.MappingResolver;
        context.DefaultTimeZone = options?.DefaultTimeZone ?? registered?.DefaultTimeZone ?? config.DefaultTimeZone;
        context.UseNested = config.UseNested;
        context.GeoLocationResolver = options?.GeoLocationResolver ?? registered?.GeoLocationResolver ?? config.GeoLocationResolver;
        context.RuntimeFieldResolver = options?.EnableRuntimeFieldResolver ?? registered?.EnableRuntimeFieldResolver ?? true
            ? options?.RuntimeFieldResolver ?? registered?.RuntimeFieldResolver ?? config.RuntimeFieldResolver
            : null;
        context.NestedFilterResolver = options?.NestedFilterResolver ?? registered?.NestedFilterResolver ?? config.NestedFilterResolver;
        context.StartDate = options?.StartDate ?? registered?.StartDate;
        context.EndDate = options?.EndDate ?? registered?.EndDate;
        return context;
    }

    /// <summary>
    /// Builds a query. An empty query matches all documents.
    /// </summary>
    /// <exception cref="QueryParseException">The query has syntax errors.</exception>
    /// <exception cref="QueryValidationException">The query is invalid (for example it uses a restricted field).</exception>
    /// <exception cref="InvalidOperationException">The configuration has asynchronous dependencies; use <see cref="BuildQueryAsync(string, ElasticsearchQueryOptions?, CancellationToken)"/>.</exception>
    public Query BuildQuery(string query, ElasticsearchQueryOptions? options = null) => BuildQuery(query, CreateContext(options));

    /// <summary>
    /// Builds a query using the specified context.
    /// </summary>
    public Query BuildQuery(string query, ElasticsearchQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var document = ParseQuery(query, context);
        return BuildProcessed(document, context, clone: false);
    }

    /// <summary>
    /// Builds a query from a parsed document. The document is not modified, so it can be cached and reused.
    /// </summary>
    public Query BuildQuery(QueryDocument document, ElasticsearchQueryOptions? options = null) => BuildQuery(document, CreateContext(options));

    /// <summary>
    /// Builds a query from a parsed document using the specified context. The document is not modified.
    /// </summary>
    public Query BuildQuery(QueryDocument document, ElasticsearchQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        return BuildProcessed(document, context, clone: true);
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then builds a query.
    /// </summary>
    public ValueTask<Query> BuildQueryAsync(string query, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
        => BuildQueryAsync(query, CreateContext(options), cancellationToken);

    /// <summary>
    /// Resolves asynchronous dependencies, then builds a query using the specified context.
    /// </summary>
    public async ValueTask<Query> BuildQueryAsync(string query, ElasticsearchQueryVisitorContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var document = ParseQuery(query, context);
        await ResolveQueryAsync(document, context, cancellationToken).ConfigureAwait(false);
        return BuildProcessed(document, context, clone: false);
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then builds a query from a parsed document. The document is not modified.
    /// </summary>
    public async ValueTask<Query> BuildQueryAsync(QueryDocument document, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var context = CreateContext(options);
        await ResolveQueryAsync(document, context, cancellationToken).ConfigureAwait(false);
        return BuildProcessed(document, context, clone: true);
    }

    /// <summary>
    /// Builds a query, returning failures as a result instead of throwing.
    /// </summary>
    public QueryResult<Query> TryBuildQuery(string query, ElasticsearchQueryOptions? options = null)
    {
        return QueryResult.Try(() => BuildQuery(query, options));
    }

    /// <summary>
    /// Resolves asynchronous dependencies and builds a query, returning failures as a result instead of throwing.
    /// </summary>
    public async ValueTask<QueryResult<Query>> TryBuildQueryAsync(string query, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return QueryResult<Query>.Success(await BuildQueryAsync(query, options, cancellationToken).ConfigureAwait(false));
        }
        catch (QueryException ex)
        {
            return QueryResult<Query>.Failure(ex);
        }
    }

    /// <summary>
    /// Builds aggregations from an expression such as <c>terms:(status~10 min:created) date:created~1d</c>.
    /// </summary>
    public IDictionary<string, Aggregation> BuildAggregations(string aggregations, ElasticsearchQueryOptions? options = null)
        => BuildAggregations(aggregations, CreateContext(options));

    /// <summary>
    /// Builds aggregations using the specified context.
    /// </summary>
    public IDictionary<string, Aggregation> BuildAggregations(string aggregations, ElasticsearchQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        ArgumentNullException.ThrowIfNull(context);
        InstallMappingResolution(context);
        var expressions = ProcessAggregations(aggregations, context);
        return ElasticsearchAggregationBuilder.Build(expressions, context);
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then builds aggregations.
    /// </summary>
    public ValueTask<IDictionary<string, Aggregation>> BuildAggregationsAsync(string aggregations, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
        => BuildAggregationsAsync(aggregations, CreateContext(options), cancellationToken);

    /// <summary>
    /// Resolves asynchronous dependencies, then builds aggregations using the specified context.
    /// </summary>
    public async ValueTask<IDictionary<string, Aggregation>> BuildAggregationsAsync(string aggregations, ElasticsearchQueryVisitorContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        ArgumentNullException.ThrowIfNull(context);
        var expressions = await ProcessAggregationsAsync(aggregations, context, cancellationToken).ConfigureAwait(false);
        return ElasticsearchAggregationBuilder.Build(expressions, context);
    }

    /// <summary>
    /// Builds sort options from an expression such as <c>-created +name</c>.
    /// </summary>
    public ICollection<SortOptions> BuildSort(string sort, ElasticsearchQueryOptions? options = null) => BuildSort(sort, CreateContext(options));

    /// <summary>
    /// Builds sort options using the specified context.
    /// </summary>
    public ICollection<SortOptions> BuildSort(string sort, ElasticsearchQueryVisitorContext context)
    {
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(context);
        InstallMappingResolution(context);
        var fields = ProcessSort(sort, context);
        return ElasticsearchSortBuilder.Build(fields, context);
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then builds sort options.
    /// </summary>
    public ValueTask<ICollection<SortOptions>> BuildSortAsync(string sort, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
        => BuildSortAsync(sort, CreateContext(options), cancellationToken);

    /// <summary>
    /// Resolves asynchronous dependencies, then builds sort options using the specified context.
    /// </summary>
    public async ValueTask<ICollection<SortOptions>> BuildSortAsync(string sort, ElasticsearchQueryVisitorContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(context);
        var fields = await ProcessSortAsync(sort, context, cancellationToken).ConfigureAwait(false);
        return ElasticsearchSortBuilder.Build(fields, context);
    }

    /// <summary>
    /// Builds a query, aggregations, and sort together, sharing one context so the runtime fields all of them need
    /// are collected. Pass null for any part you don't need.
    /// </summary>
    public ElasticsearchSearch BuildSearch(string? query, string? aggregations = null, string? sort = null, ElasticsearchQueryOptions? options = null)
    {
        var context = CreateContext(options);
        var builtQuery = query is null ? null : BuildQuery(query, context);
        var builtAggregations = aggregations is null ? null : BuildAggregations(aggregations, WithSharedState(context, options));
        var builtSort = sort is null ? null : BuildSort(sort, WithSharedState(context, options));

        return new ElasticsearchSearch { Query = builtQuery, Aggregations = builtAggregations, Sort = builtSort, RuntimeFields = context.RuntimeFields.ToList() };
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then builds a query, aggregations, and sort together. Pass null for any part
    /// you don't need.
    /// </summary>
    public async ValueTask<ElasticsearchSearch> BuildSearchAsync(string? query, string? aggregations = null, string? sort = null, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(options);
        var builtQuery = query is null ? null : await BuildQueryAsync(query, context, cancellationToken).ConfigureAwait(false);
        var builtAggregations = aggregations is null ? null : await BuildAggregationsAsync(aggregations, WithSharedState(context, options), cancellationToken).ConfigureAwait(false);
        var builtSort = sort is null ? null : await BuildSortAsync(sort, WithSharedState(context, options), cancellationToken).ConfigureAwait(false);

        return new ElasticsearchSearch { Query = builtQuery, Aggregations = builtAggregations, Sort = builtSort, RuntimeFields = context.RuntimeFields.ToList() };
    }

    /// <summary>
    /// Creates a fresh context for the next expression (each expression has its own validation result and resolution
    /// state) that adds its runtime fields to <paramref name="shared"/>.
    /// </summary>
    private ElasticsearchQueryVisitorContext WithSharedState(ElasticsearchQueryVisitorContext shared, ElasticsearchQueryOptions? options)
    {
        var context = CreateContext(options);
        context.RuntimeFieldSink = shared;
        return context;
    }

    /// <summary>
    /// Validates a query without building it.
    /// </summary>
    public QueryValidationResult ValidateQuery(string query, ElasticsearchQueryOptions? options = null)
    {
        var context = CreateContext(options);
        InstallMappingResolution(context);
        return ValidateQuery(query, context);
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then validates a query without building it.
    /// </summary>
    public ValueTask<QueryValidationResult> ValidateQueryAsync(string query, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ValidateQueryAsync(query, CreateContext(options), cancellationToken);
    }

    /// <summary>
    /// Validates an aggregation expression without building it.
    /// </summary>
    public QueryValidationResult ValidateAggregations(string aggregations, ElasticsearchQueryOptions? options = null)
    {
        var context = CreateContext(options);
        return CatchValidation(context, () => BuildAggregations(aggregations, context));
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then validates an aggregation expression without building it.
    /// </summary>
    public async ValueTask<QueryValidationResult> ValidateAggregationsAsync(string aggregations, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(options);
        try
        {
            await BuildAggregationsAsync(aggregations, context, cancellationToken).ConfigureAwait(false);
        }
        catch (QueryValidationException)
        {
        }

        return FinishValidation(context);
    }

    /// <summary>
    /// Validates a sort expression without building it.
    /// </summary>
    public QueryValidationResult ValidateSort(string sort, ElasticsearchQueryOptions? options = null)
    {
        var context = CreateContext(options);
        return CatchValidation(context, () => BuildSort(sort, context));
    }

    /// <summary>
    /// Resolves asynchronous dependencies, then validates a sort expression without building it.
    /// </summary>
    public async ValueTask<QueryValidationResult> ValidateSortAsync(string sort, ElasticsearchQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        var context = CreateContext(options);
        try
        {
            await BuildSortAsync(sort, context, cancellationToken).ConfigureAwait(false);
        }
        catch (QueryValidationException)
        {
        }

        return FinishValidation(context);
    }

    private static QueryValidationResult CatchValidation(ElasticsearchQueryVisitorContext context, Action action)
    {
        try
        {
            action();
        }
        catch (QueryValidationException)
        {
        }

        return FinishValidation(context);
    }

    private static QueryValidationResult FinishValidation(ElasticsearchQueryVisitorContext context)
    {
        if (context.ValidationOptions is { ShouldThrow: true })
            context.ValidationResult.ThrowIfInvalid();

        return context.ValidationResult;
    }

    private Query BuildProcessed(QueryDocument document, ElasticsearchQueryVisitorContext context, bool clone)
    {
        InstallMappingResolution(context);
        var processed = ProcessQuery(document, context, clone);
        return ElasticsearchQueryBuilder.Build(processed, context);
    }

    /// <inheritdoc/>
    protected override bool RequiresResolution(ElasticsearchQueryVisitorContext context)
    {
        return base.RequiresResolution(context)
            || context.MappingResolver is { CanResolveSynchronously: false }
            || (context.GeoLocationResolver is not null && context.QueryType == QueryType.Query)
            || context.RuntimeFieldResolver is not null
            || context.NestedFilterResolver is not null;
    }

    /// <inheritdoc/>
    protected override async ValueTask OnResolveAsync(QueryType type, QueryDocument document, IReadOnlyDictionary<string, string> fields, ElasticsearchQueryVisitorContext context, CancellationToken cancellationToken)
    {
        var resolver = context.MappingResolver;
        var resolvedFields = fields.Values.Concat(context.DefaultFields ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (resolver is not null)
            await resolver.EnsureFieldsAsync(resolvedFields, cancellationToken).ConfigureAwait(false);

        if (context.RuntimeFieldResolver is { } runtimeFieldResolver)
        {
            foreach (string field in resolvedFields)
            {
                if (resolver?.GetMapping(field) is { Found: true } || context.GetRuntimeField(field) is not null)
                    continue;

                ElasticRuntimeField? runtimeField;
                try
                {
                    runtimeField = await runtimeFieldResolver(field, context, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.ValidationResult.AddError($"Error in runtime field resolver callback when resolving field ({field}): {ex.Message}", code: QueryErrorCode.UnresolvedField);
                    continue;
                }

                if (runtimeField is not null)
                    context.AddRuntimeField(runtimeField);
            }
        }

        InstallMappingResolution(context);

        if (type == QueryType.Query && context.GeoLocationResolver is not null)
            await ResolveGeoLocationsAsync(document, context, cancellationToken).ConfigureAwait(false);

        if (context.NestedFilterResolver is { } nestedFilterResolver && context.UseNested && resolver is not null)
        {
            var pairs = fields.Select(p => (Original: p.Key, Resolved: CanonicalField(p.Value, context)))
                .Concat((context.DefaultFields ?? []).Select(f => (Original: f, Resolved: CanonicalField(f, context))));
            foreach (var (original, resolved) in pairs)
            {
                var mapping = resolver.GetMapping(resolved, followAlias: true);
                string? path = mapping.Property is NestedProperty ? mapping.FullPath : mapping.NestedPath;
                if (path is null)
                    continue;

                try
                {
                    var filter = await nestedFilterResolver(new NestedFilterContext(path, original, resolved), context, cancellationToken).ConfigureAwait(false);
                    context.SetNestedFilter(path, resolved, filter);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.ValidationResult.AddError($"Error in nested filter resolver callback for path ({path}): {ex.Message}");
                }
            }
        }
    }

    private static string CanonicalField(string field, ElasticsearchQueryVisitorContext context)
    {
        return context.MappingResolver?.GetMapping(field) is { Found: true } mapping ? mapping.FullPath : field;
    }

    private static async ValueTask ResolveGeoLocationsAsync(QueryDocument document, ElasticsearchQueryVisitorContext context, CancellationToken cancellationToken)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        CollectGeoTerms(document, field: null, context, texts);
        if (context.Includes is { } includes)
        {
            foreach (string text in includes.Values)
                CollectGeoTerms(LuceneQuery.Parse(text, context.ParserOptions).Document, field: null, context, texts);
        }

        foreach (string text in texts)
        {
            try
            {
                if (await context.GeoLocationResolver!(text, context, cancellationToken).ConfigureAwait(false) is { } location)
                    context.SetGeoLocation(text, location);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.ValidationResult.AddError($"Error in geo location resolver callback when resolving ({text}): {ex.Message}");
            }
        }
    }

    private static void CollectGeoTerms(QueryNode? node, string? field, ElasticsearchQueryVisitorContext context, HashSet<string> texts)
    {
        switch (node)
        {
            case QueryDocument document:
                CollectGeoTerms(document.Query, field, context, texts);
                break;
            case GroupNode group:
                CollectGeoTerms(group.Query, field, context, texts);
                break;
            case NotNode not:
                CollectGeoTerms(not.Query, field, context, texts);
                break;
            case BooleanQueryNode boolean:
                foreach (var clause in boolean.Clauses)
                    CollectGeoTerms(clause.Query, field, context, texts);
                break;
            case FieldQueryNode fieldNode:
                FieldResolverQueryVisitor.TryResolveField(fieldNode.Field, context, out string resolved);
                CollectGeoTerms(fieldNode.Query, resolved, context, texts);
                break;
            case TermNode term when field is not null && IsGeoField(field, context):
                texts.Add(term.UnescapedTerm);
                break;
            case PhraseNode phrase when field is not null && IsGeoField(field, context):
                texts.Add(phrase.Phrase);
                break;
        }
    }

    private static bool IsGeoField(string field, ElasticsearchQueryVisitorContext context)
    {
        return context.MappingResolver?.GetMapping(field, followAlias: true) is { Property: GeoPointProperty }
            || context.GetRuntimeField(field)?.Type == RuntimeFieldType.GeoPoint;
    }

    /// <summary>
    /// Adds mapping-based resolution after the configured field resolver and field map: field names are matched
    /// against the mapping case-insensitively and replaced with their canonical path, and fields that are neither
    /// mapped nor runtime fields are reported as unresolved (the field resolver or field map result is still used).
    /// </summary>
    private static void InstallMappingResolution(ElasticsearchQueryVisitorContext context)
    {
        if (context.MappingResolver is not { } resolver || context.GetValue<bool>(MappingResolutionInstalledKey))
            return;

        context.SetValue(MappingResolutionInstalledKey, true);
        var fieldResolver = context.FieldResolver;
        var fieldMap = context.FieldMap;
        context.FieldMap = null;
        context.FieldResolver = (field, ctx) =>
        {
            string? resolved = fieldResolver?.Invoke(field, ctx) ?? fieldMap?.ResolveField(field) ?? (fieldResolver is null && fieldMap is null ? field : null);
            if (resolved is null)
                return null;

            if (resolver.GetMapping(resolved) is { Found: true } mapping)
                return mapping.FullPath;

            if (context.GetRuntimeField(resolved) is { } runtimeField)
                return runtimeField.Name;

            ctx.ValidationResult.UnresolvedFields.Add(field);
            return resolved;
        };
    }
}
