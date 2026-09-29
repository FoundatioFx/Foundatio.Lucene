# Elasticsearch

`Foundatio.Lucene.Elasticsearch` builds Elasticsearch queries, aggregations, and sorts from Lucene-syntax expressions using the official `Elastic.Clients.Elasticsearch` 9.x client.

```bash
dotnet add package Foundatio.Lucene.Elasticsearch
```

## Quick start

```csharp
using Foundatio.Lucene.Elasticsearch;

var parser = new ElasticsearchQueryParser(c => c.UseMappings(ElasticMappingResolver.Create(client, "events")));

var search = await parser.BuildSearchAsync(
    query: "type:error AND (status:open OR status:regressed) -tags:ignore",
    aggregations: "terms:(status~10 max:created) date:created~1d",
    sort: "-created");

var response = await client.SearchAsync<Event>(s => s.Indices("events").Apply(search));
```

Create the parser once and share it; it's thread-safe.

## Mappings

With a mapping the provider generates the right query for each field type. Without one every field is treated as an unmapped keyword field, like a mapping-less `query_string`.

```csharp
// Load from the server (an index, alias, or pattern); lookups are cached and refreshed when fields are missing.
c.UseMappings(ElasticMappingResolver.Create(client, "events"));

// Or declare it in code (no I/O; ideal for tests).
c.UseMappings(new TypeMapping { Properties = new Properties { { "status", new KeywordProperty() } } });
```

Server mappings are loaded in the resolution phase of the `Async` methods. To use the synchronous methods, load the mapping at startup:

```csharp
var resolver = ElasticMappingResolver.Create(client, "events");
await resolver.EnsureLoadedAsync();
var parser = new ElasticsearchQueryParser(c => c.UseMappings(resolver));
var query = parser.BuildQuery("status:open");
```

See [Elasticsearch Mappings](./elasticsearch-mappings) for sources, merging, and refresh behavior.

### How terms are translated

| Term | Text field | Keyword, numeric, date, boolean, or unmapped field |
|---|---|---|
| `title:hello` | `match` | `term` (with a typed value for numbers and booleans) |
| `title:"hello world"` | `match_phrase` | `term`, or `match_phrase` with slop |
| `title:hel*` | `query_string` with `analyze_wildcard` | `prefix` |
| `title:h?llo`, `title:*llo` | `query_string` | `wildcard` |
| `title:helo~1` | `match` with fuzziness | `fuzzy` |
| `title:/hel+o/` | `regexp` | `regexp` |
| `title:*`, `_exists_:title` | `exists` | `exists` |
| `_missing_:title` | `bool.must_not.exists` | `bool.must_not.exists` |
| `price:[1 TO 5]`, `price:>5` | `range` | `range` (dates use `range` with `time_zone`) |

Regex, fuzzy, and phrase slop on numeric, date, and boolean fields, and values that don't fit the field's type (`count:abc`), are validation errors.

Field names are matched against the mapping ignoring case and replaced with the mapped spelling. Fields that are neither mapped nor runtime fields are reported as unresolved; set `AllowUnresolvedFields = false` to reject them.

### Default fields

Terms without a field search `DefaultFields`:

```csharp
c.DefaultFields = ["title", "body", "tags"];
```

Analyzed default fields are searched together with `multi_match`; non-analyzed ones get their own typed clauses; default fields under nested paths get nested queries. Without default fields, Elasticsearch's `index.query.default_field` applies.

## Boolean logic and scoring

Queries run in filter context by default: the whole query is wrapped in `bool.filter`, which is cacheable and doesn't score. Set `UseScoring = true` (or call `UseSearchMode()`, which also makes OR the default operator) for relevance-ranked search.

| Query | Elasticsearch |
|---|---|
| `a AND b` | `bool.must` (scoring) or `bool.filter` (filter context) |
| `a OR b` | `bool.should` with `minimum_should_match: 1` |
| `-a`, `NOT a` | `bool.must_not` |
| `+a b` (default OR) | `a` required, `b` only boosts the score |
| `a OR NOT b` | `bool.should: [a, bool.must_not: b]` |
| `(a OR b)^2` | `bool.must: [...]` with `boost: 2` |

