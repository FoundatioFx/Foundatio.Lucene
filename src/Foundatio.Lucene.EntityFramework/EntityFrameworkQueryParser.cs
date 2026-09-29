using System.Collections.Concurrent;
using System.Linq.Expressions;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Converts Lucene query strings to Entity Framework Core filter expressions and sort expressions to orderings.
/// </summary>
/// <remarks>
/// <para>Only fields discovered from the EF Core model (after the configured filters) and registered custom fields can
/// be queried; any other field makes the query invalid. Values are converted with the invariant culture and
/// supplied as SQL parameters.</para>
/// <para>A parser is thread-safe and meant to be shared: field metadata is cached per parser, and per-request state
/// lives in a new <see cref="EntityFrameworkQueryVisitorContext"/> created for every call.</para>
/// </remarks>
public class EntityFrameworkQueryParser : QueryParserBase<EntityFrameworkQueryVisitorContext>
{
    private readonly ConcurrentDictionary<Type, EntityFrameworkQueryOptions> _entityOptions = new();
    private readonly EntityFieldResolver _fields;

    /// <summary>
    /// Creates a parser, optionally configuring it.
    /// </summary>
    public EntityFrameworkQueryParser(Action<EntityFrameworkQueryParserConfiguration>? configure = null)
        : this(CreateConfiguration(configure))
    {
    }

    /// <summary>
    /// Creates a parser with the specified configuration.
    /// </summary>
    public EntityFrameworkQueryParser(EntityFrameworkQueryParserConfiguration configuration)
        : base(configuration)
    {
        Configuration = configuration;
        _fields = new EntityFieldResolver(configuration);
    }

    /// <summary>
    /// The parser configuration.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration Configuration { get; }

    /// <summary>
    /// The entity types that have registered options.
    /// </summary>
    public IEnumerable<Type> RegisteredEntityTypes => _entityOptions.Keys;

    private static EntityFrameworkQueryParserConfiguration CreateConfiguration(Action<EntityFrameworkQueryParserConfiguration>? configure)
    {
        var configuration = new EntityFrameworkQueryParserConfiguration();
        configure?.Invoke(configuration);
        return configuration;
    }

    /// <summary>
    /// Registers options used for every query on <typeparamref name="TEntity"/>. Per-request options override them.
    /// </summary>
    public EntityFrameworkQueryParser SetOptions<TEntity>(EntityFrameworkQueryOptions options) where TEntity : class
    {
        return SetOptions(typeof(TEntity), options);
    }

