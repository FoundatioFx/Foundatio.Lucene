using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.LuceneQuery.EntityFramework;

/// <summary>
/// Parses Lucene query strings and converts them to Entity Framework filter expressions.
/// </summary>
public class EntityFrameworkQueryParser
{
    private static readonly ConcurrentDictionary<IEntityType, List<EntityFieldInfo>> _entityFieldCache = new();
    private static readonly ConcurrentDictionary<Type, List<EntityFieldInfo>> _reflectionFieldCache = new();
    private readonly ExpressionBuilderVisitor _expressionBuilder = new();

    /// <summary>
    /// Creates a new EntityFrameworkQueryParser with optional configuration.
    /// </summary>
    public EntityFrameworkQueryParser(Action<EntityFrameworkQueryParserConfiguration>? configure = null)
    {
        var config = new EntityFrameworkQueryParserConfiguration();
        configure?.Invoke(config);
        Configuration = config;
    }

    /// <summary>
    /// The parser configuration.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration Configuration { get; }

    /// <summary>
    /// Parses a Lucene query string and returns a filter expression.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <returns>An expression that can be used with EF's Where method.</returns>
    public Expression<Func<T, bool>> BuildFilter<T>(string query, EntityFrameworkQueryVisitorContext? context = null) where T : class
    {
        context ??= new EntityFrameworkQueryVisitorContext();
        SetupContextDefaults<T>(context);

        var document = ParseQuery(query);
        return _expressionBuilder.BuildExpression<T>(document, context, Configuration);
    }

    /// <summary>
    /// Tries to parse a Lucene query string and returns a filter expression.
    /// </summary>
    /// <typeparam name="T">The entity type to filter.</typeparam>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="expression">The resulting filter expression if successful.</param>
    /// <param name="context">Optional query visitor context.</param>
    /// <returns>True if parsing succeeded; false otherwise.</returns>
    public bool TryBuildFilter<T>(string query, out Expression<Func<T, bool>>? expression, EntityFrameworkQueryVisitorContext? context = null) where T : class
    {
        try
        {
            expression = BuildFilter<T>(query, context);
            return true;
        }
        catch
        {
            expression = null;
            return false;
        }
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
        context ??= new EntityFrameworkQueryVisitorContext();
        SetupContextDefaults(entityType, context);

        var document = ParseQuery(query);
        return _expressionBuilder.BuildExpression(entityType, document, context, Configuration);
    }

    private Ast.QueryDocument ParseQuery(string query)
    {
        var parseResult = LuceneQuery.Parse(query, Configuration.DefaultOperator);
        if (!parseResult.IsSuccess && parseResult.Errors.Count > 0)
            throw new FormatException($"Failed to parse query: {string.Join(", ", parseResult.Errors.Select(e => e.Message))}");
        return parseResult.Document;
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
    /// <returns>The validation result.</returns>
    public QueryValidationResult Validate(string query, EntityFrameworkQueryVisitorContext? context = null)
    {
        var parseResult = LuceneQuery.Parse(query, Configuration.DefaultOperator);

        foreach (var error in parseResult.Errors)
        {
            context?.AddValidationError(error.Message);
        }

        return context?.GetValidationResult() ?? new QueryValidationResult();
    }

    private void SetupContextDefaults<T>(EntityFrameworkQueryVisitorContext context)
    {
        SetupContextDefaults(typeof(T), context);
    }

    private void SetupContextDefaults(Type entityType, EntityFrameworkQueryVisitorContext context)
    {
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

        // Apply configuration defaults
        context.DefaultFields ??= Configuration.DefaultFields;
        context.DateTimeParser ??= Configuration.DateTimeParser;
        context.DateOnlyParser ??= Configuration.DateOnlyParser;
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
