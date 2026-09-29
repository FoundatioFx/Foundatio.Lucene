using Foundatio.Lucene.Ast;

namespace Foundatio.Lucene;

/// <summary>
/// The aggregation types supported by aggregation expressions.
/// </summary>
public static class AggregationTypes
{
    /// <summary>Minimum value.</summary>
    public const string Min = "min";
    /// <summary>Maximum value.</summary>
    public const string Max = "max";
    /// <summary>Average value.</summary>
    public const string Avg = "avg";
    /// <summary>Sum of values.</summary>
    public const string Sum = "sum";
    /// <summary>Count, min, max, avg, and sum.</summary>
    public const string Stats = "stats";
    /// <summary>Extended statistics.</summary>
    public const string ExtendedStats = "exstats";
    /// <summary>Approximate distinct count.</summary>
    public const string Cardinality = "cardinality";
    /// <summary>Count of documents missing the field.</summary>
    public const string Missing = "missing";
    /// <summary>Percentiles.</summary>
    public const string Percentiles = "percentiles";
    /// <summary>Date histogram buckets.</summary>
    public const string DateHistogram = "date";
    /// <summary>Numeric histogram buckets.</summary>
    public const string Histogram = "histogram";
    /// <summary>Geohash grid buckets.</summary>
    public const string GeoGrid = "geogrid";
    /// <summary>Buckets per distinct value.</summary>
    public const string Terms = "terms";
    /// <summary>The top matching documents.</summary>
    public const string TopHits = "tophits";

    private static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Min, Max, Avg, Sum, Stats, ExtendedStats, Cardinality, Missing, Percentiles, DateHistogram, Histogram, GeoGrid, Terms, TopHits
    };

    private static readonly HashSet<string> Bucket = new(StringComparer.OrdinalIgnoreCase)
    {
        DateHistogram, Histogram, GeoGrid, Terms, Missing
    };

    /// <summary>
    /// Whether <paramref name="type"/> is a supported aggregation type.
    /// </summary>
    public static bool IsKnown(string type) => All.Contains(type);

    /// <summary>
    /// Whether aggregations of <paramref name="type"/> can contain sub-aggregations.
    /// </summary>
    public static bool IsBucket(string type) => Bucket.Contains(type);
}

/// <summary>
/// An aggregation in an aggregation expression such as <c>terms:(status~10 @missing:none min:created)</c>.
/// </summary>
public sealed class AggregationExpression
{
    private Dictionary<string, object?>? _data;

    /// <summary>
    /// Creates an aggregation expression.
    /// </summary>
    public AggregationExpression(string type, string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(field);
        Type = type;
        Field = field;
        OriginalField = field;
    }

    /// <summary>
    /// The aggregation type (lowercase), for example <c>terms</c> or <c>date</c>. See <see cref="AggregationTypes"/>.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// The field to aggregate, after alias resolution.
    /// </summary>
    public string Field { get; set; }

    /// <summary>
    /// The field as written in the expression.
    /// </summary>
    public string OriginalField { get; set; }

    /// <summary>
    /// The raw <c>~</c> modifier: a size, interval, missing value, precision, percents, depending on the type.
    /// </summary>
    public string? ProximityText { get; set; }

    /// <summary>
    /// The raw <c>^</c> modifier: a time zone, minimum document count, or precision threshold, depending on the type.
    /// </summary>
    public string? BoostText { get; set; }

    /// <summary>
    /// The order requested with a <c>+</c> or <c>-</c> prefix. On a sub-aggregation of a terms aggregation this
    /// orders the terms buckets by the sub-aggregation's value.
    /// </summary>
    public SortDirection? Order { get; set; }

    /// <summary>
    /// Whether the aggregation was written in group form, <c>type:(field ...)</c>.
    /// </summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// The 0-based position of the aggregation in the expression.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Sub-aggregations.
    /// </summary>
    public List<AggregationExpression> Aggregations { get; } = [];

    /// <summary>
    /// <c>@name:value</c> modifiers such as <c>@include</c>, <c>@exclude</c>, <c>@missing</c>, <c>@min</c>, or <c>@offset</c>.
    /// </summary>
    public List<AggregationModifier> Modifiers { get; } = [];

    /// <summary>
    /// Arbitrary metadata for providers.
    /// </summary>
    public IDictionary<string, object?> Data => _data ??= new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// The conventional name of the aggregation: <c>{type}_{original field}</c>, or <c>tophits</c>.
    /// </summary>
    public string Name => string.Equals(Type, AggregationTypes.TopHits, StringComparison.OrdinalIgnoreCase) ? AggregationTypes.TopHits : $"{Type}_{OriginalField}";

