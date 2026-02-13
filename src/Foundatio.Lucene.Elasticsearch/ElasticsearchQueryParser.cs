using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, ElasticsearchQueryOptions> _indexOptions = new(StringComparer.OrdinalIgnoreCase);

    // Stateless visitors that can be safely reused as singletons
    private readonly FieldResolverQueryVisitor? _fieldResolverVisitor;
    private readonly IncludeVisitor? _includeVisitor;
    private readonly DateMathEvaluatorVisitor _dateMathVisitor = new();
    private readonly ValidationVisitor _validationVisitor = new();
    private readonly List<QueryVisitor> _customVisitors;

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

        // Initialize stateless visitors that can be reused across all requests
        if (_config.FieldMap is not null)
        {
            _fieldResolverVisitor = new FieldResolverQueryVisitor(_config.FieldMap);
        }

        if (_config.Includes is not null)
        {
            _includeVisitor = new IncludeVisitor(_config.Includes);
        }

        _customVisitors = [.. _config.Visitors];
    }

    /// <summary>
    /// Registers options for a specific index.
    /// These options are used as the base configuration when building queries for this index.
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <param name="options">The options to register.</param>
    /// <returns>This parser instance for chaining.</returns>
    public ElasticsearchQueryParser SetOptions(string indexName, ElasticsearchQueryOptions options)
    {
        _indexOptions[indexName] = options;
        return this;
    }

    /// <summary>
    /// Registers options for a specific index using a configuration action.
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <param name="configure">Action to configure the options.</param>
    /// <returns>This parser instance for chaining.</returns>
    public ElasticsearchQueryParser SetOptions(string indexName, Action<ElasticsearchQueryOptionsBuilder> configure)
    {
        var builder = new ElasticsearchQueryOptionsBuilder();
        configure(builder);
        return SetOptions(indexName, builder.Build());
    }

    /// <summary>
    /// Gets the registered options for a specific index, or null if not registered.
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <returns>The registered options, or null.</returns>
    public ElasticsearchQueryOptions? GetOptions(string indexName)
    {
        return _indexOptions.TryGetValue(indexName, out var options) ? options : null;
    }

    /// <summary>
    /// Removes registered options for a specific index.
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <returns>True if options were removed.</returns>
    public bool RemoveOptions(string indexName)
    {
        return _indexOptions.TryRemove(indexName, out _);
    }

    /// <summary>
    /// Clears all registered index options.
    /// </summary>
    public void ClearOptions()
    {
        _indexOptions.Clear();
    }

    /// <summary>
    /// Gets all index names that have registered options.
    /// </summary>
    public IEnumerable<string> RegisteredIndexes => _indexOptions.Keys;

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
        return BuildQuery(query, indexName: null, options: null);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a Lucene query string using registered options for the specified index.
    /// </summary>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="indexName">The index name to look up registered options for.</param>
    /// <returns>The Elasticsearch Query DSL.</returns>
    public Query BuildQuery(string query, string indexName)
    {
        return BuildQuery(query, indexName, options: null);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a Lucene query string with per-request options.
    /// </summary>
    public Query BuildQuery(string query, ElasticsearchQueryOptions? options)
    {
        return BuildQuery(query, indexName: null, options);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a Lucene query string using registered index options merged with per-request options.
    /// </summary>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="indexName">The index name to look up registered options for (can be null).</param>
    /// <param name="options">Optional per-request options that override registered options.</param>
    /// <returns>The Elasticsearch Query DSL.</returns>
    public Query BuildQuery(string query, string? indexName, ElasticsearchQueryOptions? options)
    {
        var parseResult = LuceneQuery.Parse(query);

        if (!parseResult.IsSuccess)
        {
            var errors = string.Join("; ", parseResult.Errors.Select(e => e.Message));
            throw new QueryParseException($"Failed to parse query: {errors}");
        }

        return BuildQuery(parseResult.Document, indexName, options);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed query document.
    /// </summary>
    public Query BuildQuery(QueryDocument document)
    {
        return BuildQuery(document, indexName: null, options: null);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed query document using registered options for the specified index.
    /// </summary>
    /// <param name="document">The parsed query document.</param>
    /// <param name="indexName">The index name to look up registered options for.</param>
    /// <returns>The Elasticsearch Query DSL.</returns>
    public Query BuildQuery(QueryDocument document, string indexName)
    {
        return BuildQuery(document, indexName, options: null);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed query document with per-request options.
    /// </summary>
    public Query BuildQuery(QueryDocument document, ElasticsearchQueryOptions? options)
    {
        return BuildQuery(document, indexName: null, options);
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed query document using registered index options merged with per-request options.
    /// </summary>
    /// <param name="document">The parsed query document.</param>
    /// <param name="indexName">The index name to look up registered options for (can be null).</param>
    /// <param name="options">Optional per-request options that override registered options.</param>
    /// <returns>The Elasticsearch Query DSL.</returns>
    public Query BuildQuery(QueryDocument document, string? indexName, ElasticsearchQueryOptions? options)
    {
        // Get registered options for this index (if any)
        var registeredOptions = indexName is not null ? GetOptions(indexName) : null;

        // Create the visitor context, merging global config with registered and per-request options
        var context = CreateContext(registeredOptions, options);

        // Build visitor chain for this request
        QueryNode currentNode = document;

        // Determine which field resolver to use (per-request > registered > global)
        var fieldMap = options?.FieldMap ?? registeredOptions?.FieldMap;
        var fieldResolver = fieldMap is not null
            ? new FieldResolverQueryVisitor(fieldMap)
            : _fieldResolverVisitor;

        if (fieldResolver is not null)
        {
            currentNode = fieldResolver.Accept(currentNode, context);
        }

        // Determine which includes to use (per-request > registered > global)
        var includes = options?.Includes ?? registeredOptions?.Includes ?? _config.Includes;
        if (includes is not null)
        {
            context.SetIncludes(includes);
            var includeVisitor = (options?.Includes ?? registeredOptions?.Includes) is not null
                ? new IncludeVisitor(includes)
                : _includeVisitor;

            if (includeVisitor is not null)
            {
                currentNode = includeVisitor.Accept(currentNode, context);
            }
        }

        currentNode = _dateMathVisitor.Accept(currentNode, context);

        foreach (var visitor in _customVisitors)
        {
            currentNode = visitor.Accept(currentNode, context);
        }

        currentNode = _validationVisitor.Accept(currentNode, context);

        // Use the stateless singleton builder
        return ElasticsearchQueryBuilderVisitor.Instance.BuildQuery(currentNode, context);
    }

    private ElasticsearchQueryVisitorContext CreateContext(ElasticsearchQueryOptions? registeredOptions, ElasticsearchQueryOptions? options)
    {
        var context = new ElasticsearchQueryVisitorContext
        {
            // Merge: per-request > registered > global config
            UseScoring = options?.UseScoring ?? registeredOptions?.UseScoring ?? _config.UseScoring,
            DefaultFields = options?.DefaultFields ?? registeredOptions?.DefaultFields ?? _config.DefaultFields,
            DefaultOperator = _config.DefaultOperator,
            IsGeoPointField = options?.IsGeoPointField ?? registeredOptions?.IsGeoPointField ?? _config.IsGeoPointField,
            IsDateField = options?.IsDateField ?? registeredOptions?.IsDateField ?? _config.IsDateField,
            DefaultTimeZone = options?.DefaultTimeZone ?? registeredOptions?.DefaultTimeZone ?? _config.DefaultTimeZone,
            GeoLocationResolver = _config.GeoLocationResolver
        };

        // Set up validation options: per-request > registered > global
        var validationOptions = options?.ValidationOptions ?? registeredOptions?.ValidationOptions ?? _config.ValidationOptions;
        if (validationOptions is not null)
        {
            context.SetValidationOptions(validationOptions);
        }

        return context;
    }

    /// <summary>
    /// Adds a custom visitor to the visitor chain.
    /// </summary>
    public ElasticsearchQueryParser AddVisitor(QueryVisitor visitor)
    {
        _customVisitors.Add(visitor);
        return this;
    }

    /// <summary>
    /// Tries to build an Elasticsearch Query from a Lucene query string.
    /// Returns a result object instead of throwing exceptions.
    /// </summary>
    public QueryResult<Query> TryBuildQuery(string query)
    {
        return TryBuildQuery(query, indexName: null, options: null);
    }

    /// <summary>
    /// Tries to build an Elasticsearch Query from a Lucene query string using registered options for the specified index.
    /// Returns a result object instead of throwing exceptions.
    /// </summary>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="indexName">The index name to look up registered options for.</param>
    /// <returns>A QueryResult containing the query or error information.</returns>
    public QueryResult<Query> TryBuildQuery(string query, string indexName)
    {
        return TryBuildQuery(query, indexName, options: null);
    }

    /// <summary>
    /// Tries to build an Elasticsearch Query from a Lucene query string with per-request options.
    /// Returns a result object instead of throwing exceptions.
    /// </summary>
    public QueryResult<Query> TryBuildQuery(string query, ElasticsearchQueryOptions? options)
    {
        return TryBuildQuery(query, indexName: null, options);
    }

    /// <summary>
    /// Tries to build an Elasticsearch Query from a Lucene query string using registered index options merged with per-request options.
    /// Returns a result object instead of throwing exceptions.
    /// </summary>
    /// <param name="query">The Lucene query string.</param>
    /// <param name="indexName">The index name to look up registered options for (can be null).</param>
    /// <param name="options">Optional per-request options that override registered options.</param>
    /// <returns>A QueryResult containing the query or error information.</returns>
    public QueryResult<Query> TryBuildQuery(string query, string? indexName, ElasticsearchQueryOptions? options)
    {
        try
        {
            var parseResult = LuceneQuery.Parse(query);

            if (!parseResult.IsSuccess)
            {
                var errors = string.Join("; ", parseResult.Errors.Select(e => e.Message));
                return QueryResult<Query>.Failure(
                    new QueryParseException($"Failed to parse query: {errors}", QueryErrorCode.ParseError)
                    {
                        Errors = parseResult.Errors.ToList()
                    });
            }

            var result = BuildQuery(parseResult.Document, indexName, options);
            return QueryResult<Query>.Success(result);
        }
        catch (QueryException ex)
        {
            return QueryResult<Query>.Failure(ex);
        }
        catch (Exception ex)
        {
            return QueryResult<Query>.Failure(
                new QueryBuildException($"Failed to build query: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Validates a query string and returns the validation result.
    /// </summary>
    public QueryValidationResult Validate(string query, ElasticsearchQueryOptions? options = null)
    {
        return Validate(query, indexName: null, options);
    }

    /// <summary>
    /// Validates a query string using registered options for the specified index.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="indexName">The index name to look up registered options for.</param>
    /// <returns>The validation result.</returns>
    public QueryValidationResult Validate(string query, string indexName)
    {
        return Validate(query, indexName, options: null);
    }

    /// <summary>
    /// Validates a query string using registered index options merged with per-request options.
    /// </summary>
    /// <param name="query">The query string to validate.</param>
    /// <param name="indexName">The index name to look up registered options for (can be null).</param>
    /// <param name="options">Optional per-request options that override registered options.</param>
    /// <returns>The validation result.</returns>
    public QueryValidationResult Validate(string query, string? indexName, ElasticsearchQueryOptions? options)
    {
        var registeredOptions = indexName is not null ? GetOptions(indexName) : null;
        var parseResult = LuceneQuery.Parse(query);
        var context = CreateContext(registeredOptions, options);

        // Add parse errors as validation errors
        if (!parseResult.IsSuccess)
        {
            foreach (var error in parseResult.Errors)
            {
                context.AddValidationError(error.Message, error.Position);
            }
        }

        // Validate the document if it exists
        if (parseResult.Document is not null)
        {
            _validationVisitor.Accept(parseResult.Document, context);
            _validationVisitor.ApplyRestrictions(context);
        }

        return context.GetValidationResult();
    }
}
