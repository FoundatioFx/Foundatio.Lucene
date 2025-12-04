using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Parser that converts Lucene query strings to Elasticsearch Query DSL.
/// </summary>
public class ElasticsearchQueryParser
{
    private readonly ElasticsearchQueryParserConfiguration _config;
    private readonly List<QueryNodeVisitor> _visitors = [];

    /// <summary>
    /// Creates a new parser with default configuration.
    /// </summary>
    public ElasticsearchQueryParser() : this(null) { }

    /// <summary>
    /// Creates a new parser with the specified configuration.
    /// </summary>
    public ElasticsearchQueryParser(Action<ElasticsearchQueryParserConfiguration>? configure)
    {
        _config = new ElasticsearchQueryParserConfiguration();
        configure?.Invoke(_config);

        // Build the visitor chain
        BuildVisitorChain();
    }

    private void BuildVisitorChain()
    {
        // Add field resolver if field map is provided
        if (_config.FieldMap is not null)
        {
            _visitors.Add(new FieldResolverQueryVisitor(_config.FieldMap.ToHierarchicalFieldResolver()));
        }

        // Add include resolver if provided
        if (_config.IncludeResolver is not null)
        {
            _visitors.Add(new IncludeVisitor(_config.IncludeResolver));
        }

        // Add date math evaluator
        _visitors.Add(new DateMathEvaluatorVisitor());

        // Add custom visitors
        _visitors.AddRange(_config.Visitors);

        // Add validation visitor last
        _visitors.Add(new ValidationVisitor());
    }

    /// <summary>
    /// Parses a Lucene query string and returns the AST.
    /// </summary>
    public LuceneParseResult Parse(string query)
    {
        return LuceneQuery.Parse(query);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a Lucene query string.
    /// </summary>
    public Query BuildQuery(string query)
    {
        return BuildQueryAsync(query).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a Lucene query string asynchronously.
    /// </summary>
    public Task<Query> BuildQueryAsync(string query)
    {
        var parseResult = LuceneQuery.Parse(query);

        if (!parseResult.IsSuccess)
        {
            var errors = string.Join("; ", parseResult.Errors.Select(e => e.Message));
            throw new QueryParseException($"Failed to parse query: {errors}");
        }

        return BuildQueryAsync(parseResult.Document);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed query document.
    /// </summary>
    public Query BuildQuery(QueryDocument document)
    {
        return BuildQueryAsync(document).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed query document asynchronously.
    /// </summary>
    public async Task<Query> BuildQueryAsync(QueryDocument document)
    {
        // Create the visitor context
        var context = CreateContext();

        // Run the visitor chain
        QueryNode currentNode = document;
        foreach (var visitor in _visitors)
        {
            currentNode = await visitor.AcceptAsync(currentNode, context).ConfigureAwait(false);
        }

        // Build the Elasticsearch query
        var builder = new ElasticsearchQueryBuilderVisitor();
        return await builder.BuildQueryAsync(currentNode, context).ConfigureAwait(false);
    }

    private ElasticsearchQueryVisitorContext CreateContext()
    {
        var context = new ElasticsearchQueryVisitorContext
        {
            UseScoring = _config.UseScoring,
            DefaultFields = _config.DefaultFields,
            DefaultOperator = _config.DefaultOperator,
            IsGeoPointField = _config.IsGeoPointField,
            IsDateField = _config.IsDateField,
            DefaultTimeZone = _config.DefaultTimeZone,
            GeoLocationResolver = _config.GeoLocationResolver
        };

        // Set up validation options
        if (_config.ValidationOptions is not null)
        {
            context.SetValidationOptions(_config.ValidationOptions);
        }

        return context;
    }

    /// <summary>
    /// Adds a custom visitor to the visitor chain.
    /// </summary>
    public ElasticsearchQueryParser AddVisitor(QueryNodeVisitor visitor)
    {
        _visitors.Add(visitor);
        return this;
    }
}
