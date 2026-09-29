using System.Globalization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;
using Elastic.Clients.Elasticsearch.Core.Search;

namespace Foundatio.Lucene.Elasticsearch;

/// <summary>
/// Converts aggregation expressions into Elasticsearch aggregations.
/// </summary>
internal static class ElasticsearchAggregationBuilder
{
    /// <summary>
    /// The maximum bucket count Elasticsearch allows by default (<c>search.max_buckets</c> guidance for shard sizing).
    /// </summary>
    public const int MaxBucketSize = 10000;

    private const string FieldTypeMeta = "@field_type";
    private const string TimeZoneMeta = "@timezone";

    public static Dictionary<string, Aggregation> Build(IReadOnlyList<AggregationExpression> expressions, ElasticsearchQueryVisitorContext context)
    {
        var root = new Dictionary<string, Aggregation>(StringComparer.Ordinal);
        new Builder(context).AddAll(root, parentTerms: null, expressions, currentNestedPath: null);
        context.ValidationResult.ThrowIfInvalid();
        return root;
    }

    private sealed class Builder(ElasticsearchQueryVisitorContext context)
    {
        private readonly QueryValidationResult _result = context.ValidationResult;

        public void AddAll(IDictionary<string, Aggregation> container, TermsAggregation? parentTerms, IReadOnlyList<AggregationExpression> expressions, string? currentNestedPath)
        {
            foreach (var expression in expressions)
            {
                var aggregation = Create(expression);
                if (aggregation is null)
                    continue;

                string? nestedPath = context.UseNested ? GetMapping(expression.Field)?.NestedPath : null;
                var target = container;
                string bucketPath = string.Empty;

                if (nestedPath is not null && !string.Equals(nestedPath, currentNestedPath, StringComparison.Ordinal))
                {
                    foreach (string path in GetNestedChain(expression.Field, currentNestedPath))
                    {
                        string key = $"nested_{path}";
                        if (!target.TryGetValue(key, out var nested))
                        {
                            nested = new Aggregation { Nested = new NestedAggregation { Path = path }, Aggregations = new Dictionary<string, Aggregation>(StringComparer.Ordinal) };
                            target[key] = nested;
                        }

                        target = nested.Aggregations ??= new Dictionary<string, Aggregation>(StringComparer.Ordinal);
                        bucketPath += key + ">";
                    }

                    if (context.GetNestedFilter(nestedPath, expression.Field) is { } filter)
                    {
                        string filteredKey = $"filtered_{expression.Name}";
                        var filtered = new Aggregation { Filter = filter, Aggregations = new Dictionary<string, Aggregation>(StringComparer.Ordinal) };
                        target[filteredKey] = filtered;
                        target = filtered.Aggregations;
                        bucketPath += filteredKey + ">";
                    }
                }

                target[expression.Name] = aggregation;

                if (parentTerms is not null && expression.Order is { } order)
                {
                    parentTerms.Order ??= new List<KeyValuePair<Field, SortOrder>>();
                    parentTerms.Order.Add(new KeyValuePair<Field, SortOrder>(bucketPath + expression.Name, order == SortDirection.Descending ? SortOrder.Desc : SortOrder.Asc));
                }

                if (expression.Aggregations.Count > 0)
                {
                    aggregation.Aggregations ??= new Dictionary<string, Aggregation>(StringComparer.Ordinal);
                    AddAll(aggregation.Aggregations, aggregation.Terms, expression.Aggregations, nestedPath ?? currentNestedPath);
                }
            }
        }

        private IEnumerable<string> GetNestedChain(string field, string? currentNestedPath)
        {
            var chain = GetMapping(field)?.NestedPathChain ?? [];
            foreach (string path in chain)
            {
                if (currentNestedPath is not null && (path == currentNestedPath || currentNestedPath.StartsWith(path + ".", StringComparison.Ordinal)))
                    continue;

                yield return path;
            }
        }

