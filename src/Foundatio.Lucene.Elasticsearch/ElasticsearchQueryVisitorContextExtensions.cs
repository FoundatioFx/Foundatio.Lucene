namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Extension methods for configuring ElasticsearchQueryVisitorContext.
/// </summary>
public static class ElasticsearchQueryVisitorContextExtensions
{
    /// <summary>
    /// Enables scoring mode (uses match queries instead of term queries).
    /// </summary>
    public static T UseScoring<T>(this T context) where T : IElasticsearchQueryVisitorContext
    {
        context.UseScoring = true;
        return context;
    }

    /// <summary>
    /// Configures context for search mode with scoring and OR operator.
    /// </summary>
    public static T UseSearchMode<T>(this T context) where T : IElasticsearchQueryVisitorContext
    {
        context.UseScoring = true;
        context.DefaultOperator = QueryOperator.Or;
        return context;
    }

    /// <summary>
    /// Sets the default boolean operator.
    /// </summary>
    public static T SetDefaultOperator<T>(this T context, QueryOperator op) where T : IElasticsearchQueryVisitorContext
    {
        context.DefaultOperator = op;
        return context;
    }

    /// <summary>
    /// Sets the default fields to search.
    /// </summary>
    public static T SetDefaultFields<T>(this T context, params string[] fields) where T : IElasticsearchQueryVisitorContext
    {
        context.DefaultFields = fields;
        return context;
    }

    /// <summary>
    /// Sets the default timezone for date queries.
    /// </summary>
    public static T SetTimeZone<T>(this T context, string timeZone) where T : IElasticsearchQueryVisitorContext
    {
        context.DefaultTimeZone = timeZone;
        return context;
    }

    /// <summary>
    /// Sets the function to resolve location strings to coordinates.
    /// </summary>
    public static T SetGeoLocationResolver<T>(this T context, Func<string, Task<string?>> resolver) where T : IElasticsearchQueryVisitorContext
    {
        context.GeoLocationResolver = resolver;
        return context;
    }

    /// <summary>
    /// Sets the function to determine if a field is a geo_point field.
    /// </summary>
    public static T SetGeoPointFieldResolver<T>(this T context, Func<string, bool> resolver) where T : IElasticsearchQueryVisitorContext
    {
        context.IsGeoPointField = resolver;
        return context;
    }

    /// <summary>
    /// Sets the function to determine if a field is a date field.
    /// </summary>
    public static T SetDateFieldResolver<T>(this T context, Func<string, bool> resolver) where T : IElasticsearchQueryVisitorContext
    {
        context.IsDateField = resolver;
        return context;
    }
}
