using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Parses Lucene query strings and converts them to Entity Framework filter expressions.
/// </summary>
public class EntityFrameworkQueryParser
{
    private static readonly ConcurrentDictionary<IEntityType, List<EntityFieldInfo>> _entityFieldCache = new();
    private static readonly ConcurrentDictionary<Type, List<EntityFieldInfo>> _reflectionFieldCache = new();
    private readonly ConcurrentDictionary<Type, EntityFrameworkQueryOptions> _entityTypeOptions = new();

    // Stateless visitors reused as singletons; per-request configuration (field map,
    // includes) is carried on the visitor context, so no visitor is allocated per query.
    private readonly FieldResolverQueryVisitor _fieldResolverVisitor = new();
    private readonly IncludeVisitor _includeVisitor = new();
    private readonly DateMathEvaluatorVisitor _dateMathVisitor;

    /// <summary>
    /// Creates a new EntityFrameworkQueryParser with optional configuration.
    /// </summary>
    public EntityFrameworkQueryParser(Action<EntityFrameworkQueryParserConfiguration>? configure = null)
    {
        var config = new EntityFrameworkQueryParserConfiguration();
        configure?.Invoke(config);
        Configuration = config;
        _dateMathVisitor = new DateMathEvaluatorVisitor(config.TimeProvider);
    }

    /// <summary>
    /// The parser configuration.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration Configuration { get; }

    /// <summary>
    /// Registers options for a specific entity type.
    /// These options are used as the base configuration when building filters for this entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="options">The options to register.</param>
    /// <returns>This parser instance for chaining.</returns>
    public EntityFrameworkQueryParser SetOptions<TEntity>(EntityFrameworkQueryOptions options) where TEntity : class
    {
        return SetOptions(typeof(TEntity), options);
    }

    /// <summary>
    /// Registers options for a specific entity type.
    /// These options are used as the base configuration when building filters for this entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="options">The options to register.</param>
    /// <returns>This parser instance for chaining.</returns>
    public EntityFrameworkQueryParser SetOptions(Type entityType, EntityFrameworkQueryOptions options)
    {
        _entityTypeOptions[entityType] = options;
        return this;
    }

    /// <summary>
    /// Registers options for a specific entity type using a configuration action.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="configure">Action to configure the options.</param>
    /// <returns>This parser instance for chaining.</returns>
    public EntityFrameworkQueryParser SetOptions<TEntity>(Action<EntityFrameworkQueryOptionsBuilder> configure) where TEntity : class
    {
        var builder = new EntityFrameworkQueryOptionsBuilder();
        configure(builder);
        return SetOptions<TEntity>(builder.Build());
    }

    /// <summary>
    /// Gets the registered options for a specific entity type, or null if not registered.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>The registered options, or null.</returns>
    public EntityFrameworkQueryOptions? GetOptions<TEntity>() where TEntity : class
    {
        return GetOptions(typeof(TEntity));
    }

    /// <summary>
    /// Gets the registered options for a specific entity type, or null if not registered.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>The registered options, or null.</returns>
    public EntityFrameworkQueryOptions? GetOptions(Type entityType)
    {
        return _entityTypeOptions.TryGetValue(entityType, out var options) ? options : null;
    }

    /// <summary>
    /// Removes registered options for a specific entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions<TEntity>() where TEntity : class
    {
        return RemoveOptions(typeof(TEntity));
    }

    /// <summary>
    /// Removes registered options for a specific entity type.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions(Type entityType)
    {
        return _entityTypeOptions.TryRemove(entityType, out _);
    }

    /// <summary>
    /// Clears all registered entity type options.
    /// </summary>
    public void ClearOptions()
    {
        _entityTypeOptions.Clear();
    }

    /// <summary>
    /// Gets all entity types that have registered options.
    /// </summary>
    public IEnumerable<Type> RegisteredEntityTypes => _entityTypeOptions.Keys;