        private Aggregation? Create(AggregationExpression expression)
        {
            string field = GetAggregationField(expression.Field);
            string? fieldType = GetMapping(expression.Field)?.Property?.Type ?? context.GetRuntimeField(expression.Field)?.Type.ToString().ToLowerInvariant();

            switch (expression.Type)
            {
                case AggregationTypes.Min:
                    return WithMeta(new MinAggregation { Field = field, Missing = ReadDouble(expression, expression.ProximityText, "missing value") }, fieldType, expression.BoostText ?? context.DefaultTimeZone);
                case AggregationTypes.Max:
                    return WithMeta(new MaxAggregation { Field = field, Missing = ReadDouble(expression, expression.ProximityText, "missing value") }, fieldType, expression.BoostText ?? context.DefaultTimeZone);
                case AggregationTypes.Avg:
                    return WithMeta(new AverageAggregation { Field = field, Missing = ReadDouble(expression, expression.ProximityText, "missing value") }, fieldType);
                case AggregationTypes.Sum:
                    return WithMeta(new SumAggregation { Field = field, Missing = ReadDouble(expression, expression.ProximityText, "missing value") }, fieldType);
                case AggregationTypes.Stats:
                    return WithMeta(new StatsAggregation { Field = field, Missing = ReadDouble(expression, expression.ProximityText, "missing value") }, fieldType);
                case AggregationTypes.ExtendedStats:
                    return WithMeta(new ExtendedStatsAggregation { Field = field, Missing = ReadDouble(expression, expression.ProximityText, "missing value") }, fieldType);
                case AggregationTypes.Cardinality:
                    return new CardinalityAggregation
                    {
                        Field = field,
                        Missing = ReadDouble(expression, expression.ProximityText, "missing value"),
                        PrecisionThreshold = ReadInt(expression, expression.BoostText, "precision threshold")
                    };
                case AggregationTypes.Missing:
                    return new MissingAggregation { Field = field };
                case AggregationTypes.Percentiles:
                    return CreatePercentiles(expression, field);
                case AggregationTypes.Histogram:
                    return new HistogramAggregation
                    {
                        Field = field,
                        MinDocCount = 0,
                        Interval = ReadDouble(expression, expression.ProximityText, "interval") ?? 50
                    };
                case AggregationTypes.DateHistogram:
                    return CreateDateHistogram(expression, field);
                case AggregationTypes.GeoGrid:
                    return CreateGeoGrid(expression, field);
                case AggregationTypes.Terms:
                    return CreateTerms(expression, field, fieldType);
                case AggregationTypes.TopHits:
                    return CreateTopHits(expression);
                default:
                    _result.AddError($"Unknown aggregation type '{expression.Type}'.", expression.Position);
                    return null;
            }
        }

        private Aggregation CreatePercentiles(AggregationExpression expression, string field)
        {
            List<double>? percents = null;
            if (!string.IsNullOrWhiteSpace(expression.ProximityText))
            {
                percents = [];
                foreach (string value in expression.ProximityText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) && percent is >= 0 and <= 100)
                        percents.Add(percent);
                    else
                        _result.AddError($"Invalid percentile '{value}' for aggregation {expression.Name}; percentiles must be numbers from 0 to 100.", expression.Position);
                }
            }

