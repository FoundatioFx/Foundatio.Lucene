using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Extension methods for configuring ElasticsearchQueryParserConfiguration.
/// </summary>
public static class ElasticsearchQueryParserConfigurationExtensions
{
    /// <summary>
    /// Enables scoring mode (uses match queries instead of term queries).
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseScoring(this ElasticsearchQueryParserConfiguration config)
    {
        config.UseScoring = true;
        return config;
    }

    /// <summary>
    /// Configures the parser for search mode with scoring and OR operator.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseSearchMode(this ElasticsearchQueryParserConfiguration config)
    {
        config.UseScoring = true;
        config.DefaultOperator = QueryOperator.Or;
        return config;
    }

    /// <summary>
    /// Sets the default boolean operator.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration SetDefaultOperator(this ElasticsearchQueryParserConfiguration config, QueryOperator op)
    {
        config.DefaultOperator = op;
        return config;
    }

    /// <summary>
    /// Sets the default fields to search.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration SetDefaultFields(this ElasticsearchQueryParserConfiguration config, params string[] fields)
    {
        config.DefaultFields = fields;
        return config;
    }

    /// <summary>
    /// Sets the field map for resolving field aliases.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseFieldMap(this ElasticsearchQueryParserConfiguration config, FieldMap fieldMap)
    {
        config.FieldMap = fieldMap;
        return config;
    }

    /// <summary>
    /// Configures which fields are date fields.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseDateFields(this ElasticsearchQueryParserConfiguration config, Func<string, bool> resolver)
    {
        config.IsDateField = resolver;
        return config;
    }

    /// <summary>
    /// Configures which fields are geo_point fields.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseGeoFields(this ElasticsearchQueryParserConfiguration config, Func<string, bool> resolver)
    {
        config.IsGeoPointField = resolver;
        return config;
    }

    /// <summary>
    /// Sets the geo location resolver.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseGeoLocationResolver(this ElasticsearchQueryParserConfiguration config, Func<string, Task<string?>> resolver)
    {
        config.GeoLocationResolver = resolver;
        return config;
    }

    /// <summary>
    /// Sets the default timezone for date queries.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration SetDefaultTimeZone(this ElasticsearchQueryParserConfiguration config, string timeZone)
    {
        config.DefaultTimeZone = timeZone;
        return config;
    }

    /// <summary>
    /// Sets the include resolver.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseIncludes(this ElasticsearchQueryParserConfiguration config, IncludeResolver resolver)
    {
        config.IncludeResolver = resolver;
        return config;
    }

    /// <summary>
    /// Sets validation options.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration UseValidation(this ElasticsearchQueryParserConfiguration config, QueryValidationOptions options)
    {
        config.ValidationOptions = options;
        return config;
    }

    /// <summary>
    /// Adds a custom visitor to the visitor chain.
    /// </summary>
    public static ElasticsearchQueryParserConfiguration AddVisitor(this ElasticsearchQueryParserConfiguration config, QueryNodeVisitor visitor)
    {
        config.Visitors.Add(visitor);
        return config;
    }
}