    /// <summary>
    /// Parses a Lucene query string and returns a filter expression.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <returns>An expression that can be used with EF's Where method.</returns>
    public Expression<Func<T, bool>> BuildFilter<T>(string query, EntityFrameworkQueryVisitorContext? context = null) where T : class
    {
        return BuildFilter<T>(query, context, null);
    }

    /// <summary>
    /// Parses a Lucene query string and returns a filter expression with per-request options.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <param name="options">Optional per-request options to merge with global configuration.</param>
    /// <returns>An expression that can be used with EF's Where method.</returns>
    public Expression<Func<T, bool>> BuildFilter<T>(string query, EntityFrameworkQueryVisitorContext? context, EntityFrameworkQueryOptions? options) where T : class
    {
        context ??= new EntityFrameworkQueryVisitorContext();
        SetupContextDefaults<T>(context, options);

        var document = ParseQuery(query);
        ApplyVisitorPipeline(document, context, typeof(T), options);

        return ExpressionBuilderVisitor.Instance.BuildExpression<T>(document, context, Configuration);
    }

    /// <summary>
    /// Tries to parse a Lucene query string and returns a filter expression.
    /// Returns a result object instead of throwing exceptions.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <returns>A QueryResult containing the expression or error information.</returns>
    public QueryResult<Expression<Func<T, bool>>> TryBuildFilter<T>(string query, EntityFrameworkQueryVisitorContext? context = null) where T : class
    {
        return TryBuildFilter<T>(query, context, null);
    }