            return new PercentilesAggregation { Field = field, Percents = percents };
        }

        private Aggregation CreateDateHistogram(AggregationExpression expression, string field)
        {
            var start = context.StartDate;
            var end = context.EndDate;
            bool hasRange = start.HasValue && end.HasValue && start.Value <= end.Value;

            var histogram = new DateHistogramAggregation
            {
                Field = field,
                MinDocCount = 0,
                Format = "date_optional_time",
                TimeZone = ElasticsearchTimeZone.Normalize(expression.BoostText ?? context.DefaultTimeZone)
            };

            if (hasRange)
                histogram.ExtendedBounds = new ExtendedBounds<FieldDateMath> { Min = start!.Value.UtcDateTime, Max = end!.Value.UtcDateTime };

            string? interval = expression.ProximityText?.Trim();
            if (string.IsNullOrEmpty(interval))
            {
                if (hasRange)
                    histogram.FixedInterval = GetAutomaticInterval(end!.Value - start!.Value);
                else
                    histogram.CalendarInterval = CalendarInterval.Day;
            }
            else
            {
                var calendar = interval switch
                {
                    "s" or "1s" or "second" => CalendarInterval.Second,
                    "m" or "1m" or "minute" => CalendarInterval.Minute,
                    "h" or "1h" or "hour" => CalendarInterval.Hour,
                    "d" or "1d" or "day" => CalendarInterval.Day,
                    "w" or "1w" or "week" => CalendarInterval.Week,
                    "M" or "1M" or "month" => CalendarInterval.Month,
                    "q" or "1q" or "quarter" => CalendarInterval.Quarter,
                    "y" or "1y" or "year" => CalendarInterval.Year,
                    _ => (CalendarInterval?)null
                };

                if (calendar is not null)
                    histogram.CalendarInterval = calendar;
                else
                    histogram.FixedInterval = interval;
            }

            if (expression.GetModifier("missing") is { } missing)
            {
                if (DateTimeOffset.TryParse(missing.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var missingDate))
                    histogram.Missing = missingDate.UtcDateTime;
                else
                    _result.AddError($"Invalid @missing date '{missing.Value}' for aggregation {expression.Name}.", missing.Position);
            }

            if (expression.GetModifier("offset") is { } offset)
                histogram.Offset = offset.IsExcluded && !offset.Value.StartsWith('-') ? "-" + offset.Value : offset.Value;

            Aggregation aggregation = histogram;
            if (expression.BoostText is not null)
                aggregation.Meta = new Dictionary<string, object> { [TimeZoneMeta] = expression.BoostText };

            return aggregation;
        }

        private static Duration GetAutomaticInterval(TimeSpan range, int buckets = 100)
        {
            var perBucket = TimeSpan.FromMinutes(range.TotalMinutes / buckets);
            if (perBucket.TotalDays > 1)
                return Round(perBucket, TimeSpan.FromDays(1));
            if (perBucket.TotalHours > 1)
                return Round(perBucket, TimeSpan.FromHours(1));
            if (perBucket.TotalMinutes > 1)
                return Round(perBucket, TimeSpan.FromMinutes(1));

            var seconds = Round(perBucket, TimeSpan.FromSeconds(15));
            return seconds < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(15) : seconds;
        }

        private static TimeSpan Round(TimeSpan value, TimeSpan unit) => TimeSpan.FromTicks((long)Math.Round(value.Ticks / (double)unit.Ticks) * unit.Ticks);

        private Aggregation? CreateGeoGrid(AggregationExpression expression, string field)
        {
            int precision = 1;
            if (!string.IsNullOrEmpty(expression.ProximityText))
            {
                if (!int.TryParse(expression.ProximityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out precision) || precision is < 1 or > 12)
                {
                    _result.AddError("GeoHashGrid precision must be between 1 and 12.", expression.Position);
                    return null;
                }
            }

            return new Aggregation
            {
                GeohashGrid = new GeohashGridAggregation { Field = field, Precision = new GeohashPrecision(precision) },
                Aggregations = new Dictionary<string, Aggregation>(StringComparer.Ordinal)
                {
                    ["avg_lat"] = new AverageAggregation { Script = new Script { Source = $"doc['{field}'].lat" } },
                    ["avg_lon"] = new AverageAggregation { Script = new Script { Source = $"doc['{field}'].lon" } }
                }
            };
        }

        private Aggregation CreateTerms(AggregationExpression expression, string field, string? fieldType)
        {
            var terms = new TermsAggregation
            {
                Field = field,
                Size = ReadInt(expression, expression.ProximityText, "size"),
                MinDocCount = ReadInt(expression, expression.BoostText, "minimum document count")
            };

            if (terms.Size is { } size && size * 1.5 + 10 > MaxBucketSize)
                terms.ShardSize = Math.Max(size, MaxBucketSize);

            List<string>? include = null, exclude = null;
            foreach (var modifier in expression.Modifiers)
            {
                switch (modifier.Name.ToLowerInvariant())
                {
                    case "include" when modifier.IsRegex:
                        terms.Include = new TermsInclude(modifier.Value);
                        break;
                    case "include":
                        (include ??= []).Add(modifier.Value);
                        break;
                    case "exclude" when modifier.IsRegex:
                        terms.Exclude = new TermsExclude(modifier.Value);
                        break;
                    case "exclude":
                        (exclude ??= []).Add(modifier.Value);
                        break;
                    case "missing":
                        terms.Missing = modifier.Value;
                        break;
                    case "min":
                        terms.MinDocCount = ReadInt(expression, modifier.Value, "@min");
                        break;
                    default:
                        _result.AddError($"Unknown modifier '@{modifier.Name}' for terms aggregation {expression.Name}.", modifier.Position);
                        break;
                }
            }

            if (include is not null)
                terms.Include = new TermsInclude(include);
            if (exclude is not null)
                terms.Exclude = new TermsExclude(exclude);

            return WithMeta(terms, fieldType);
        }

        private Aggregation CreateTopHits(AggregationExpression expression)
        {
            var topHits = new TopHitsAggregation { Size = ReadInt(expression, expression.ProximityText, "size") };
            var includes = expression.GetModifiers("include").Select(m => m.Value).ToList();
            var excludes = expression.GetModifiers("exclude").Select(m => m.Value).ToList();
            if (includes.Count > 0 || excludes.Count > 0)
            {
                topHits.Source = new SourceFilter
                {
                    Includes = includes.Count > 0 ? includes.ToArray() : null,
                    Excludes = excludes.Count > 0 ? excludes.ToArray() : null
                };
            }

            return topHits;
        }

        private static Aggregation WithMeta(Aggregation aggregation, string? fieldType, string? timeZone = null)
        {
            if (fieldType is null && timeZone is null)
                return aggregation;

            var meta = new Dictionary<string, object>();
            if (fieldType is not null)
                meta[FieldTypeMeta] = fieldType;
            if (timeZone is not null)
                meta[TimeZoneMeta] = timeZone;

            aggregation.Meta = meta;
            return aggregation;
        }

        private double? ReadDouble(AggregationExpression expression, string? text, string description)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
                return value;

            _result.AddError($"Invalid {description} '{text}' for aggregation {expression.Name}; expected a number.", expression.Position);
            return null;
        }

        private int? ReadInt(AggregationExpression expression, string? text, string description)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0)
                return value;

            _result.AddError($"Invalid {description} '{text}' for aggregation {expression.Name}; expected a non-negative integer.", expression.Position);
            return null;
        }

        private string GetAggregationField(string field)
        {
            if (context.MappingResolver is not { } resolver || field is "_" or "")
                return field;

            return resolver.GetAggregationsFieldName(field);
        }

        private FieldMapping? GetMapping(string field)
        {
            if (field is "_" or "")
                return null;

            return context.MappingResolver?.GetMapping(field);
        }
    }
}