    /// <summary>
    /// Gets the values of all modifiers with the specified name (without the <c>@</c>).
    /// </summary>
    public IEnumerable<AggregationModifier> GetModifiers(string name) => Modifiers.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the last modifier with the specified name (without the <c>@</c>), or null.
    /// </summary>
    public AggregationModifier? GetModifier(string name) => Modifiers.LastOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public override string ToString() => $"{Type}:{Field}";
}

/// <summary>
/// An <c>@name:value</c> modifier on an aggregation.
/// </summary>
/// <param name="Name">The modifier name without the <c>@</c>, for example <c>include</c>.</param>
/// <param name="Value">The value (unescaped; a regex pattern without slashes when <paramref name="IsRegex"/>).</param>
/// <param name="IsRegex">Whether the value was written as a regular expression.</param>
/// <param name="IsExcluded">Whether the modifier was prefixed with <c>-</c>.</param>
/// <param name="Position">The 0-based position in the expression.</param>
public sealed record AggregationModifier(string Name, string Value, bool IsRegex = false, bool IsExcluded = false, int Position = -1);

/// <summary>
/// Converts aggregation expressions to <see cref="AggregationExpression"/> trees.
/// </summary>
/// <remarks>
/// Each aggregation is written <c>type:field</c> with optional <c>~</c> and <c>^</c> modifiers, or in group form
/// <c>type:(field [modifiers] [@name:value ...] [sub-aggregations ...])</c>. A <c>+</c> or <c>-</c> prefix on a
/// sub-aggregation orders the parent terms aggregation by it. NOT and <c>!</c> are not allowed.
/// </remarks>
public static class AggregationExpressionParser
{
    /// <summary>
    /// Parses an aggregation expression. Errors are added to <paramref name="result"/>.
    /// </summary>
    public static List<AggregationExpression> Parse(string aggregations, QueryValidationResult result, LuceneParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(aggregations);
        ArgumentNullException.ThrowIfNull(result);

        var parsed = LuceneQuery.Parse(aggregations, options);
        foreach (var error in parsed.Errors)
            result.AddError(error.Message, error.Position, error.Code);

        return FromDocument(parsed.Document, result);
    }

    /// <summary>
    /// Converts a parsed aggregation expression. Errors are added to <paramref name="result"/>.
    /// </summary>
    public static List<AggregationExpression> FromDocument(QueryDocument document, QueryValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);

