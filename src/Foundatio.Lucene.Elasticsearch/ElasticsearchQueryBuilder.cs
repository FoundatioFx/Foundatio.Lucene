using System.Globalization;
using System.Text;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Mapping;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Converts a processed query tree into Elasticsearch Query DSL. Stateless; all per-query state is in the context.
/// </summary>
internal static class ElasticsearchQueryBuilder
{
    private const string QueryStringSpecialCharacters = "+-=!(){}[]^\"~*?:\\/|&<>";

    public static Query Build(QueryDocument document, ElasticsearchQueryVisitorContext context)
    {
        var builder = new Builder(context);
        var query = document.Query is null ? null : builder.Build(document.Query, scopeField: null).Query;
        query ??= new MatchAllQuery();

        context.ValidationResult.ThrowIfInvalid();

        if (!context.UseScoring && !IsFilterOnlyBool(query))
            query = new BoolQuery { Filter = [query] };

        return query;
    }

    internal static bool IsFilterOnlyBool(Query query)
    {
        return query.Bool is { } boolQuery
            && boolQuery.Filter is { Count: > 0 }
            && boolQuery.Must is null or { Count: 0 }
            && boolQuery.Should is null or { Count: 0 }
            && boolQuery.MustNot is null or { Count: 0 };
    }

    /// <summary>
    /// A built query. <see cref="NestedPath"/> is set when the query is a nested query generated for a single nested
    /// field, so it can be merged with sibling clauses on the same nested document.
    /// </summary>
    private readonly record struct Part(Query? Query, string? NestedPath = null, bool IsNestedMergeable = false)
    {
        public static Part None => default;
    }

    private sealed class Builder(ElasticsearchQueryVisitorContext context)
    {
        private readonly QueryValidationResult _result = context.ValidationResult;
        private string? _currentNestedPath;

        public Part Build(QueryNode node, string? scopeField)
        {
            if (node.GetQuery() is { } custom)
                return WrapNested(node, custom, scopeField);

            return node switch
            {
                GroupNode group => BuildGroup(group, scopeField),
                BooleanQueryNode boolean => BuildBoolean(boolean, scopeField),
                NotNode not => BuildNot(not, scopeField),
                FieldQueryNode field => BuildField(field),
                TermNode term => BuildTerm(term, scopeField),
                PhraseNode phrase => BuildPhrase(phrase, scopeField),
                RegexNode regex => BuildRegex(regex, scopeField),
                RangeNode range => BuildRange(range, scopeField),
                ExistsNode exists => BuildExists(exists.Field, missing: false, exists),
                MissingNode missing => BuildExists(missing.Field, missing: true, missing),
                MatchAllNode => new Part(new MatchAllQuery()),
                _ => Error($"Unsupported query node type '{node.GetType().Name}'.", node)
            };
        }

        private Part WrapNested(QueryNode node, Query query, string? scopeField)
        {
            string? field = node switch
            {
                IFieldNode fieldNode => fieldNode.Field,
                _ => scopeField
            };

            return field is null ? new Part(query) : WrapNested(field, query);
        }

        private Part BuildGroup(GroupNode group, string? scopeField)
        {
            var inner = group.Query is null ? Part.None : Build(group.Query, scopeField);
            if (group.BoostText is null || inner.Query is null)
                return inner;

            if (!TryReadBoost(group.BoostText, group, out float? boost))
                return Part.None;

            if (inner.Query.Nested is { } nested && inner.IsNestedMergeable)
            {
                nested.Query = new BoolQuery { Must = [nested.Query], Boost = boost };
                return new Part(inner.Query);
            }

            return new Part(new BoolQuery { Must = [inner.Query], Boost = boost });
        }

        private Part BuildNot(NotNode not, string? scopeField)
        {
            var inner = not.Query is null ? Part.None : Build(not.Query, scopeField);
            return inner.Query is null ? Part.None : new Part(new BoolQuery { MustNot = [inner.Query] });
        }

