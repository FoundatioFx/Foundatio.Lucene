using System.Linq.Expressions;
using Foundatio.Lucene.Ast;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Foundatio.Lucene.EntityFramework;

/// <summary>
/// Decides whether a scalar property of the EF Core model can be queried.
/// </summary>
public delegate bool EntityTypePropertyFilter(IProperty property);

/// <summary>
/// Decides whether a navigation (including owned types) of the EF Core model can be queried.
/// </summary>
public delegate bool EntityTypeNavigationFilter(INavigation navigation);

/// <summary>
/// Decides whether a many-to-many skip navigation of the EF Core model can be queried.
/// </summary>
public delegate bool EntityTypeSkipNavigationFilter(ISkipNavigation navigation);

/// <summary>
/// Decides whether a complex property of the EF Core model can be queried.
/// </summary>
public delegate bool EntityTypeComplexPropertyFilter(IComplexProperty property);

/// <summary>
/// Builds the predicate for a regular expression query (<c>field:/pattern/</c>). <paramref name="field"/> is the string
/// member being matched and <paramref name="pattern"/> is a parameterized expression holding the pattern.
/// </summary>
public delegate Expression RegexExpressionBuilder(Expression field, Expression pattern);

/// <summary>
/// Configuration for <see cref="EntityFrameworkQueryParser"/>. Configure it once when the parser is created; field
/// metadata is cached per parser using these settings.
/// </summary>
public class EntityFrameworkQueryParserConfiguration : QueryParserConfiguration
{
    /// <summary>
    /// How terms without a field are matched against string <see cref="QueryParserConfiguration.DefaultFields"/>.
    /// Defaults to <see cref="SearchOperator.StartsWith"/>.
    /// </summary>
    public SearchOperator DefaultSearchOperator { get; set; } = SearchOperator.StartsWith;

    /// <summary>
    /// Fields with a SQL Server full-text index. Terms on these fields use <c>EF.Functions.Contains</c>. Entries are
    /// field paths from the queried entity (<c>Company.Name</c>) or a declaring type and property name
    /// (<c>Company.Name</c> also matches the <c>Name</c> property of <c>Company</c> wherever it is reached).
    /// </summary>
    public ISet<string> FullTextFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The maximum number of navigations a field path may traverse. Defaults to 10.
    /// </summary>
    public int MaxFieldDepth { get; set; } = 10;

    /// <summary>
    /// Excludes scalar properties from querying. Excluded properties are unknown fields, so queries that use them are
    /// invalid.
    /// </summary>
    public EntityTypePropertyFilter? PropertyFilter { get; set; }

    /// <summary>
    /// Excludes navigations (and everything reached through them) from querying.
    /// </summary>
    public EntityTypeNavigationFilter? NavigationFilter { get; set; }

    /// <summary>
    /// Excludes skip navigations (and everything reached through them) from querying.
    /// </summary>
    public EntityTypeSkipNavigationFilter? SkipNavigationFilter { get; set; }

    /// <summary>
    /// Excludes complex properties (and everything reached through them) from querying.
    /// </summary>
    public EntityTypeComplexPropertyFilter? ComplexPropertyFilter { get; set; }

    /// <summary>
    /// Builds expressions for custom fields (for example dynamic or EAV fields). It is called for every field
    /// comparison; return null to use the default expression.
    /// </summary>
    public CustomFieldExpressionBuilder? CustomFieldExpressionBuilder { get; set; }

    /// <summary>
    /// Splits terms searched against default fields into tokens and chooses the operator for each default field.
    /// </summary>
    public SearchTokenizer? SearchTokenizer { get; set; }

    /// <summary>
    /// The time zone used for <c>now</c>, date math rounding, and dates written without an offset. Defaults to UTC.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// The time zone of the wall-clock values stored in <see cref="DateTime"/> columns. Query dates are converted to
    /// this zone before they are compared. Defaults to UTC.
    /// </summary>
    public TimeZoneInfo DateTimeStorageTimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Builds regular expression predicates. When null (the default) regular expression queries are rejected, because
    /// most databases cannot translate them and client evaluation is vulnerable to catastrophic backtracking.
    /// </summary>
    public RegexExpressionBuilder? RegexExpressionBuilder { get; set; }

    /// <summary>
    /// The EF Core model used to discover fields when a request does not supply one. The <c>DbSet</c> and
    /// <c>IQueryable</c> extension methods supply the model automatically.
    /// </summary>
    public IModel? Model { get; set; }