        var aggregations = new List<AggregationExpression>();
        CollectAggregations(document.Query, order: null, aggregations, parent: null, result);
        return aggregations;
    }

    private static void CollectAggregations(QueryNode? node, SortDirection? order, List<AggregationExpression> aggregations, AggregationExpression? parent, QueryValidationResult result)
    {
        switch (node)
        {
            case null:
                break;
            case GroupNode group:
                CollectAggregations(group.Query, order, aggregations, parent, result);
                break;
            case BooleanQueryNode boolean:
                foreach (var clause in boolean.Clauses)
                {
                    var clauseOrder = clause.Modifier switch
                    {
                        ClauseModifier.Plus => SortDirection.Ascending,
                        ClauseModifier.Minus => SortDirection.Descending,
                        _ => order
                    };

                    if (clause.Modifier is ClauseModifier.Not or ClauseModifier.Bang)
                        SortExpression.AddOrderingError(clause.Query, "aggregation", result);
                    else if (parent is not null && clause.Query is FieldQueryNode modifier && IsModifier(modifier))
                        AddModifier(parent, modifier, clause.Modifier == ClauseModifier.Minus, result);
                    else
                        CollectAggregations(clause.Query, clauseOrder, aggregations, parent, result);
                }
                break;
            case NotNode not:
                SortExpression.AddOrderingError(not.Query, "aggregation", result);
                break;
            case FieldQueryNode field when parent is not null && IsModifier(field):
                AddModifier(parent, field, isExcluded: false, result);
                break;
            case FieldQueryNode field:
                if (CreateAggregation(field, order, result) is { } aggregation)
                    aggregations.Add(aggregation);
                break;
            default:
                result.AddError($"Unexpected {Describe(node)} in aggregation expression. Aggregations are written as type:field or type:(field ...).", node.StartPosition);
                break;
        }
    }

    private static AggregationExpression? CreateAggregation(FieldQueryNode node, SortDirection? order, QueryValidationResult result)
    {
        string type = node.Field.ToLowerInvariant();
        if (!AggregationTypes.IsKnown(type))
        {
            result.AddError($"Unknown aggregation type '{node.Field}'.", node.StartPosition, QueryErrorCode.OperationNotAllowed);
            return null;
        }

        switch (node.Query)
        {
            case TermNode term when !term.IsWildcard && !term.IsPrefix:
                return new AggregationExpression(type, term.UnescapedTerm)
                {
                    ProximityText = term.ProximityText,
                    BoostText = term.BoostText,
                    Order = order,
                    Position = node.StartPosition
                };
            case PhraseNode phrase:
                return new AggregationExpression(type, phrase.Phrase)
                {
                    ProximityText = phrase.ProximityText,
                    BoostText = phrase.BoostText,
                    Order = order,
                    Position = node.StartPosition
                };
            case GroupNode group:
                return CreateGroupAggregation(type, node, group, order, result);
            default:
                result.AddError($"Aggregations ({type}) must specify a field.", node.StartPosition);
                return null;
        }
    }

    private static AggregationExpression? CreateGroupAggregation(string type, FieldQueryNode node, GroupNode group, SortDirection? order, QueryValidationResult result)
    {
        var items = new List<(QueryNode Node, ClauseModifier Modifier)>();
        Flatten(group.Query, ClauseModifier.None, items);

        if (items.Count == 0 || items[0].Node is not (TermNode or PhraseNode))
        {
            result.AddError($"Aggregations ({type}) must specify a field.", node.StartPosition);
            return null;
        }

        var (first, firstModifier) = items[0];
        if (firstModifier is ClauseModifier.Not or ClauseModifier.Bang)
            SortExpression.AddOrderingError(first, "aggregation", result);

        var aggregation = first switch
        {
            TermNode term => new AggregationExpression(type, term.UnescapedTerm) { ProximityText = term.ProximityText, BoostText = term.BoostText },
            PhraseNode phrase => new AggregationExpression(type, phrase.Phrase) { ProximityText = phrase.ProximityText, BoostText = phrase.BoostText },
            _ => throw new InvalidOperationException()
        };

        aggregation.IsGroup = true;
        aggregation.Order = order;
        aggregation.Position = node.StartPosition;
        aggregation.BoostText ??= group.BoostText;

        for (int i = 1; i < items.Count; i++)
        {
            var (item, modifier) = items[i];
            if (modifier is ClauseModifier.Not or ClauseModifier.Bang)
            {
                SortExpression.AddOrderingError(item, "aggregation", result);
                continue;
            }

            if (item is FieldQueryNode field && IsModifier(field))
            {
                AddModifier(aggregation, field, modifier == ClauseModifier.Minus, result);
                continue;
            }

            if (item is not FieldQueryNode subField)
            {
                result.AddError($"Unexpected {Describe(item)} in aggregation ({type}:{aggregation.Field}). Only one field may be specified.", item.StartPosition);
                continue;
            }

            if (!AggregationTypes.IsBucket(type))
            {
                result.AddError($"Aggregation type '{type}' does not support sub-aggregations.", item.StartPosition);
                continue;
            }

            var subOrder = modifier switch
            {
                ClauseModifier.Plus => SortDirection.Ascending,
                ClauseModifier.Minus => SortDirection.Descending,
                _ => (SortDirection?)null
            };

            if (CreateAggregation(subField, subOrder, result) is { } sub)
                aggregation.Aggregations.Add(sub);
        }

        return aggregation;
    }

    private static void Flatten(QueryNode? node, ClauseModifier modifier, List<(QueryNode, ClauseModifier)> items)
    {
        switch (node)
        {
            case null:
                break;
            case GroupNode { Query: var inner }:
                Flatten(inner, modifier, items);
                break;
            case BooleanQueryNode boolean:
                foreach (var clause in boolean.Clauses)
                    Flatten(clause.Query, clause.Modifier == ClauseModifier.None ? modifier : clause.Modifier, items);
                break;
            case NotNode not:
                Flatten(not.Query, ClauseModifier.Not, items);
                break;
            default:
                items.Add((node, modifier));
                break;
        }
    }

    private static bool IsModifier(FieldQueryNode node) => node.Field.Length > 1 && node.Field[0] == '@';

    private static void AddModifier(AggregationExpression aggregation, FieldQueryNode node, bool isExcluded, QueryValidationResult result)
    {
        string name = node.Field[1..];
        switch (node.Query)
        {
            case TermNode term:
                aggregation.Modifiers.Add(new AggregationModifier(name, term.UnescapedTerm, IsExcluded: isExcluded, Position: node.StartPosition));
                break;
            case PhraseNode phrase:
                aggregation.Modifiers.Add(new AggregationModifier(name, phrase.Phrase, IsExcluded: isExcluded, Position: node.StartPosition));
                break;
            case RegexNode regex:
                aggregation.Modifiers.Add(new AggregationModifier(name, regex.Pattern, IsRegex: true, IsExcluded: isExcluded, Position: node.StartPosition));
                break;
            default:
                result.AddError($"Aggregation modifier (@{name}) must have a value.", node.StartPosition);
                break;
        }
    }

    private static string Describe(QueryNode node) => $"'{QueryStringBuilder.ToQueryString(node)}'";
}
