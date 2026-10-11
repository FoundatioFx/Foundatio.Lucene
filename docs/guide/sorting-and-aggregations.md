# Sorting and Aggregations

Besides queries, Foundatio.Lucene parses two more kinds of expressions written in the same syntax: sort expressions and aggregation expressions. Both run through the same pipeline as queries — includes are expanded, field aliases are resolved, and validation rules apply — so a field alias or restriction you configure once applies everywhere.

## Sort expressions

A sort expression is a list of fields separated by whitespace:

```
-created +title count
```

| Syntax | Order |
|---|---|
| `field` or `+field` | ascending |
| `-field` | descending |
| `field:asc`, `field:desc` | explicit |
| `location:"51.5,-0.12"`, `-location:gcpvj0` | by distance from a point (Elasticsearch `geo_point` fields) |
| `-(a b +c)` | a prefix on a group applies to the fields inside it, so `a` and `b` are descending and `c` ascending |

`NOT` and `!` are rejected in sort expressions (use `-`), as are wildcards, ranges, and other query syntax.

The parsed form is a list of `SortField` (`Field`, `OriginalField`, `Direction`, and the `Value` written after the field, if any). Providers turn it into their own sort:

::: code-group

```csharp [Elasticsearch]
var parser = new ElasticsearchQueryParser(c => c.UseMappings(mapping));
ICollection<SortOptions> sort = parser.BuildSort("-created +title");
```

```csharp [Core]
var result = new QueryValidationResult();
List<SortField> fields = SortExpression.Parse("-created +title", result);
```

:::

### Elasticsearch sorts

With a mapping, the Elasticsearch provider picks a sortable field and sets `unmapped_type`, so sorting on a field that some indices don't have doesn't fail:

| Field | Sort |
|---|---|
| keyword `status` | `{"status":{"order":"asc","unmapped_type":"keyword"}}` |
| text `title` with a `sort` sub-field | `{"title.sort":{...}}` |
| text `message` with only a `keyword` sub-field | `{"message.keyword":{...}}` |
| integer `count` | `{"count":{"order":"asc","unmapped_type":"integer"}}` |
| nested `children.name` | adds `"nested":{"path":"children"}` (nested chains for multi-level nesting, plus the nested filter if one is configured) |
| `_score`, `_doc` | score and index-order sorts |
| geo_point `location:"51.5,-0.12"` or `location:gcpvj0` | `{"_geo_distance":{"location":{"lat":51.5,"lon":-0.12},"order":"asc","distance_type":"arc"}}` |

A value on any other field is a validation error, as it is for the Entity Framework provider.

## Aggregation expressions

An aggregation is written `type:field`, with optional `~` and `^` modifiers:

```
terms:status~10 date:created~1d max:price
```

or in group form, which allows `@` modifiers and sub-aggregations:

```
terms:(status~10 @missing:none @exclude:deleted max:created -avg:price)
date:(created~month terms:(status~5 sum:price))
```

In group form the first term is the field; `@name:value` items are modifiers; other `type:field` items are sub-aggregations. A `+` or `-` prefix on a sub-aggregation of a terms aggregation orders the terms buckets by it (ascending or descending). `NOT` and `!` are not allowed.

Each aggregation is named `{type}_{field}` using the field as written (before alias resolution), so `terms:status` produces `terms_status`. Top hits aggregations are named `tophits`.

### Types

| Type | `~` | `^` | Group modifiers |
|---|---|---|---|
| `min`, `max` | missing value | time zone (reported in `@timezone` meta) | |
| `avg`, `sum`, `stats`, `exstats` | missing value | | |
| `cardinality` | missing value | precision threshold | |
| `missing` | | | sub-aggregations |
| `percentiles` | comma-separated percents (`~50,95,99.9`) | | |
| `histogram` | interval (default 50) | | sub-aggregations |
| `date` | interval | time zone | `@missing:date`, `@offset:6h` (`-@offset:6h` or `@offset:"-6h"` for negative), sub-aggregations |
| `geogrid` | precision 1–12 (default 1) | | sub-aggregations |
| `terms` | size | minimum document count | `@include:value`, `@include:/regex/`, `@exclude:...`, `@missing:value`, `@min:count`, sub-aggregations |
| `tophits` | size | | `@include:field`, `@exclude:field` (source filtering) |

Unknown types and invalid modifier values are validation errors.

### Date histograms

The interval accepts calendar units — `s`, `m`, `h`, `d`, `w`, `M`, `q`, `y` (or `1d`, `day`, and so on) — or any fixed interval such as `2d` or `90m`. Without an interval the histogram uses days, or, when the request has a `StartDate` and `EndDate`, a fixed interval that produces about 100 buckets together with `extended_bounds`.

The time zone comes from `^` (`date:created^-5h`, `date:created^"America/Chicago"`) or the configured default time zone. Offsets written as time units (`1h`, `-5h`) are converted to `+01:00` / `-05:00`.

### Elasticsearch aggregations

```csharp
var parser = new ElasticsearchQueryParser(c => c.UseMappings(mapping));
IDictionary<string, Aggregation> aggregations = parser.BuildAggregations("terms:(status~10 max:created) date:created~1d");
```

The dictionary can be assigned directly to a search request's `Aggregations`. Text fields are aggregated on their keyword sub-field, aggregations on nested fields are wrapped in `nested_{path}` aggregations (and in a `filtered_{name}` filter aggregation when a nested filter is configured), and terms ordering on nested sub-aggregations uses the right bucket path. Most aggregations carry `@field_type` meta with the mapped type of the field, which helps callers format bucket keys and values.

## Custom visitors

Visitors added with `AddVisitor` (or just to one pipeline with `AddSortVisitor` and `AddAggregationVisitor`) run on sort and aggregation expressions after includes are expanded and before fields are resolved, so they can rename fields or rewrite the expression. With Elasticsearch a visitor can also supply the sort or aggregation for a node with `node.SetSort(...)` and `node.SetAggregation(...)`, including aggregation types the provider doesn't know. See [Custom Visitors](./custom-visitors#custom-sorts-and-aggregations).

## Includes in sorts and aggregations

`@include:name` works at the top level of both kinds of expressions, so saved sorts and aggregation sets can be reused. Inside an aggregation group, `@include` is the terms/top hits modifier instead.

## Validation

`QueryValidator.ValidateSort` and `QueryValidator.ValidateAggregations` (and the providers' `ValidateSort`/`ValidateAggregations`, with `Async` versions for parsers with asynchronous resolvers) apply the same `QueryValidationOptions` as queries. For aggregations, `AllowedOperations` and `RestrictedOperations` are the aggregation types, and `AllowedMaxSortFields` limits the number of sort fields.

```csharp
var options = new QueryValidationOptions();
options.AllowedFields.Add("status");
options.AllowedFields.Add("created");
options.AllowedOperations.Add("terms");
options.AllowedOperations.Add("date");

var result = QueryValidator.ValidateAggregations("terms:status date:created", options);
```