See [Query Syntax](./query-syntax#boolean-logic) for precedence and the rules for `+`, `-`, and `NOT`.

## Nested fields

Fields under `nested` mappings are queried with `nested` queries automatically. Clauses on the same nested path are combined into one `nested` query, so they must match the same nested document:

```
children.name:x AND children.age:>5
→ nested(children, bool.must: [children.name:x, children.age:>5])
```

Deeper paths are folded into their ancestor's nested query, clauses on sibling paths (`parent.a.x` and `parent.b.y`) are combined under their shared parent, and `children:(children.name:x children.age:>5)` writes a nested group explicitly (fields inside the group keep their full paths). A `-` or `NOT` clause on a nested field excludes documents that have any matching nested document; put it inside a nested group to exclude it from the matched nested document only. Sorts and aggregations on nested fields get nested sorts and `nested_{path}` aggregations. Set `UseNested = false` to query nested fields as ordinary fields.

A `NestedFilterResolver` adds a filter inside every nested query, sort, and aggregation — for example to restrict nested documents to the current tenant:

```csharp
c.NestedFilterResolver = (filter, context, cancellationToken) =>
    ValueTask.FromResult<Query?>(new TermQuery($"{filter.NestedPath}.tenantId", tenantId));
```

## Dates and time zones

Date math is passed through to Elasticsearch, which evaluates it:

```
created:[now-7d/d TO now]
created:>=2024-01-01||+1M/d
```

Set a default time zone for date ranges and date histograms, or give one per range with `^`:

```csharp
c.DefaultTimeZone = "America/Chicago";
```

```
created:[2024-01-01 TO 2024-01-31]^"Europe/London"
created:[now/d TO *]^-5h
```

On a date range `^` is always the time zone; offsets written as time units (`-5h`) are converted to `-05:00`.

## Geo queries

Terms on `geo_point` fields are distance queries; ranges are bounding boxes:

```
location:"51.5,-0.12"~5km
location:[51.5,-0.12 TO 50.0,1.0]
```

The distance defaults to `10mi`. To accept place names, configure a `GeoLocationResolver`; it runs in the resolution phase and returns a `lat,lon` or geohash:

```csharp
c.GeoLocationResolver = async (location, context, cancellationToken) => await geocoder.LookupAsync(location, cancellationToken);
```

```
location:"London"~5km
```

## Runtime fields

A `RuntimeFieldResolver` supplies definitions for fields that aren't in the mapping. The fields a query uses are collected so you can add them to the request (`BuildSearch` does this for you):

```csharp
c.RuntimeFieldResolver = (field, context, cancellationToken) => ValueTask.FromResult(
    field == "day_of_week"
        ? new ElasticRuntimeField("day_of_week", RuntimeFieldType.Keyword, "emit(doc['created'].value.dayOfWeekEnum.toString())")
        : null);

var context = parser.CreateContext();
var query = await parser.BuildQueryAsync("day_of_week:MONDAY", context);
// context.RuntimeFields contains day_of_week
```

## Aggregations and sorts

```csharp
IDictionary<string, Aggregation> aggregations = parser.BuildAggregations("terms:(status~10 max:created) date:created~1d");
ICollection<SortOptions> sort = parser.BuildSort("-created +title");
```

See [Sorting and Aggregations](./sorting-and-aggregations) for the syntax, every aggregation type, and how sub-fields and nested fields are handled.

## Per-request and per-index options

Pass per-request settings with an immutable `ElasticsearchQueryOptions`, which is safe to cache per tenant:

```csharp
var options = new ElasticsearchQueryOptions
{
    FieldMap = tenantFieldMap,
    DefaultFields = ["title"],
    UseScoring = true,
    DefaultTimeZone = user.TimeZone,
    ValidationOptions = tenantValidation
};

var query = parser.BuildQuery("title:report", options);
```

Options can also be registered by name (typically an index) and selected with `Index`:

```csharp
parser.SetOptions("logs", new ElasticsearchQueryOptions { MappingResolver = logsMapping, DefaultFields = ["message"] });
var query = parser.BuildQuery("error", new ElasticsearchQueryOptions { Index = "logs" });
```

Per-request options win over registered options, which win over the parser configuration.

## Synchronous and asynchronous building

| You have | Use |
|---|---|
| no async dependencies (code mapping or preloaded server mapping, static includes and field maps) | `BuildQuery`, `BuildAggregations`, `BuildSort`, `BuildSearch` |
| a server mapping not yet loaded, `IncludeResolver`, `AsyncFieldResolver`, `GeoLocationResolver`, `RuntimeFieldResolver`, or `NestedFilterResolver` | the `Async` methods |

The synchronous methods throw `InvalidOperationException` when async dependencies haven't been resolved, so they never block on I/O.

## Custom queries for fields

A visitor can replace the query generated for a node with `node.SetQuery(query)`. See [Custom Visitors](./custom-visitors#custom-elasticsearch-queries).

## Errors

`BuildQuery` throws `QueryParseException` for syntax errors and `QueryValidationException` for invalid queries. `TryBuildQuery` and `TryBuildQueryAsync` return a `QueryResult<Query>` instead. `ValidateQuery`, `ValidateAggregations`, and `ValidateSort` validate without building, including mapping-based field resolution.
