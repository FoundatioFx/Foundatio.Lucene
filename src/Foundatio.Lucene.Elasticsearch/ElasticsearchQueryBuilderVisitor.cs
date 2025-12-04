using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Visitor that converts Lucene AST nodes into Elasticsearch Query DSL objects.
/// </summary>
public class ElasticsearchQueryBuilderVisitor : QueryNodeVisitor
{
    private readonly Stack<Query> _queryStack = new();
    private IElasticsearchQueryVisitorContext _context = null!;
    private string? _currentField;

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed Lucene query node.
    /// </summary>
    public Query BuildQuery(QueryNode node, IElasticsearchQueryVisitorContext? context = null)
    {
        return BuildQueryAsync(node, context).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed Lucene query node asynchronously.
    /// </summary>
    public async Task<Query> BuildQueryAsync(QueryNode node, IElasticsearchQueryVisitorContext? context = null)
    {
        _context = context ?? new ElasticsearchQueryVisitorContext();
        _queryStack.Clear();
        _currentField = null;

        await AcceptAsync(node, _context).ConfigureAwait(false);

        var query = _queryStack.Count > 0 ? _queryStack.Pop() : new MatchAllQuery();

        // Wrap in bool filter if not using scoring
        if (!_context.UseScoring)
        {
            query = new BoolQuery { Filter = [query] };
        }

        return query;
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();
        }
        else
        {
            _queryStack.Push(new MatchAllQuery());
        }
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(GroupNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();

            // Apply boost if specified
            if (node.Boost.HasValue && _queryStack.Count > 0)
            {
                var query = _queryStack.Pop();
                ApplyBoost(query, node.Boost.Value);
                _queryStack.Push(query);
            }
        }
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(BooleanQueryNode node, IQueryVisitorContext context)
    {
        if (node.Clauses.Count == 0)
        {
            _queryStack.Push(new MatchAllQuery());
            return Task.FromResult<QueryNode>(node);
        }

        var mustClauses = new List<Query>();
        var shouldClauses = new List<Query>();
        var mustNotClauses = new List<Query>();

        foreach (var clause in node.Clauses)
        {
            if (clause.Query is null)
                continue;

            // Check if the clause query is a BooleanQueryNode with a single Must/MustNot clause
            // (created from +/- prefix). If so, we need to extract it and apply the occur correctly.
            if (clause.Query is BooleanQueryNode innerBoolNode && innerBoolNode.Clauses.Count == 1)
            {
                var innerClause = innerBoolNode.Clauses[0];
                if (innerClause.Query is not null && innerClause.Occur != Occur.Should)
                {
                    // Visit the inner query
                    AcceptAsync(innerClause.Query, context).GetAwaiter().GetResult();

                    if (_queryStack.Count > 0)
                    {
                        var innerQuery = _queryStack.Pop();

                        // Apply the inner clause's occur (from +/-) to the outer structure
                        switch (innerClause.Occur)
                        {
                            case Occur.Must:
                                mustClauses.Add(innerQuery);
                                break;
                            case Occur.MustNot:
                                mustNotClauses.Add(innerQuery);
                                break;
                        }
                    }
                    continue;
                }
            }

            AcceptAsync(clause.Query, context).GetAwaiter().GetResult();

            if (_queryStack.Count == 0)
                continue;

            var clauseQuery = _queryStack.Pop();

            switch (clause.Occur)
            {
                case Occur.Must:
                    mustClauses.Add(clauseQuery);
                    break;
                case Occur.MustNot:
                    mustNotClauses.Add(clauseQuery);
                    break;
                case Occur.Should:
                    // Check operator to determine if this should be Must or Should
                    if (clause.Operator == BooleanOperator.And || _context.DefaultOperator == QueryOperator.And)
                        mustClauses.Add(clauseQuery);
                    else
                        shouldClauses.Add(clauseQuery);
                    break;
            }
        }

        var boolQuery = new BoolQuery();

        if (mustClauses.Count > 0)
            boolQuery.Must = mustClauses;
        if (shouldClauses.Count > 0)
            boolQuery.Should = shouldClauses;
        if (mustNotClauses.Count > 0)
            boolQuery.MustNot = mustNotClauses;

        // If we only have should clauses, set minimum_should_match to 1
        if (mustClauses.Count == 0 && mustNotClauses.Count == 0 && shouldClauses.Count > 0)
            boolQuery.MinimumShouldMatch = 1;

        _queryStack.Push(boolQuery);
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        var previousField = _currentField;
        _currentField = node.Field;

        if (node.IsExists)
        {
            // field:* syntax means exists
            _queryStack.Push(new ExistsQuery { Field = node.Field });
        }
        else if (node.Query is not null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();
        }

        _currentField = previousField;
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        Query query;
        var term = node.UnescapedTerm;
        var field = GetEffectiveField();

        // Handle match all
        if (term == "*" && field is null)
        {
            query = new MatchAllQuery();
        }
        else if (node.IsPrefix)
        {
            // Prefix query (term ends with *)
            var prefixValue = term.TrimEnd('*');
            if (field is not null)
            {
                query = new PrefixQuery((Field)field, prefixValue);
            }
            else if (_context.DefaultFields is { Length: > 0 })
            {
                // Use MultiMatchQuery with wildcard for prefix when no field
                query = new MultiMatchQuery(prefixValue + "*")
                {
                    Fields = Fields.FromStrings(_context.DefaultFields),
                    Type = TextQueryType.BestFields
                };
            }
            else
            {
                // No field and no defaults - use query_string style all-fields
                query = new QueryStringQuery(prefixValue + "*");
            }
        }
        else if (node.IsWildcard)
        {
            // Wildcard query (contains * or ?)
            if (field is not null)
            {
                query = new WildcardQuery((Field)field) { Value = term };
            }
            else if (_context.DefaultFields is { Length: > 0 })
            {
                query = new QueryStringQuery(term)
                {
                    Fields = Fields.FromStrings(_context.DefaultFields)
                };
            }
            else
            {
                query = new QueryStringQuery(term);
            }
        }
        else if (node.FuzzyDistance.HasValue)
        {
            // Fuzzy query - use Fuzziness property which accepts a string
            if (field is not null)
            {
                query = new FuzzyQuery((Field)field, term)
                {
                    Fuzziness = node.FuzzyDistance.Value.ToString()
                };
            }
            else if (_context.DefaultFields is { Length: > 0 })
            {
                query = new MultiMatchQuery(term)
                {
                    Fields = Fields.FromStrings(_context.DefaultFields),
                    Fuzziness = new Fuzziness(node.FuzzyDistance.Value.ToString())
                };
            }
            else
            {
                query = new QueryStringQuery($"{term}~{node.FuzzyDistance.Value}");
            }
        }
        else if (field is null && _context.DefaultFields is { Length: > 1 })
        {
            // Multi-match query when no field specified and multiple default fields
            query = new MultiMatchQuery(term)
            {
                Fields = Fields.FromStrings(_context.DefaultFields)
            };
        }
        else if (field is null)
        {
            // No field and no or single default field - use MultiMatchQuery
            if (_context.DefaultFields is { Length: 1 })
            {
                query = _context.UseScoring
                    ? new MatchQuery((Field)_context.DefaultFields[0], term)
                    : (Query)new TermQuery((Field)_context.DefaultFields[0], (FieldValue)term);
            }
            else
            {
                // No default fields - use MultiMatchQuery with no fields specified
                // which searches all searchable fields
                query = new MultiMatchQuery(term);
            }
        }
        else
        {
            // Simple term or match query with explicit field
            if (_context.UseScoring)
            {
                query = new MatchQuery((Field)field, term);
            }
            else
            {
                query = new TermQuery((Field)field, (FieldValue)term);
            }
        }

        ApplyBoost(query, node.Boost);
        _queryStack.Push(query);
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context)
    {
        Query query;
        var phrase = node.Phrase;
        var field = GetEffectiveField();

        if (field is null && _context.DefaultFields is { Length: > 1 })
        {
            // Multi-match phrase query
            query = new MultiMatchQuery(phrase)
            {
                Type = TextQueryType.Phrase,
                Fields = Fields.FromStrings(_context.DefaultFields),
                Slop = node.Slop
            };
        }
        else if (field is null && _context.DefaultFields is { Length: 1 })
        {
            query = new MatchPhraseQuery((Field)_context.DefaultFields[0], phrase)
            {
                Slop = node.Slop
            };
        }
        else if (field is null)
        {
            // No default fields - use MultiMatchQuery with phrase type
            query = new MultiMatchQuery(phrase)
            {
                Type = TextQueryType.Phrase,
                Slop = node.Slop
            };
        }
        else
        {
            query = new MatchPhraseQuery((Field)field, phrase)
            {
                Slop = node.Slop
            };
        }

        ApplyBoost(query, node.Boost);
        _queryStack.Push(query);
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(RegexNode node, IQueryVisitorContext context)
    {
        var field = GetEffectiveField();

        Query query;
        if (field is null && _context.DefaultFields is { Length: >= 1 })
        {
            // Use first default field for regex
            query = new RegexpQuery((Field)_context.DefaultFields[0], node.Pattern);
        }
        else if (field is null)
        {
            // No field - use query_string with regex
            query = new QueryStringQuery($"/{node.Pattern}/");
        }
        else
        {
            query = new RegexpQuery((Field)field, node.Pattern);
        }

        ApplyBoost(query, node.Boost);
        _queryStack.Push(query);
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
    {
        var field = _currentField ?? node.Field ?? throw new InvalidOperationException("Range query requires a field");

        // Check if this is a date range query
        if (IsDateField(field))
        {
            var dateQuery = BuildDateRangeQuery(field, node);
            ApplyBoost(dateQuery, node.Boost);
            _queryStack.Push(dateQuery);
            return Task.FromResult<QueryNode>(node);
        }

        // Try to determine if this is a numeric or term range
        var isNumeric = (node.Min is not null && double.TryParse(node.Min, out _)) ||
                       (node.Max is not null && double.TryParse(node.Max, out _));

        Query rangeQuery;
        if (isNumeric)
        {
            rangeQuery = BuildNumberRangeQuery(field, node);
        }
        else
        {
            rangeQuery = BuildTermRangeQuery(field, node);
        }

        ApplyBoost(rangeQuery, node.Boost);
        _queryStack.Push(rangeQuery);
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(NotNode node, IQueryVisitorContext context)
    {
        if (node.Query is not null)
        {
            AcceptAsync(node.Query, context).GetAwaiter().GetResult();

            if (_queryStack.Count > 0)
            {
                var innerQuery = _queryStack.Pop();
                var boolQuery = new BoolQuery { MustNot = [innerQuery] };
                _queryStack.Push(boolQuery);
            }
        }
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(ExistsNode node, IQueryVisitorContext context)
    {
        _queryStack.Push(new ExistsQuery { Field = node.Field });
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(MissingNode node, IQueryVisitorContext context)
    {
        // Missing is implemented as bool must_not exists
        var boolQuery = new BoolQuery
        {
            MustNot = [new ExistsQuery { Field = node.Field }]
        };
        _queryStack.Push(boolQuery);
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(MatchAllNode node, IQueryVisitorContext context)
    {
        _queryStack.Push(new MatchAllQuery());
        return Task.FromResult<QueryNode>(node);
    }

    /// <inheritdoc />
    public override Task<QueryNode> VisitAsync(MultiTermNode node, IQueryVisitorContext context)
    {
        // MultiTermNode is typically for OR'd terms without explicit operator
        // Build a bool should query
        var shouldClauses = new List<Query>();
        var field = GetEffectiveField();

        foreach (var term in node.Terms)
        {
            Query termQuery;
            if (field is not null)
            {
                if (_context.UseScoring)
                {
                    termQuery = new MatchQuery((Field)field, term);
                }
                else
                {
                    termQuery = new TermQuery((Field)field, (FieldValue)term);
                }
            }
            else if (_context.DefaultFields is { Length: > 0 })
            {
                termQuery = new MultiMatchQuery(term)
                {
                    Fields = Fields.FromStrings(_context.DefaultFields)
                };
            }
            else
            {
                termQuery = new MultiMatchQuery(term);
            }
            shouldClauses.Add(termQuery);
        }

        if (shouldClauses.Count == 1)
        {
            _queryStack.Push(shouldClauses[0]);
        }
        else if (shouldClauses.Count > 1)
        {
            var boolQuery = new BoolQuery
            {
                Should = shouldClauses,
                MinimumShouldMatch = 1
            };
            _queryStack.Push(boolQuery);
        }

        return Task.FromResult<QueryNode>(node);
    }

    private string? GetEffectiveField()
    {
        if (_currentField is not null)
            return _currentField;

        if (_context.DefaultFields is { Length: 1 })
            return _context.DefaultFields[0];

        return null;
    }

    private bool IsDateField(string field)
    {
        return _context.IsDateField?.Invoke(field) ?? false;
    }

    private Query BuildDateRangeQuery(string field, RangeNode node)
    {
        var dateRange = new DateRangeQuery((Field)field);

        if (!string.IsNullOrEmpty(node.Min) && node.Min != "*")
        {
            if (node.MinInclusive)
                dateRange.Gte = node.Min;
            else
                dateRange.Gt = node.Min;
        }

        if (!string.IsNullOrEmpty(node.Max) && node.Max != "*")
        {
            if (node.MaxInclusive)
                dateRange.Lte = node.Max;
            else
                dateRange.Lt = node.Max;
        }

        if (_context.DefaultTimeZone is not null)
            dateRange.TimeZone = _context.DefaultTimeZone;

        return dateRange;
    }

    private Query BuildNumberRangeQuery(string field, RangeNode node)
    {
        var numberRange = new NumberRangeQuery((Field)field);

        if (!string.IsNullOrEmpty(node.Min) && node.Min != "*")
        {
            if (double.TryParse(node.Min, out var minValue))
            {
                if (node.MinInclusive)
                    numberRange.Gte = minValue;
                else
                    numberRange.Gt = minValue;
            }
        }

        if (!string.IsNullOrEmpty(node.Max) && node.Max != "*")
        {
            if (double.TryParse(node.Max, out var maxValue))
            {
                if (node.MaxInclusive)
                    numberRange.Lte = maxValue;
                else
                    numberRange.Lt = maxValue;
            }
        }

        return numberRange;
    }

    private static Query BuildTermRangeQuery(string field, RangeNode node)
    {
        var termRange = new TermRangeQuery((Field)field);

        if (!string.IsNullOrEmpty(node.Min) && node.Min != "*")
        {
            if (node.MinInclusive)
                termRange.Gte = node.Min;
            else
                termRange.Gt = node.Min;
        }

        if (!string.IsNullOrEmpty(node.Max) && node.Max != "*")
        {
            if (node.MaxInclusive)
                termRange.Lte = node.Max;
            else
                termRange.Lt = node.Max;
        }

        return termRange;
    }

    private static void ApplyBoost(Query query, float? boost)
    {
        if (!boost.HasValue)
            return;

        // Use the query's variant to access the actual query type and set boost
        if (query.Term is not null)
            query.Term.Boost = boost.Value;
        else if (query.Match is not null)
            query.Match.Boost = boost.Value;
        else if (query.MatchPhrase is not null)
            query.MatchPhrase.Boost = boost.Value;
        else if (query.Prefix is not null)
            query.Prefix.Boost = boost.Value;
        else if (query.Wildcard is not null)
            query.Wildcard.Boost = boost.Value;
        else if (query.Fuzzy is not null)
            query.Fuzzy.Boost = boost.Value;
        else if (query.Regexp is not null)
            query.Regexp.Boost = boost.Value;
        else if (query.Bool is not null)
            query.Bool.Boost = boost.Value;
        else if (query.MultiMatch is not null)
            query.MultiMatch.Boost = boost.Value;
    }
}