        private Part BuildBoolean(BooleanQueryNode node, string? scopeField)
        {
            List<Part>? musts = null, shoulds = null, mustNots = null;
            foreach (var clause in node.Clauses)
            {
                if (clause.Query is null)
                    continue;

                var part = Build(clause.Query, scopeField);
                if (part.Query is null)
                {
                    if (clause.Occur == Occur.Must)
                        _result.AddError($"A required clause did not produce a query: {QueryStringBuilder.ToQueryString(clause.Query)}", clause.Query.StartPosition);
                    continue;
                }

                switch (clause.Occur)
                {
                    case Occur.Must:
                        (musts ??= []).Add(part);
                        break;
                    case Occur.MustNot:
                        (mustNots ??= []).Add(part);
                        break;
                    default:
                        (shoulds ??= []).Add(part);
                        break;
                }
            }

            if (musts is not null)
                musts = MergeNested(musts, Occur.Must);
            if (shoulds is not null)
                shoulds = MergeNested(shoulds, Occur.Should);

            int mustCount = musts?.Count ?? 0, shouldCount = shoulds?.Count ?? 0, mustNotCount = mustNots?.Count ?? 0;
            if (mustCount == 1 && shouldCount == 0 && mustNotCount == 0)
                return musts![0];
            if (shouldCount == 1 && mustCount == 0 && mustNotCount == 0)
                return shoulds![0];
            if (mustCount + shouldCount + mustNotCount == 0)
                return Part.None;

            var boolQuery = new BoolQuery();
            if (mustCount > 0)
            {
                var queries = musts!.Select(p => p.Query!).ToList();
                if (context.UseScoring)
                    boolQuery.Must = queries;
                else
                    boolQuery.Filter = queries;
            }

            // Optional clauses only affect scoring when there are required clauses.
            if (shouldCount > 0 && (mustCount == 0 || context.UseScoring))
            {
                boolQuery.Should = shoulds!.Select(p => p.Query!).ToList();
                if (mustCount == 0)
                    boolQuery.MinimumShouldMatch = 1;
            }

            if (mustNotCount > 0)
                boolQuery.MustNot = mustNots!.Select(p => p.Query!).ToList();

            return new Part(boolQuery);
        }

        /// <summary>
        /// Combines clauses on the same nested path into a single nested query so they must match the same nested
        /// document. Clauses on a deeper nested path are folded into the query of an ancestor path.
        /// </summary>
        private List<Part> MergeNested(List<Part> parts, Occur occur)
        {
            if (parts.Count < 2 || parts.Count(p => p.IsNestedMergeable) < 2)
                return parts;

            var groups = new Dictionary<string, List<Query>>(StringComparer.Ordinal);
            foreach (var part in parts.Where(p => p.IsNestedMergeable))
            {
                string path = part.NestedPath!;
                if (!groups.TryGetValue(path, out var inner))
                    groups[path] = inner = [];
                inner.Add(part.Query!.Nested!.Query);
            }

            AddCommonNestedAncestors(groups);

            // Fold deeper paths into the nearest ancestor path present in this clause set.
            foreach (string path in groups.Keys.OrderByDescending(p => p.Length).ToList())
            {
                string? ancestor = groups.Keys.Where(p => p.Length < path.Length && path.StartsWith(p + ".", StringComparison.Ordinal))
                    .OrderByDescending(p => p.Length)
                    .FirstOrDefault();
                if (ancestor is null)
                    continue;

                var childQueries = groups[path];
                groups.Remove(path);
                groups[ancestor].Add(new NestedQuery(path, Combine(childQueries, occur)));
            }

            var merged = new List<Part>(parts.Count);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                if (!part.IsNestedMergeable)
                {
                    merged.Add(part);
                    continue;
                }

                string path = part.NestedPath!;
                string? root = groups.ContainsKey(path) ? path : groups.Keys.First(p => path.StartsWith(p + ".", StringComparison.Ordinal));
                if (emitted.Add(root))
                    merged.Add(new Part(new NestedQuery(root, Combine(groups[root], occur)), root, IsNestedMergeable: true));
            }

            return merged;
        }