    /// <summary>
    /// Tries to parse a Lucene query string and returns a filter expression with per-request options.
    /// Returns a result object instead of throwing exceptions.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <param name="options">Optional per-request options to merge with global configuration.</param>
    /// <returns>A QueryResult containing the expression or error information.</returns>
    public QueryResult<Expression<Func<T, bool>>> TryBuildFilter<T>(string query, EntityFrameworkQueryVisitorContext? context, EntityFrameworkQueryOptions? options) where T : class
    {
        try
        {
            var expression = BuildFilter<T>(query, context, options);
            return QueryResult<Expression<Func<T, bool>>>.Success(expression);
        }
        catch (QueryException ex)
        {
            return QueryResult<Expression<Func<T, bool>>>.Failure(ex);
        }
        catch (FormatException ex)
        {
            return QueryResult<Expression<Func<T, bool>>>.Failure(
                new QueryParseException(ex.Message, QueryErrorCode.ParseError, ex));
        }
        catch (Exception ex)
        {
            return QueryResult<Expression<Func<T, bool>>>.Failure(
                new QueryBuildException($"Failed to build filter: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Tries to parse a Lucene query string and returns a filter expression (legacy out parameter version).
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="expression">The resulting filter expression if successful.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <returns>True if parsing succeeded; false otherwise.</returns>
    public bool TryBuildFilter<T>(string query, out Expression<Func<T, bool>>? expression, EntityFrameworkQueryVisitorContext? context = null) where T : class
    {
        return TryBuildFilter<T>(query, out expression, context, null);
    }

    /// <summary>
    /// Tries to parse a Lucene query string and returns a filter expression with per-request options (legacy out parameter version).
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="expression">The resulting filter expression if successful.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <param name="options">Optional per-request options to merge with global configuration.</param>
    /// <returns>True if parsing succeeded; false otherwise.</returns>
    public bool TryBuildFilter<T>(string query, out Expression<Func<T, bool>>? expression, EntityFrameworkQueryVisitorContext? context, EntityFrameworkQueryOptions? options) where T : class
    {
        var result = TryBuildFilter<T>(query, context, options);
        expression = result.GetValueOrDefault();
        return result.IsSuccess;
    }

    /// <summary>
    /// Parses a Lucene query string using EF entity type metadata for field discovery.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="entityType">The EF entity type metadata.</param>
    /// <returns>An expression that can be used with EF's Where method.</returns>
    public Expression<Func<T, bool>> BuildFilter<T>(string query, IEntityType entityType) where T : class
    {
        var context = GetContext(entityType);
        return BuildFilter<T>(query, context);
    }

    /// <summary>
    /// Parses a Lucene query string and returns a dynamically typed filter expression.
    /// </summary>
    /// <param name="entityType">The entity type to filter.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <returns>A lambda expression that can be used with EF's Where method.</returns>
    public LambdaExpression BuildFilter(Type entityType, string query, EntityFrameworkQueryVisitorContext? context = null)
    {
        return BuildFilter(entityType, query, context, null);
    }

    /// <summary>
    /// Parses a Lucene query string and returns a dynamically typed filter expression with per-request options.
    /// </summary>
    /// <param name="entityType">The entity type to filter.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <param name="options">Optional per-request options to merge with global configuration.</param>
    /// <returns>A lambda expression that can be used with EF's Where method.</returns>
    public LambdaExpression BuildFilter(Type entityType, string query, EntityFrameworkQueryVisitorContext? context, EntityFrameworkQueryOptions? options)
    {
        context ??= new EntityFrameworkQueryVisitorContext();
        SetupContextDefaults(entityType, context, options);

        var document = ParseQuery(query);
        ApplyVisitorPipeline(document, context, entityType, options);

        return ExpressionBuilderVisitor.Instance.BuildExpression(entityType, document, context, Configuration);
    }

    /// <summary>
    /// Tries to parse a Lucene query string and returns a dynamically typed filter expression.
    /// Returns a result object instead of throwing exceptions.
    /// </summary>
    /// <param name="entityType">The entity type to filter.</param>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <param name="options">Optional per-request options to merge with global configuration.</param>
    /// <returns>A QueryResult containing the expression or error information.</returns>
    public QueryResult<LambdaExpression> TryBuildFilter(Type entityType, string query, EntityFrameworkQueryVisitorContext? context = null, EntityFrameworkQueryOptions? options = null)
    {
        try
        {
            var expression = BuildFilter(entityType, query, context, options);
            return QueryResult<LambdaExpression>.Success(expression);
        }
        catch (QueryException ex)
        {
            return QueryResult<LambdaExpression>.Failure(ex);
        }
        catch (FormatException ex)
        {
            return QueryResult<LambdaExpression>.Failure(
                new QueryParseException(ex.Message, QueryErrorCode.ParseError, ex));
        }
        catch (Exception ex)
        {
            return QueryResult<LambdaExpression>.Failure(
                new QueryBuildException($"Failed to build filter: {ex.Message}", ex));
        }
    }

    private Ast.QueryDocument ParseQuery(string query)
    {
        var parseResult = LuceneQuery.Parse(query, Configuration.DefaultOperator);
        if (!parseResult.IsSuccess && parseResult.Errors.Count > 0)
            throw new FormatException($"Failed to parse query: {string.Join(", ", parseResult.Errors.Select(e => e.Message))}");
        return parseResult.Document;
    }

    /// <summary>
    /// Runs the shared pre-build visitor pipeline (field alias resolution, @include expansion,
    /// and date-math evaluation) over the parsed document so these behave identically to the
    /// Elasticsearch integration. Per-scope field map and includes come from per-request options
    /// (highest precedence) then options registered for the entity type.
    /// </summary>
    private void ApplyVisitorPipeline(Ast.QueryDocument document, EntityFrameworkQueryVisitorContext context, Type entityType, EntityFrameworkQueryOptions? options)
    {
        if (document.Query is null)
            return;

        var registeredOptions = GetOptions(entityType);

        // Resolve field aliases (per-request > registered)
        var fieldMap = options?.FieldMap ?? registeredOptions?.FieldMap;
        if (fieldMap is not null)
        {
            context.SetFieldMap(fieldMap);
            document.Query = _fieldResolverVisitor.Accept(document.Query, context);
        }

        // Expand @includes (per-request > registered)
        var includes = options?.Includes ?? registeredOptions?.Includes;
        if (includes is not null)
        {
            context.SetIncludes(includes);
            document.Query = _includeVisitor.Accept(document.Query, context);
        }

        // Evaluate date math (now-7d, now/d, 2024-01-01||+1M, ...)
        document.Query = _dateMathVisitor.Accept(document.Query, context);
    }

    /// <summary>
    /// Gets a visitor context initialized with field information from an EF entity type.
    /// </summary>
    /// <param name="entityType">The EF entity type metadata.</param>
    /// <returns>A configured visitor context.</returns>
    public EntityFrameworkQueryVisitorContext GetContext(IEntityType entityType)
    {
        // Only use cache if no custom filters are configured
        var useCache = !Configuration.HasCustomPropertyFilter &&
                       !Configuration.HasCustomNavigationFilter &&
                       !Configuration.HasCustomSkipNavigationFilter;

        List<EntityFieldInfo> fields;

        if (useCache && _entityFieldCache.TryGetValue(entityType, out var cachedFields))
        {
            fields = cachedFields.ToList();
        }
        else
        {
            fields = [];
            AddEntityFields(fields, null, entityType);

            if (useCache)
            {
                _entityFieldCache.TryAdd(entityType, fields);
                fields = fields.ToList();
            }
        }

        var validationOptions = new QueryValidationOptions();
        foreach (var field in fields.Where(f => !f.IsNavigation).Select(f => f.FullName))
            validationOptions.AllowedFields.Add(field);

        return new EntityFrameworkQueryVisitorContext
        {
            Fields = fields,
            EntityType = entityType,
            DefaultFields = Configuration.DefaultFields,
            DateTimeParser = Configuration.DateTimeParser,
            DateOnlyParser = Configuration.DateOnlyParser
        };
    }

    /// <summary>
    /// Validates a Lucene query string.
    /// </summary>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <param name="options">Optional per-request options for validation.</param>
    /// <returns>The validation result.</returns>
    public QueryValidationResult Validate(string query, EntityFrameworkQueryVisitorContext? context = null, EntityFrameworkQueryOptions? options = null)
    {
        context ??= new EntityFrameworkQueryVisitorContext();

        // Apply validation options from per-request options
        if (options?.ValidationOptions is not null)
        {
            context.SetValidationOptions(options.ValidationOptions);
        }

        var parseResult = LuceneQuery.Parse(query, Configuration.DefaultOperator);

        // Add parse errors as validation errors
        foreach (var error in parseResult.Errors)
        {
            context.AddValidationError(error.Message, error.Position);
        }

        // Validate the document if it exists
        if (parseResult.Document is not null)
        {
            var visitor = new Visitors.ValidationVisitor();
            visitor.Accept(parseResult.Document, context);
            visitor.ApplyRestrictions(context);
        }

        return context.GetValidationResult();
    }

    private void SetupContextDefaults<T>(EntityFrameworkQueryVisitorContext context, EntityFrameworkQueryOptions? options)
    {
        SetupContextDefaults(typeof(T), context, options);
    }

    private void SetupContextDefaults(Type entityType, EntityFrameworkQueryVisitorContext context, EntityFrameworkQueryOptions? options)
    {
        // Get registered options for this entity type (if any)
        var registeredOptions = GetOptions(entityType);

        // Discover fields from cache or reflection if not already set
        if (context.Fields.Count == 0)
        {
            if (_reflectionFieldCache.TryGetValue(entityType, out var cachedFields))
            {
                // Use cached fields - make a copy to avoid mutation issues
                context.Fields.AddRange(cachedFields);
            }
            else
            {
                DiscoverFieldsFromReflection(context.Fields, null, entityType);
                // Cache the discovered fields
                _reflectionFieldCache.TryAdd(entityType, [.. context.Fields]);
            }
        }

        // Add registered additional fields first
        if (registeredOptions?.AdditionalFields is not null)
        {
            context.Fields.AddRange(registeredOptions.AdditionalFields);
        }

        // Add per-request additional fields (can override registered)
        if (options?.AdditionalFields is not null)
        {
            context.Fields.AddRange(options.AdditionalFields);
        }

        // Attach registered field data first
        ApplyFieldData(context, registeredOptions?.FieldData);

        // Attach per-request field data (can override registered)
        ApplyFieldData(context, options?.FieldData);

        // Apply defaults: per-request > registered > configuration
        context.DefaultFields ??= options?.DefaultFields ?? registeredOptions?.DefaultFields ?? Configuration.DefaultFields;
        context.DateTimeParser ??= Configuration.DateTimeParser;
        context.DateOnlyParser ??= Configuration.DateOnlyParser;

        // Apply validation options: per-request > registered
        var validationOptions = options?.ValidationOptions ?? registeredOptions?.ValidationOptions;
        if (validationOptions is not null)
        {
            context.SetValidationOptions(validationOptions);
        }
    }

    private static void ApplyFieldData(EntityFrameworkQueryVisitorContext context, IReadOnlyDictionary<string, object?>? fieldData)
    {
        if (fieldData is null)
            return;

        foreach (var kvp in fieldData)
        {
            var field = context.Fields.FirstOrDefault(f => f.Name.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (field is not null && kvp.Value is not null)
            {
                foreach (var dataEntry in (IDictionary<string, object?>)kvp.Value)
                {
                    if (dataEntry.Value is not null)
                    {
                        field.Data[dataEntry.Key] = dataEntry.Value;
                    }
                }
            }
        }
    }

    private void AddEntityFields(List<EntityFieldInfo> fields, EntityFieldInfo? parent, IEntityType entityType, Stack<IEntityType>? entityTypeStack = null, string? prefix = null, int depth = 0)
    {
        entityTypeStack ??= new Stack<IEntityType>();

        if (depth > 0 && entityTypeStack.Contains(entityType))
            return;

        entityTypeStack.Push(entityType);

        if (depth > Configuration.MaxFieldDepth)
            return;

        prefix ??= "";

        foreach (var property in entityType.GetProperties())
        {
            if (!Configuration.EntityTypePropertyFilter(property))
                continue;

            var propertyPath = prefix + property.Name;
            var clrType = property.ClrType;
            var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;

            fields.Add(new EntityFieldInfo
            {
                Name = property.Name,
                FullName = propertyPath,
                ClrType = clrType,
                IsNumber = IsNumericType(underlyingType),
                IsDate = underlyingType == typeof(DateTime),
                IsDateOnly = underlyingType == typeof(DateOnly),
                IsBoolean = underlyingType == typeof(bool),
                IsString = underlyingType == typeof(string),
                DeclaringTypeName = entityType.ClrType.Name,
                Parent = parent,
                Property = property
            });
        }

        foreach (var nav in entityType.GetNavigations())
        {
            if (!Configuration.EntityTypeNavigationFilter(nav))
                continue;

            var propertyPath = prefix + nav.Name;
            var isNavCollection = nav.IsCollection;

            var navFieldInfo = new EntityFieldInfo
            {
                Name = nav.Name,
                FullName = propertyPath,
                ClrType = nav.ClrType,
                IsCollection = isNavCollection,
                IsNavigation = true,
                Parent = parent,
                Navigation = nav
            };
            fields.Add(navFieldInfo);

            AddEntityFields(fields, navFieldInfo, nav.TargetEntityType, entityTypeStack, propertyPath + ".", depth + 1);
        }

        foreach (var skipNav in entityType.GetSkipNavigations())
        {
            if (!Configuration.EntityTypeSkipNavigationFilter(skipNav))
                continue;

            var propertyPath = prefix + skipNav.Name;

            var navFieldInfo = new EntityFieldInfo
            {
                Name = skipNav.Name,
                FullName = propertyPath,
                ClrType = skipNav.ClrType,
                IsCollection = skipNav.IsCollection,
                IsNavigation = true,
                Parent = parent,
                Navigation = skipNav
            };
            fields.Add(navFieldInfo);

            AddEntityFields(fields, navFieldInfo, skipNav.TargetEntityType, entityTypeStack, propertyPath + ".", depth + 1);
        }

        entityTypeStack.Pop();
    }

    private void DiscoverFieldsFromReflection(List<EntityFieldInfo> fields, EntityFieldInfo? parent, Type entityType, HashSet<Type>? visitedTypes = null, string? prefix = null, int depth = 0)
    {
        visitedTypes ??= [];

        if (depth > 0 && visitedTypes.Contains(entityType))
            return;

        visitedTypes.Add(entityType);

        if (depth > Configuration.MaxFieldDepth)
            return;

        prefix ??= "";

        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var propertyPath = prefix + property.Name;
            var clrType = property.PropertyType;
            var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;

            // Skip indexer properties
            if (property.GetIndexParameters().Length > 0)
                continue;

            // Check if this is a collection type (for navigation properties)
            var isCollection = IsCollectionType(clrType);
            var elementType = isCollection ? GetCollectionElementType(clrType) : null;

            // Determine if this is a "simple" type or a navigation property
            var isSimpleType = IsSimpleType(underlyingType);

            if (isSimpleType)
            {
                fields.Add(new EntityFieldInfo
                {
                    Name = property.Name,
                    FullName = propertyPath,
                    ClrType = clrType,
                    IsNumber = IsNumericType(underlyingType),
                    IsDate = underlyingType == typeof(DateTime),
                    IsDateOnly = underlyingType == typeof(DateOnly),
                    IsBoolean = underlyingType == typeof(bool),
                    IsString = underlyingType == typeof(string),
                    DeclaringTypeName = entityType.Name,
                    Parent = parent
                });
            }
            else if (isCollection && elementType != null && !IsSimpleType(elementType))
            {
                // Collection navigation property
                var navFieldInfo = new EntityFieldInfo
                {
                    Name = property.Name,
                    FullName = propertyPath,
                    ClrType = clrType,
                    IsCollection = true,
                    IsNavigation = true,
                    Parent = parent
                };
                fields.Add(navFieldInfo);

                DiscoverFieldsFromReflection(fields, navFieldInfo, elementType, visitedTypes, propertyPath + ".", depth + 1);
            }
            else if (!isSimpleType && !isCollection)
            {
                // Reference navigation property
                var navFieldInfo = new EntityFieldInfo
                {
                    Name = property.Name,
                    FullName = propertyPath,
                    ClrType = clrType,
                    IsNavigation = true,
                    Parent = parent
                };
                fields.Add(navFieldInfo);

                DiscoverFieldsFromReflection(fields, navFieldInfo, clrType, visitedTypes, propertyPath + ".", depth + 1);
            }
        }

        visitedTypes.Remove(entityType);
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateOnly) ||
               type == typeof(TimeOnly) ||
               type == typeof(TimeSpan) ||
               type == typeof(Guid) ||
               type == typeof(byte[]);
    }

    internal static bool IsNumericType(Type type)
    {
        return type == typeof(int) || type == typeof(long) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(decimal) || type == typeof(double) ||
               type == typeof(float) || type == typeof(uint) || type == typeof(ulong) ||
               type == typeof(ushort) || type == typeof(sbyte);
    }

    internal static bool IsCollectionType(Type type)
    {
        if (type == typeof(string))
            return false;

        return type.IsGenericType &&
               (type.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                type.GetGenericTypeDefinition() == typeof(IList<>) ||
                type.GetGenericTypeDefinition() == typeof(List<>) ||
                type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>)));
    }

    internal static Type? GetCollectionElementType(Type collectionType)
    {
        if (collectionType.IsGenericType)
        {
            return collectionType.GetGenericArguments().FirstOrDefault();
        }

        var enumerableInterface = collectionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerableInterface?.GetGenericArguments().FirstOrDefault();
    }
}