    /// <summary>
    /// Registers options used for every query on <typeparamref name="TEntity"/>, configured with a builder.
    /// </summary>
    public EntityFrameworkQueryParser SetOptions<TEntity>(Action<EntityFrameworkQueryOptionsBuilder> configure) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EntityFrameworkQueryOptionsBuilder();
        configure(builder);
        return SetOptions<TEntity>(builder.Build());
    }

    /// <summary>
    /// Registers options used for every query on <paramref name="entityType"/>.
    /// </summary>
    public EntityFrameworkQueryParser SetOptions(Type entityType, EntityFrameworkQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(options);
        _entityOptions[entityType] = options;
        return this;
    }

    /// <summary>
    /// Gets the options registered for <typeparamref name="TEntity"/>, or null.
    /// </summary>
    public EntityFrameworkQueryOptions? GetOptions<TEntity>() where TEntity : class => GetOptions(typeof(TEntity));

    /// <summary>
    /// Gets the options registered for <paramref name="entityType"/>, or null.
    /// </summary>
    public EntityFrameworkQueryOptions? GetOptions(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return _entityOptions.TryGetValue(entityType, out var options) ? options : null;
    }

    /// <summary>
    /// Removes the options registered for <typeparamref name="TEntity"/>.
    /// </summary>
    public bool RemoveOptions<TEntity>() where TEntity : class => RemoveOptions(typeof(TEntity));

    /// <summary>
    /// Removes the options registered for <paramref name="entityType"/>.
    /// </summary>
    public bool RemoveOptions(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return _entityOptions.TryRemove(entityType, out _);
    }

    /// <summary>
    /// Removes all registered options.
    /// </summary>
    public void ClearOptions() => _entityOptions.Clear();

    /// <summary>
    /// Builds a filter expression for <typeparamref name="T"/> from a Lucene query. An empty query matches everything.
    /// </summary>
    /// <exception cref="QueryParseException">The query has syntax errors.</exception>
    /// <exception cref="QueryValidationException">The query is invalid, for example it uses an unknown field or a value
    /// that cannot be converted to the field's type.</exception>
    public Expression<Func<T, bool>> BuildFilter<T>(string query, EntityFrameworkQueryOptions? options = null) where T : class
    {
        return (Expression<Func<T, bool>>)BuildFilter(GetEntityType(typeof(T), options), query, options);
    }

    /// <summary>
    /// Builds a filter expression (a <c>Func&lt;TEntity, bool&gt;</c> lambda) for <paramref name="entityType"/>.
    /// </summary>
    /// <inheritdoc cref="BuildFilter{T}(string, EntityFrameworkQueryOptions?)"/>
    public LambdaExpression BuildFilter(Type entityType, string query, EntityFrameworkQueryOptions? options = null)
    {
        return BuildFilter(GetEntityType(entityType, options), query, options);
    }

    internal LambdaExpression BuildFilter(IEntityType entityType, string query, EntityFrameworkQueryOptions? options)
    {
        var context = CreateContext(entityType, options);
        var document = ParseQuery(query, context);
        return Build(ProcessQuery(document, context, clone: false), context);
    }

    /// <summary>
    /// Builds a filter expression, first running the asynchronous resolution phase (include and field resolvers).
    /// </summary>
    /// <inheritdoc cref="BuildFilter{T}(string, EntityFrameworkQueryOptions?)"/>
    public async ValueTask<Expression<Func<T, bool>>> BuildFilterAsync<T>(string query, EntityFrameworkQueryOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        return (Expression<Func<T, bool>>)await BuildFilterAsync(GetEntityType(typeof(T), options), query, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a filter expression for <paramref name="entityType"/>, first running the asynchronous resolution phase.
    /// </summary>
    /// <inheritdoc cref="BuildFilter{T}(string, EntityFrameworkQueryOptions?)"/>
    public ValueTask<LambdaExpression> BuildFilterAsync(Type entityType, string query, EntityFrameworkQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        return BuildFilterAsync(GetEntityType(entityType, options), query, options, cancellationToken);
    }

    internal async ValueTask<LambdaExpression> BuildFilterAsync(IEntityType entityType, string query, EntityFrameworkQueryOptions? options, CancellationToken cancellationToken)
    {
        var context = CreateContext(entityType, options);
        var document = ParseQuery(query, context);
        await ResolveQueryAsync(document, context, cancellationToken).ConfigureAwait(false);
        return Build(ProcessQuery(document, context, clone: false), context);
    }

    /// <summary>
    /// Builds a filter expression, returning a failed result instead of throwing when the query is invalid.
    /// </summary>
    public QueryResult<Expression<Func<T, bool>>> TryBuildFilter<T>(string query, EntityFrameworkQueryOptions? options = null) where T : class
    {
        try
        {
            return QueryResult<Expression<Func<T, bool>>>.Success(BuildFilter<T>(query, options));
        }
        catch (QueryException ex)
        {
            return QueryResult<Expression<Func<T, bool>>>.Failure(ex);
        }
    }

    /// <summary>
    /// Builds a filter expression, first running the asynchronous resolution phase, returning a failed result instead
    /// of throwing when the query is invalid.
    /// </summary>
    public async ValueTask<QueryResult<Expression<Func<T, bool>>>> TryBuildFilterAsync<T>(string query, EntityFrameworkQueryOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            return QueryResult<Expression<Func<T, bool>>>.Success(await BuildFilterAsync<T>(query, options, cancellationToken).ConfigureAwait(false));
        }
        catch (QueryException ex)
        {
            return QueryResult<Expression<Func<T, bool>>>.Failure(ex);
        }
    }

    /// <summary>
    /// Validates a query for <typeparamref name="T"/> with the full pipeline, including field and value checks,
    /// without returning the filter.
    /// </summary>
    /// <exception cref="InvalidOperationException">Asynchronous resolvers are configured; use <see cref="ValidateQueryAsync{T}"/>.</exception>
    public QueryValidationResult ValidateQuery<T>(string query, EntityFrameworkQueryOptions? options = null) where T : class
    {
        return ValidateQuery(typeof(T), query, options);
    }

    /// <summary>
    /// Validates a query for <paramref name="entityType"/>.
    /// </summary>
    /// <inheritdoc cref="ValidateQuery{T}(string, EntityFrameworkQueryOptions?)"/>
    public QueryValidationResult ValidateQuery(Type entityType, string query, EntityFrameworkQueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = CreateContext(GetEntityType(entityType, options), options);
        if (RequiresResolution(context))
            throw new InvalidOperationException("The parser is configured with asynchronous resolvers. Use ValidateQueryAsync so they can run before the query is validated.");

        return ValidateQuery(LuceneQuery.Parse(query, context.ParserOptions), context);
    }

    /// <summary>
    /// Validates a query for <typeparamref name="T"/>, first running the asynchronous resolution phase.
    /// </summary>
    public ValueTask<QueryValidationResult> ValidateQueryAsync<T>(string query, EntityFrameworkQueryOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        return ValidateQueryAsync(typeof(T), query, options, cancellationToken);
    }

    /// <summary>
    /// Validates a query for <paramref name="entityType"/>, first running the asynchronous resolution phase.
    /// </summary>
    public async ValueTask<QueryValidationResult> ValidateQueryAsync(Type entityType, string query, EntityFrameworkQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = CreateContext(GetEntityType(entityType, options), options);
        var parsed = LuceneQuery.Parse(query, context.ParserOptions);
        if (parsed.IsSuccess)
            await ResolveQueryAsync(parsed.Document, context, cancellationToken).ConfigureAwait(false);

        return ValidateQuery(parsed, context);
    }

    /// <summary>
    /// Validates a sort expression for <typeparamref name="T"/>, including that every field exists and can be sorted on.
    /// </summary>
    public QueryValidationResult ValidateSort<T>(string sort, EntityFrameworkQueryOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(sort);
        var context = CreateContext(GetEntityType(typeof(T), options), options);
        try
        {
            CreateSort<T>(ProcessSort(sort, context), context);
        }
        catch (QueryValidationException) when (context.ValidationOptions is not { ShouldThrow: true })
        {
        }

        return context.ValidationResult;
    }

    private QueryValidationResult ValidateQuery(LuceneParseResult parsed, EntityFrameworkQueryVisitorContext context)
    {
        context.QueryType = QueryType.Query;
        var result = context.ValidationResult;
        result.QueryType = QueryType.Query;
        foreach (var error in parsed.Errors)
            result.AddError(error.Message, error.Position, error.Code);

        if (result.IsValid && BaseConfiguration.QueryVisitor.Accept(parsed.Document, context) is QueryDocument processed && result.IsValid)
            new FilterExpressionBuilder(context).Build(processed);

        if (context.ValidationOptions is { ShouldThrow: true })
            result.ThrowIfInvalid();

        return result;
    }

    /// <summary>
    /// Builds a sort for <typeparamref name="T"/> from a sort expression such as <c>-created +name</c> or
    /// <c>created:desc name</c>. Sort fields must be scalar fields reached through reference navigations only.
    /// </summary>
    /// <exception cref="QueryValidationException">The sort expression is empty or invalid.</exception>
    public EntityFrameworkSort<T> BuildSort<T>(string sort, EntityFrameworkQueryOptions? options = null) where T : class
    {
        return BuildSort<T>(GetEntityType(typeof(T), options), sort, options);
    }

    internal EntityFrameworkSort<T> BuildSort<T>(IEntityType entityType, string sort, EntityFrameworkQueryOptions? options) where T : class
    {
        var context = CreateContext(entityType, options);
        return CreateSort<T>(ProcessSort(sort, context), context);
    }

    /// <summary>
    /// Builds a sort for <typeparamref name="T"/>, first running the asynchronous resolution phase.
    /// </summary>
    /// <inheritdoc cref="BuildSort{T}(string, EntityFrameworkQueryOptions?)"/>
    public async ValueTask<EntityFrameworkSort<T>> BuildSortAsync<T>(string sort, EntityFrameworkQueryOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        var context = CreateContext(GetEntityType(typeof(T), options), options);
        var fields = await ProcessSortAsync(sort, context, cancellationToken).ConfigureAwait(false);
        return CreateSort<T>(fields, context);
    }

    /// <summary>
    /// Looks up a queryable field of <typeparamref name="T"/> by name (case-insensitive), or returns null when the
    /// field does not exist or is excluded by the configured filters.
    /// </summary>
    public EntityFieldInfo? GetField<T>(string field, EntityFrameworkQueryOptions? options = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(field);
        var context = CreateContext(GetEntityType(typeof(T), options), options);
        return context.TryGetField(field, out var info) ? info : null;
    }

    private static LambdaExpression Build(QueryDocument document, EntityFrameworkQueryVisitorContext context)
    {
        var filter = new FilterExpressionBuilder(context).Build(document);
        context.ValidationResult.ThrowIfInvalid();
        return filter;
    }

    private static EntityFrameworkSort<T> CreateSort<T>(List<SortField> fields, EntityFrameworkQueryVisitorContext context) where T : class
    {
        var result = context.ValidationResult;
        if (fields.Count == 0)
            result.AddError("The sort expression must contain at least one field.");

        var keys = new SortKey[fields.Count];
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (!context.TryGetField(field.Field, out var info))
            {
                result.UnresolvedFields.Add(field.OriginalField);
                result.AddError($"Field ({field.OriginalField}) is not a queryable field of {context.EntityType.ClrType.Name}.", field.Position, QueryErrorCode.UnresolvedField);
                continue;
            }

            if (GetSortKey(info, context.EntityType.ClrType) is { } key)
            {
                keys[i] = key;
                field.Field = info.FullName;
            }
            else
                result.AddError($"Field ({field.OriginalField}) cannot be sorted on: sort fields must be scalar fields that are not reached through a collection.", field.Position, QueryErrorCode.UnsupportedQueryType);
        }

        result.ThrowIfInvalid();
        return new EntityFrameworkSort<T>(fields, keys);
    }

    private static SortKey? GetSortKey(EntityFieldInfo field, Type rootType)
    {
        if (field.SortKey is { } cached)
            return cached;

        if (field.Kind != EntityFieldKind.Property || field.IsCollection)
            return null;

        var chain = new List<EntityFieldInfo>();
        for (var current = field; current is not null; current = current.Parent)
        {
            if (current != field && current.IsCollection)
                return null;
            chain.Add(current);
        }

        var parameter = Expression.Parameter(rootType, "e");
        Expression body = parameter;
        for (int i = chain.Count - 1; i >= 0; i--)
            body = ExpressionHelpers.Access(body, chain[i]);

        var key = new SortKey(Expression.Lambda(body, parameter));
        field.SortKey = key;
        return key;
    }

    internal IEntityType GetEntityType(Type clrType, EntityFrameworkQueryOptions? options)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        var model = options?.Model ?? GetOptions(clrType)?.Model ?? Configuration.Model
            ?? throw new InvalidOperationException($"No EF Core model is available to discover the fields of {clrType.Name}. Use the DbSet or IQueryable extension methods, set EntityFrameworkQueryOptions.Model, or call UseModel on the parser configuration.");

        return model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"{clrType.Name} is not an entity type of the EF Core model.");
    }

    internal EntityFrameworkQueryVisitorContext CreateContext(IEntityType entityType, EntityFrameworkQueryOptions? options)
    {
        var registered = GetOptions(entityType.ClrType);
        var context = new EntityFrameworkQueryVisitorContext(Configuration, _fields, entityType) { Options = options };
        ApplyOptions(context, registered, options);
        context.DefaultSearchOperator = options?.DefaultSearchOperator ?? registered?.DefaultSearchOperator ?? Configuration.DefaultSearchOperator;
        context.TimeZone = options?.TimeZone ?? registered?.TimeZone ?? Configuration.TimeZone;
        context.CustomFieldExpressionBuilder = options?.CustomFieldExpressionBuilder ?? registered?.CustomFieldExpressionBuilder ?? Configuration.CustomFieldExpressionBuilder;
        context.AdditionalFields = Combine(registered?.AdditionalFields, options?.AdditionalFields);
        return context;
    }

    private static IReadOnlyList<EntityFieldInfo> Combine(IReadOnlyList<EntityFieldInfo>? registered, IReadOnlyList<EntityFieldInfo>? request)
    {
        if (registered is not { Count: > 0 })
            return request ?? [];
        if (request is not { Count: > 0 })
            return registered;

        return [.. registered, .. request];
    }
}