        /// <summary>
        /// Adds the deepest nested path shared by clauses on sibling nested paths (for example <c>parent</c> for
        /// <c>parent.a</c> and <c>parent.b</c>) so they are folded into it and must match the same ancestor document.
        /// </summary>
        private void AddCommonNestedAncestors(Dictionary<string, List<Query>> groups)
        {
            var paths = groups.Keys.ToList();
            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = i + 1; j < paths.Count; j++)
                {
                    if (GetCommonNestedAncestor(paths[i], paths[j]) is { } ancestor)
                        groups.TryAdd(ancestor, []);
                }
            }
        }

        private string? GetCommonNestedAncestor(string first, string second)
        {
            for (int dot = first.LastIndexOf('.'); dot > 0; dot = first.LastIndexOf('.', dot - 1))
            {
                string prefix = first[..dot];
                if (second.StartsWith(prefix + ".", StringComparison.Ordinal) && GetMapping(prefix) is { Property: NestedProperty })
                    return prefix;
            }

            return null;
        }

        private Query Combine(List<Query> queries, Occur occur)
        {
            if (queries.Count == 1)
                return queries[0];

            if (occur == Occur.Should)
                return new BoolQuery { Should = queries, MinimumShouldMatch = 1 };

            return context.UseScoring ? new BoolQuery { Must = queries } : new BoolQuery { Filter = queries };
        }

        private Part BuildField(FieldQueryNode node)
        {
            if (node.Query is null)
                return Part.None;

            string field = node.Field;
            if (field.Contains('^'))
                return Error($"Field names cannot contain '^': {field}", node);

            if (context.UseNested && GetMapping(field) is { Property: NestedProperty } mapping)
            {
                string path = mapping.FullPath;
                string? previous = _currentNestedPath;
                _currentNestedPath = path;
                try
                {
                    var inner = Build(node.Query, field);
                    if (inner.Query is null)
                        return Part.None;

                    var nestedFilter = context.GetNestedFilter(path, field);
                    return new Part(new NestedQuery(path, ApplyFilter(inner.Query, nestedFilter)));
                }
                finally
                {
                    _currentNestedPath = previous;
                }
            }

            return Build(node.Query, field);
        }

        private Part BuildTerm(TermNode node, string? field)
        {
            if (field is not null && IsGeoField(field))
                return BuildGeoDistance(node.UnescapedTerm, node.ProximityText, node.BoostText, field, node);

            if (!TryCreateTerm(node, out var term))
                return new Part(new MatchNoneQuery());

            if (field is not null)
                return WrapNested(field, BuildSingleFieldTermQuery(term, field, node));

            return BuildDefaultFieldsQuery(term, node);
        }

        private Part BuildPhrase(PhraseNode node, string? field)
        {
            if (field is not null && IsGeoField(field))
                return BuildGeoDistance(node.Phrase, node.ProximityText, node.BoostText, field, node);

            if (!TryReadBoost(node.BoostText, node, out float? boost))
                return new Part(new MatchNoneQuery());

            int? slop = null;
            if (node.ProximityText is { } proximity)
            {
                if (proximity.Length == 0)
                {
                    slop = 0;
                }
                else if (int.TryParse(proximity, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) && value >= 0)
                {
                    slop = value;
                }
                else
                {
                    _result.AddError($"Phrase slop must be a non-negative integer: {proximity}", node.StartPosition);
                    return new Part(new MatchNoneQuery());
                }
            }

            var term = new QueryTerm { Value = node.Phrase, Quoted = true, Slop = slop, Boost = boost };
            if (field is not null)
                return WrapNested(field, BuildSingleFieldTermQuery(term, field, node));

            return BuildDefaultFieldsQuery(term, node);
        }

        private Part BuildRegex(RegexNode node, string? field)
        {
            if (!TryReadBoost(node.BoostText, node, out float? boost))
                return new Part(new MatchNoneQuery());

            var term = new QueryTerm { Value = node.Pattern, Regex = node.Pattern, Expression = $"/{node.Pattern}/", Boost = boost };
            if (field is not null)
                return WrapNested(field, BuildSingleFieldTermQuery(term, field, node));

            return BuildDefaultFieldsQuery(term, node);
        }

        private Part BuildRange(RangeNode node, string? field)
        {
            if (node.ProximityText is not null)
                return Error("'~' is not supported on range queries.", node);

            if (field is not null)
                return BuildSingleFieldRange(node, field);

            if (context.DefaultFields is not { Length: > 0 } defaultFields)
                return Error("Range queries require a field.", node);

            var queries = new List<Query>();
            foreach (string defaultField in defaultFields)
            {
                if (BuildSingleFieldRange(node, ResolveDefaultField(defaultField)).Query is { } query)
                    queries.Add(query);
            }

            return queries.Count switch
            {
                0 => Part.None,
                1 => new Part(queries[0]),
                _ => new Part(new BoolQuery { Should = queries, MinimumShouldMatch = 1 })
            };
        }

        private Part BuildSingleFieldRange(RangeNode node, string field)
        {
            var mapping = GetMapping(field);
            var fieldType = GetFieldType(field, mapping);

            if (fieldType == FieldType.GeoPoint)
            {
                if (node.Min is null || node.Max is null)
                    return Error($"Geo bounding box queries on '{field}' require both corners.", node);

                var box = GeoBounds.TopLeftBottomRight(new TopLeftBottomRightGeoBounds
                {
                    TopLeft = GeoLocation.Text(node.Min),
                    BottomRight = GeoLocation.Text(node.Max)
                });
                return WrapNested(field, new GeoBoundingBoxQuery(box, field));
            }

            if (fieldType is FieldType.Date or FieldType.DateNanos)
            {
                string? timeZone = node.BoostText is null ? context.DefaultTimeZone : ElasticsearchTimeZone.Normalize(node.BoostText);
                if (node.BoostText is not null && float.TryParse(node.BoostText, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return Error($"The '^' modifier on a date range is its time zone (for example ^\"America/Chicago\" or ^-5h), not a boost: {node.BoostText}", node);

                var dateRange = new DateRangeQuery(field) { TimeZone = timeZone };
                if (node.Min is { } min)
                {
                    if (node.MinInclusive)
                        dateRange.Gte = min;
                    else
                        dateRange.Gt = min;
                }

                if (node.Max is { } max)
                {
                    if (node.MaxInclusive)
                        dateRange.Lte = max;
                    else
                        dateRange.Lt = max;
                }

                return WrapNested(field, dateRange);
            }

            if (!TryReadBoost(node.BoostText, node, out float? boost))
                return new Part(new MatchNoneQuery());

            foreach (string? bound in (string?[])[node.Min, node.Max])
            {
                if (bound is not null && !IsValidValue(bound, fieldType))
                    return Error($"'{bound}' is not a valid range bound for field '{field}' of type '{fieldType}'.", node);
            }

            var range = new TermRangeQuery(field) { Boost = boost };
            if (node.Min is { } lower)
            {
                if (node.MinInclusive)
                    range.Gte = lower;
                else
                    range.Gt = lower;
            }

            if (node.Max is { } upper)
            {
                if (node.MaxInclusive)
                    range.Lte = upper;
                else
                    range.Lt = upper;
            }

            return WrapNested(field, range);
        }

        private Part BuildExists(string field, bool missing, QueryNode node)
        {
            if (field.Length == 0)
                return Error("Exists and missing queries require a field.", node);

            if (field.Contains('^'))
                return Error($"Field names cannot contain '^': {field}", node);

            string resolved = ResolveDefaultField(field);
            Query query = new ExistsQuery(resolved);
            var part = WrapNested(resolved, query);
            if (!missing)
                return part;

            return new Part(new BoolQuery { MustNot = [part.Query!] });
        }

        private Part WrapNested(string field, Query query)
        {
            if (!context.UseNested)
                return new Part(query);

            var mapping = GetMapping(field);
            string? path = mapping?.NestedPath;
            if (path is null || string.Equals(path, _currentNestedPath, StringComparison.Ordinal))
                return new Part(query);

            // Inside an explicit nested group, a deeper nested field still needs its own nested query.
            var filter = context.GetNestedFilter(path, field);
            return new Part(new NestedQuery(path, ApplyFilter(query, filter)), path, IsNestedMergeable: true);
        }

        private static Query ApplyFilter(Query query, Query? filter)
        {
            if (filter is null)
                return query;

            return new BoolQuery { Must = [query], Filter = [filter] };
        }

        private Query BuildSingleFieldTermQuery(QueryTerm term, string field, QueryNode node)
        {
            var mapping = GetMapping(field);
            var fieldType = GetFieldType(field, mapping);

            if ((term.Regex is not null || term.Fuzziness is not null || term.Slop is not null) && IsScalar(fieldType))
            {
                _result.AddError($"Regex, fuzzy, and phrase-proximity queries are not supported on field '{field}' of type '{fieldType}'.", node.StartPosition);
                return new MatchNoneQuery();
            }

            if (term.Regex is not null)
                return new RegexpQuery(field, term.Regex) { Boost = term.Boost };

            bool analyzed = IsAnalyzed(field, mapping);
            if (term.Wildcard is not null)
            {
                if (analyzed)
                    return ToQueryString(term, [field]);

                if (term.IsPrefix)
                    return new PrefixQuery(field, term.PrefixValue!) { Boost = term.Boost };

                return new WildcardQuery(field) { Value = term.Wildcard, Boost = term.Boost };
            }

            if (term.Quoted && (analyzed || term.Slop is not null))
                return new MatchPhraseQuery(field, term.Value) { Slop = term.Slop, Boost = term.Boost };

            if (analyzed)
                return new MatchQuery(field, term.Value) { Fuzziness = term.Fuzziness, Boost = term.Boost };

            if (term.Fuzziness is not null)
                return new FuzzyQuery(field, term.Value) { Fuzziness = term.Fuzziness, Boost = term.Boost };

            if (!IsValidValue(term.Value, fieldType))
            {
                _result.AddError($"'{term.Value}' is not a valid value for field '{field}' of type '{fieldType}'.", node.StartPosition, QueryErrorCode.TypeConversionError);
                return new MatchNoneQuery();
            }

            return new TermQuery(field, GetTypedValue(term.Value, fieldType)) { Boost = term.Boost };
        }

        private bool IsGeoField(string field) => GetFieldType(field, GetMapping(field)) == FieldType.GeoPoint;

        private Part BuildGeoDistance(string text, string? distance, string? boostText, string field, QueryNode node)
        {
            if (!TryReadBoost(boostText, node, out float? boost))
                return new Part(new MatchNoneQuery());

            string location = context.GetGeoLocation(text) ?? text;
            if (string.IsNullOrWhiteSpace(location))
                return Error($"A location is required for geo distance queries on '{field}'.", node);

            var query = new GeoDistanceQuery(distance is { Length: > 0 } ? distance : "10mi", field, GeoLocation.Text(location)) { Boost = boost };
            return WrapNested(field, query);
        }

        private Part BuildDefaultFieldsQuery(QueryTerm term, QueryNode node)
        {
            var fields = context.DefaultFields;
            if (fields is not { Length: > 0 })
                return new Part(BuildAnalyzedFieldsQuery(term, null, node));

            var resolved = fields.Select(ResolveDefaultField).ToArray();
            if (resolved.Length == 1)
                return WrapNested(resolved[0], BuildSingleFieldTermQuery(term, resolved[0], node));

            var byPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string field in resolved)
            {
                string path = (context.UseNested ? GetMapping(field)?.NestedPath : null) ?? string.Empty;
                if (!byPath.TryGetValue(path, out var list))
                    byPath[path] = list = [];
                list.Add(field);
            }

            if (byPath.Count == 1 && byPath.ContainsKey(string.Empty))
                return new Part(BuildMultiFieldQuery(term, resolved, node));

            var should = new List<Query>();
            foreach (var (path, pathFields) in byPath)
            {
                if (path.Length == 0)
                {
                    var query = pathFields.Count == 1 ? BuildSingleFieldTermQuery(term, pathFields[0], node) : BuildMultiFieldQuery(term, pathFields.ToArray(), node);
                    if (query.Bool is { Should: { } rootShould } && query.Bool.Must is null && query.Bool.Filter is null)
                        should.AddRange(rootShould);
                    else
                        should.Add(query);
                    continue;
                }

                var branches = pathFields.Select(f => ApplyFilter(BuildSingleFieldTermQuery(term, f, node), context.GetNestedFilter(path, f))).ToList();
                should.Add(new NestedQuery(path, branches.Count == 1 ? branches[0] : new BoolQuery { Should = branches }));
            }

            return new Part(new BoolQuery { Should = should });
        }

        private Query BuildMultiFieldQuery(QueryTerm term, string[] fields, QueryNode node)
        {
            var analyzed = new List<string>();
            var nonAnalyzed = new List<string>();
            foreach (string field in fields)
            {
                if (IsAnalyzed(field, GetMapping(field)))
                    analyzed.Add(field);
                else
                    nonAnalyzed.Add(field);
            }

            if (nonAnalyzed.Count == 0)
                return BuildAnalyzedFieldsQuery(term, analyzed.ToArray(), node);

            if (analyzed.Count == 0)
            {
                return nonAnalyzed.Count == 1
                    ? BuildSingleFieldTermQuery(term, nonAnalyzed[0], node)
                    : new BoolQuery { Should = nonAnalyzed.Select(f => BuildSingleFieldTermQuery(term, f, node)).ToList() };
            }

            var queries = new List<Query> { BuildAnalyzedFieldsQuery(term, analyzed.ToArray(), node) };
            queries.AddRange(nonAnalyzed.Select(f => BuildSingleFieldTermQuery(term, f, node)));
            return new BoolQuery { Should = queries };
        }

        private Query BuildAnalyzedFieldsQuery(QueryTerm term, string[]? fields, QueryNode node)
        {
            if (term.Wildcard is not null)
                return ToQueryString(term, fields);

            if (term.Regex is not null)
            {
                if (fields is not { Length: > 0 })
                    return ToQueryString(term, fields);

                if (fields.Length == 1)
                    return new RegexpQuery(fields[0], term.Regex) { Boost = term.Boost };

                return new BoolQuery { Should = fields.Select(f => (Query)new RegexpQuery(f, term.Regex) { Boost = term.Boost }).ToList() };
            }

            if (fields is { Length: 1 })
                return BuildSingleFieldTermQuery(term, fields[0], node);

            var query = new MultiMatchQuery(term.Value)
            {
                Type = term.Quoted ? TextQueryType.Phrase : null,
                Slop = term.Slop,
                Fuzziness = term.Fuzziness,
                Boost = term.Boost
            };
            if (fields is { Length: > 0 })
                query.Fields = fields;

            return query;
        }

        private QueryStringQuery ToQueryString(QueryTerm term, string[]? fields)
        {
            var query = new QueryStringQuery(term.Expression!)
            {
                AllowLeadingWildcard = context.ValidationOptions?.AllowLeadingWildcards ?? true,
                AnalyzeWildcard = true,
                Boost = term.Boost
            };
            if (fields is { Length: > 0 })
                query.Fields = fields;

            return query;
        }

        private bool TryCreateTerm(TermNode node, out QueryTerm term)
        {
            term = default;
            if (!TryReadBoost(node.BoostText, node, out float? boost))
                return false;

            string raw = node.IsPrefix ? node.Term + "*" : node.Term;
            string? wildcard = null, expression = null, prefixValue = null;
            if (node.IsPrefix || node.IsWildcard)
            {
                BuildWildcard(raw, out wildcard, out expression);
                if (node.IsPrefix)
                    prefixValue = node.UnescapedTerm;

                if (raw[0] is '*' or '?' && context.ValidationOptions is { AllowLeadingWildcards: false })
                {
                    _result.AddError($"Terms must not start with a wildcard: {raw}", node.StartPosition, QueryErrorCode.LeadingWildcardNotAllowed);
                    return false;
                }
            }

            Fuzziness? fuzziness = null;
            if (node.ProximityText is { } proximity)
            {
                if (wildcard is not null)
                {
                    _result.AddError($"Fuzziness cannot be combined with wildcard syntax: {QueryStringBuilder.ToQueryString(node)}", node.StartPosition);
                    return false;
                }

                if (!TryReadFuzziness(proximity, out fuzziness))
                {
                    _result.AddError($"Fuzziness must be 0, 1, 2, AUTO, or AUTO:low,high with non-negative integer thresholds and low <= high: {proximity}", node.StartPosition);
                    return false;
                }
            }

            term = new QueryTerm
            {
                Value = node.UnescapedTerm,
                Wildcard = wildcard,
                Expression = expression,
                IsPrefix = node.IsPrefix,
                PrefixValue = prefixValue,
                Fuzziness = fuzziness,
                Boost = boost
            };
            return true;
        }

        private bool TryReadBoost(string? text, QueryNode node, out float? boost)
        {
            boost = null;
            if (text is null)
                return true;

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && value >= 0)
            {
                boost = value;
                return true;
            }

            _result.AddError($"A query boost must be a finite, non-negative number: {text}", node.StartPosition);
            return false;
        }

        private Part Error(string message, QueryNode node)
        {
            _result.AddError(message, node.StartPosition);
            return Part.None;
        }

        private string ResolveDefaultField(string field)
        {
            return context.MappingResolver is { } resolver && resolver.GetMapping(field) is { Found: true } mapping ? mapping.FullPath : field;
        }

        private ElasticFieldMapping? GetMapping(string field)
        {
            return context.MappingResolver?.GetMapping(field, followAlias: true);
        }

        private FieldType GetFieldType(string field, ElasticFieldMapping? mapping)
        {
            if (mapping is { Found: true })
                return ElasticMappingResolver.GetFieldType(mapping.Property);

            return context.GetRuntimeField(field)?.Type switch
            {
                RuntimeFieldType.Boolean => FieldType.Boolean,
                RuntimeFieldType.Date => FieldType.Date,
                RuntimeFieldType.Double => FieldType.Double,
                RuntimeFieldType.Long => FieldType.Long,
                RuntimeFieldType.GeoPoint => FieldType.GeoPoint,
                RuntimeFieldType.Ip => FieldType.Ip,
                RuntimeFieldType.Keyword => FieldType.Keyword,
                _ => FieldType.None
            };
        }

        private bool IsAnalyzed(string field, ElasticFieldMapping? mapping)
        {
            return mapping is { Found: true } && context.MappingResolver!.IsPropertyAnalyzed(mapping.Property);
        }
    }

    private static bool IsScalar(FieldType type)
    {
        return type is FieldType.Byte or FieldType.Short or FieldType.Integer or FieldType.Long or FieldType.Float
            or FieldType.HalfFloat or FieldType.Double or FieldType.ScaledFloat or FieldType.TokenCount
            or FieldType.Date or FieldType.DateNanos or FieldType.Boolean;
    }

    private static bool IsValidValue(string value, FieldType fieldType)
    {
        return fieldType switch
        {
            FieldType.Integer or FieldType.Short or FieldType.Byte or FieldType.Long or FieldType.Float or FieldType.HalfFloat
                or FieldType.Double or FieldType.ScaledFloat or FieldType.TokenCount
                => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number),
            FieldType.Boolean => bool.TryParse(value, out _),
            _ => true
        };
    }

    private static FieldValue GetTypedValue(string value, FieldType fieldType)
    {
        return fieldType switch
        {
            FieldType.Integer or FieldType.Short or FieldType.Byte when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue) => intValue,
            FieldType.Long when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue) => longValue,
            FieldType.Float or FieldType.HalfFloat when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue) => floatValue,
            FieldType.Double or FieldType.ScaledFloat when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue) => doubleValue,
            FieldType.Boolean when bool.TryParse(value, out bool boolValue) => boolValue,
            _ => value
        };
    }

    private static bool TryReadFuzziness(string value, out Fuzziness? fuzziness)
    {
        fuzziness = null;
        if (value.Length == 0)
        {
            fuzziness = new Fuzziness(2);
        }
        else if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int distance) && distance is >= 0 and <= 2)
        {
            fuzziness = new Fuzziness(distance);
        }
        else if (string.Equals(value, "AUTO", StringComparison.OrdinalIgnoreCase))
        {
            fuzziness = new Fuzziness("AUTO");
        }
        else if (value.StartsWith("AUTO:", StringComparison.OrdinalIgnoreCase))
        {
            string[] thresholds = value[5..].Split(',');
            if (thresholds.Length == 2
                && int.TryParse(thresholds[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int low)
                && int.TryParse(thresholds[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int high)
                && low >= 0 && high >= low)
            {
                fuzziness = new Fuzziness(FormattableString.Invariant($"AUTO:{low},{high}"));
            }
        }

        return fuzziness is not null;
    }

    /// <summary>
    /// Builds the <c>wildcard</c> query pattern (literal <c>* ? \</c> escaped) and a safely escaped
    /// <c>query_string</c> expression that keeps only the wildcard operators, from a raw term.
    /// </summary>
    private static void BuildWildcard(string raw, out string wildcard, out string expression)
    {
        var pattern = new StringBuilder(raw.Length + 4);
        var escaped = new StringBuilder(raw.Length + 4);
        for (int i = 0; i < raw.Length; i++)
        {
            char value = raw[i];
            bool literal = value == '\\';
            if (literal && i + 1 < raw.Length)
                value = raw[++i];

            if (!literal && value is '*' or '?')
            {
                pattern.Append(value);
                escaped.Append(value);
                continue;
            }

            if (value is '*' or '?' or '\\')
                pattern.Append('\\');
            pattern.Append(value);

            if (char.IsWhiteSpace(value) || QueryStringSpecialCharacters.Contains(value))
                escaped.Append('\\');
            escaped.Append(value);
        }

        wildcard = pattern.ToString();
        expression = escaped.ToString();
    }

    private readonly record struct QueryTerm
    {
        public required string Value { get; init; }
        public bool Quoted { get; init; }
        public string? Regex { get; init; }
        public string? Wildcard { get; init; }
        public string? Expression { get; init; }
        public bool IsPrefix { get; init; }
        public string? PrefixValue { get; init; }
        public float? Boost { get; init; }
        public int? Slop { get; init; }
        public Fuzziness? Fuzziness { get; init; }
    }
}
