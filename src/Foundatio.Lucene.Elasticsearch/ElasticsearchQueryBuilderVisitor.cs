using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Stateless visitor that converts Lucene AST nodes into Elasticsearch Query DSL objects.
/// All build state is stored in the context, making this visitor safe to use as a singleton.
/// </summary>
public class ElasticsearchQueryBuilderVisitor : QueryVisitor<IElasticsearchQueryVisitorContext>
{
    /// <summary>
    /// Singleton instance for reuse. Since the visitor is stateless, a single instance can be shared.
    /// </summary>
    public static ElasticsearchQueryBuilderVisitor Instance { get; } = new();

    /// <summary>
    /// Builds an Elasticsearch Query from a parsed Lucene query node.
    /// </summary>
    public Query BuildQuery(QueryNode node, IElasticsearchQueryVisitorContext? context = null)
    {
        context ??= new ElasticsearchQueryVisitorContext();
        context.QueryStack.Clear();
        context.CurrentField = null;

        Accept(node, context);

        var query = context.QueryStack.Count > 0 ? context.QueryStack.Pop() : new MatchAllQuery();

        // Wrap in bool filter if not using scoring
        if (!context.UseScoring)
        {
            query = new BoolQuery { Filter = [query] };
        }

        return query;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(QueryDocument node, IElasticsearchQueryVisitorContext context)
    {
        if (node.Query is not null)
        {
            Accept(node.Query, context);
        }
        else
        {
            context.QueryStack.Push(new MatchAllQuery());
        }
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(GroupNode node, IElasticsearchQueryVisitorContext context)
    {
        if (node.Query is not null)
        {
            Accept(node.Query, context);

            // Apply boost if specified
            if (node.Boost.HasValue && context.QueryStack.Count > 0)
            {
                var query = context.QueryStack.Pop();
                ApplyBoost(query, node.Boost.Value);
                context.QueryStack.Push(query);
            }
        }
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(BooleanQueryNode node, IElasticsearchQueryVisitorContext context)
    {
        if (node.Clauses.Count == 0)
        {
            context.QueryStack.Push(new MatchAllQuery());
            return node;
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
                    Accept(innerClause.Query, context);

                    if (context.QueryStack.Count > 0)
                    {
                        var innerQuery = context.QueryStack.Pop();

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

            Accept(clause.Query, context);

            if (context.QueryStack.Count == 0)
                continue;

            var clauseQuery = context.QueryStack.Pop();

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
                    if (clause.Operator == BooleanOperator.And || context.DefaultOperator == QueryOperator.And)
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

        context.QueryStack.Push(boolQuery);
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(FieldQueryNode node, IElasticsearchQueryVisitorContext context)
    {
        var previousField = context.CurrentField;
        context.CurrentField = node.Field;

        if (node.IsExists)
        {
            // field:* syntax means exists
            context.QueryStack.Push(new ExistsQuery { Field = node.Field });
        }
        else if (node.Query is not null)
        {
            Accept(node.Query, context);
        }

        context.CurrentField = previousField;
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(TermNode node, IElasticsearchQueryVisitorContext context)
    {
        Query query;
        var term = node.UnescapedTerm;
        var field = GetEffectiveField(context);

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
            else if (context.DefaultFields is { Length: > 0 })
            {
                // Use MultiMatchQuery with wildcard for prefix when no field
                query = new MultiMatchQuery(prefixValue + "*")
                {
                    Fields = Fields.FromStrings(context.DefaultFields),
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
            else if (context.DefaultFields is { Length: > 0 })
            {
                query = new QueryStringQuery(term)
                {
                    Fields = Fields.FromStrings(context.DefaultFields)
                };
            }
            else
            {
                query = new QueryStringQuery(term);
            }
        }
        else if (node.FuzzyDistance.HasValue)
        {
            var fuzziness = node.FuzzyDistance.Value == TermNode.DefaultFuzzyDistance
                ? "AUTO"
                : node.FuzzyDistance.Value.ToString();

            if (field is not null)
            {
                query = new FuzzyQuery((Field)field, term)
                {
                    Fuzziness = fuzziness
                };
            }
            else if (context.DefaultFields is { Length: > 0 })
            {
                query = new MultiMatchQuery(term)
                {
                    Fields = Fields.FromStrings(context.DefaultFields),
                    Fuzziness = new Fuzziness(fuzziness)
                };
            }
            else
            {
                var fuzzyString = node.FuzzyDistance.Value == TermNode.DefaultFuzzyDistance
                    ? $"{term}~"
                    : $"{term}~{node.FuzzyDistance.Value}";
                query = new QueryStringQuery(fuzzyString);
            }
        }
        else if (field is null && context.DefaultFields is { Length: > 1 })
        {
            // Multi-match query when no field specified and multiple default fields
            query = new MultiMatchQuery(term)
            {
                Fields = Fields.FromStrings(context.DefaultFields)
            };
        }
        else if (field is null)
        {
            // No field and no or single default field - use MultiMatchQuery
            if (context.DefaultFields is { Length: 1 })
            {
                query = context.UseScoring
                    ? new MatchQuery((Field)context.DefaultFields[0], term)
                    : (Query)new TermQuery((Field)context.DefaultFields[0], (FieldValue)term);
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
            if (context.UseScoring)
            {
                query = new MatchQuery((Field)field, term);
            }
            else
            {
                query = new TermQuery((Field)field, (FieldValue)term);
            }
        }

        ApplyBoost(query, node.Boost);
        context.QueryStack.Push(query);
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(PhraseNode node, IElasticsearchQueryVisitorContext context)
    {
        Query query;
        var phrase = node.Phrase;
        var field = GetEffectiveField(context);

        if (field is null && context.DefaultFields is { Length: > 1 })
        {
            // Multi-match phrase query
            query = new MultiMatchQuery(phrase)
            {
                Type = TextQueryType.Phrase,
                Fields = Fields.FromStrings(context.DefaultFields),
                Slop = node.Slop
            };
        }
        else if (field is null && context.DefaultFields is { Length: 1 })
        {
            query = new MatchPhraseQuery((Field)context.DefaultFields[0], phrase)
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
        context.QueryStack.Push(query);
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(RegexNode node, IElasticsearchQueryVisitorContext context)
    {
        var field = GetEffectiveField(context);

        Query query;
        if (field is null && context.DefaultFields is { Length: >= 1 })
        {
            // Use first default field for regex
            query = new RegexpQuery((Field)context.DefaultFields[0], node.Pattern);
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
        context.QueryStack.Push(query);
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(RangeNode node, IElasticsearchQueryVisitorContext context)
    {
        var field = context.CurrentField ?? node.Field ?? throw new InvalidOperationException("Range query requires a field");

        // Check if this is a date range query
        if (IsDateField(context, field))
        {
            var dateQuery = BuildDateRangeQuery(context, field, node);
            ApplyBoost(dateQuery, node.Boost);
            context.QueryStack.Push(dateQuery);
            return node;
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
        context.QueryStack.Push(rangeQuery);
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(NotNode node, IElasticsearchQueryVisitorContext context)
    {
        if (node.Query is not null)
        {
            Accept(node.Query, context);

            if (context.QueryStack.Count > 0)
            {
                var innerQuery = context.QueryStack.Pop();
                var boolQuery = new BoolQuery { MustNot = [innerQuery] };
                context.QueryStack.Push(boolQuery);
            }
        }
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(ExistsNode node, IElasticsearchQueryVisitorContext context)
    {
        context.QueryStack.Push(new ExistsQuery { Field = node.Field });
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(MissingNode node, IElasticsearchQueryVisitorContext context)
    {
        // Missing is implemented as bool must_not exists
        var boolQuery = new BoolQuery
        {
            MustNot = [new ExistsQuery { Field = node.Field }]
        };
        context.QueryStack.Push(boolQuery);
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(MatchAllNode node, IElasticsearchQueryVisitorContext context)
    {
        context.QueryStack.Push(new MatchAllQuery());
        return node;
    }

    /// <inheritdoc />
    protected override QueryNode Visit(MultiTermNode node, IElasticsearchQueryVisitorContext context)
    {
        // MultiTermNode is typically for OR'd terms without explicit operator
        // Build a bool should query
        var shouldClauses = new List<Query>();
        var field = GetEffectiveField(context);

        foreach (var term in node.Terms)
        {
            Query termQuery;
            if (field is not null)
            {
                if (context.UseScoring)
                {
                    termQuery = new MatchQuery((Field)field, term);
                }
                else
                {
                    termQuery = new TermQuery((Field)field, (FieldValue)term);
                }
            }
            else if (context.DefaultFields is { Length: > 0 })
            {
                termQuery = new MultiMatchQuery(term)
                {
                    Fields = Fields.FromStrings(context.DefaultFields)
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
            context.QueryStack.Push(shouldClauses[0]);
        }
        else if (shouldClauses.Count > 1)
        {
            var boolQuery = new BoolQuery
            {
                Should = shouldClauses,
                MinimumShouldMatch = 1
            };
            context.QueryStack.Push(boolQuery);
        }

        return node;
    }

    private static string? GetEffectiveField(IElasticsearchQueryVisitorContext ctx)
    {
        if (ctx.CurrentField is not null)
            return ctx.CurrentField;

        if (ctx.DefaultFields is { Length: 1 })
            return ctx.DefaultFields[0];

        return null;
    }

    private static bool IsDateField(IElasticsearchQueryVisitorContext ctx, string field)
    {
        return ctx.IsDateField?.Invoke(field) ?? false;
    }

    private static Query BuildDateRangeQuery(IElasticsearchQueryVisitorContext ctx, string field, RangeNode node)
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

        if (ctx.DefaultTimeZone is not null)
            dateRange.TimeZone = ctx.DefaultTimeZone;

        return dateRange;
    }

    private static Query BuildNumberRangeQuery(string field, RangeNode node)
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