    /// <summary>
    /// Sets the operator used between clauses written without AND or OR.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDefaultOperator(BooleanOperator op)
    {
        DefaultOperator = op;
        return this;
    }

    /// <summary>
    /// Sets the fields searched by terms that have no field.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDefaultFields(params string[] fields)
    {
        DefaultFields = fields;
        return this;
    }

    /// <summary>
    /// Sets the fields searched by terms that have no field and how string fields are matched.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDefaultFields(string[] fields, SearchOperator searchOperator)
    {
        DefaultFields = fields;
        DefaultSearchOperator = searchOperator;
        return this;
    }

    /// <summary>
    /// Sets how terms without a field are matched against string default fields.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDefaultSearchOperator(SearchOperator searchOperator)
    {
        DefaultSearchOperator = searchOperator;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of navigations a field path may traverse.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetMaxFieldDepth(int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        MaxFieldDepth = maxDepth;
        return this;
    }

    /// <summary>
    /// Sets the clock used for <c>now</c>.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetTimeProvider(TimeProvider timeProvider)
    {
        TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        return this;
    }

    /// <summary>
    /// Sets the time zone used for <c>now</c>, date math rounding, and dates written without an offset.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetTimeZone(TimeZoneInfo timeZone)
    {
        TimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        return this;
    }

    /// <summary>
    /// Sets the time zone of the values stored in <see cref="DateTime"/> columns.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration SetDateTimeStorageTimeZone(TimeZoneInfo timeZone)
    {
        DateTimeStorageTimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        return this;
    }

    /// <summary>
    /// Sets the EF Core model used to discover fields when a request does not supply one.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseModel(IModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        return this;
    }

    /// <summary>
    /// Excludes scalar properties for which <paramref name="filter"/> returns false.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseEntityTypePropertyFilter(EntityTypePropertyFilter filter)
    {
        PropertyFilter = filter ?? throw new ArgumentNullException(nameof(filter));
        return this;
    }

    /// <summary>
    /// Excludes navigations for which <paramref name="filter"/> returns false.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseEntityTypeNavigationFilter(EntityTypeNavigationFilter filter)
    {
        NavigationFilter = filter ?? throw new ArgumentNullException(nameof(filter));
        return this;
    }

    /// <summary>
    /// Excludes skip navigations for which <paramref name="filter"/> returns false.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseEntityTypeSkipNavigationFilter(EntityTypeSkipNavigationFilter filter)
    {
        SkipNavigationFilter = filter ?? throw new ArgumentNullException(nameof(filter));
        return this;
    }

    /// <summary>
    /// Excludes complex properties for which <paramref name="filter"/> returns false.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseEntityTypeComplexPropertyFilter(EntityTypeComplexPropertyFilter filter)
    {
        ComplexPropertyFilter = filter ?? throw new ArgumentNullException(nameof(filter));
        return this;
    }

    /// <summary>
    /// Sets the builder for custom field expressions.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseCustomFieldExpressionBuilder(CustomFieldExpressionBuilder builder)
    {
        CustomFieldExpressionBuilder = builder ?? throw new ArgumentNullException(nameof(builder));
        return this;
    }

    /// <summary>
    /// Sets the tokenizer for terms searched against default fields.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseSearchTokenizer(SearchTokenizer tokenizer)
    {
        SearchTokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        return this;
    }

    /// <summary>
    /// Allows regular expression queries, translating them with <paramref name="builder"/>.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseRegex(RegexExpressionBuilder builder)
    {
        RegexExpressionBuilder = builder ?? throw new ArgumentNullException(nameof(builder));
        return this;
    }

    /// <summary>
    /// Marks fields as full-text indexed. See <see cref="FullTextFields"/> for the name format.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration AddFullTextFields(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (string field in fields)
            FullTextFields.Add(field);

        return this;
    }

    /// <summary>
    /// Sets the field aliases.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseFieldMap(FieldMap fieldMap)
    {
        FieldMap = fieldMap ?? throw new ArgumentNullException(nameof(fieldMap));
        return this;
    }

    /// <summary>
    /// Sets the query text for <c>@include:name</c> references.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseIncludes(IReadOnlyDictionary<string, string> includes)
    {
        Includes = includes ?? throw new ArgumentNullException(nameof(includes));
        return this;
    }

    /// <summary>
    /// Sets the validation rules applied to every query.
    /// </summary>
    public EntityFrameworkQueryParserConfiguration UseValidationOptions(QueryValidationOptions options)
    {
        ValidationOptions = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}
