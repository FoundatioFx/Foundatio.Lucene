# Migrating from Foundatio.Parsers

Foundatio.Lucene replaces Foundatio.Parsers. It supports the same query, sort, and aggregation syntax and the same Elasticsearch features, is much faster (see [Performance](./performance)), and builds Entity Framework Core expressions instead of Dynamic LINQ strings. This guide lists what changes when you migrate.

## Packages

| Foundatio.Parsers | Foundatio.Lucene |
|---|---|
| `Foundatio.Parsers.LuceneQueries` | `Foundatio.Lucene` |
| `Foundatio.Parsers.ElasticQueries` | `Foundatio.Lucene.Elasticsearch` (Elastic.Clients.Elasticsearch 9.x) |
| `Foundatio.Parsers.SqlQueries` | `Foundatio.Lucene.EntityFramework` (LINQ expression trees, no Dynamic LINQ) |

## Synchronous building with an explicit async phase

Foundatio.Parsers was asynchronous from end to end because a few callbacks (field and include resolvers, mapping loads, geo lookups) could be asynchronous. Foundatio.Lucene parses and builds synchronously and runs those callbacks in a separate resolution phase first:

```csharp
// Everything is known up front: synchronous, no allocations for tasks.
var query = parser.BuildQuery("status:open");

// Async dependencies are configured: resolve them, then build.
var query = await parser.BuildQueryAsync("status:open @include:saved");
```

The synchronous methods throw `InvalidOperationException` when the parser has asynchronous dependencies that haven't been resolved, so you can't accidentally block on I/O.

| Foundatio.Parsers async hook | Foundatio.Lucene |
|---|---|
| `QueryFieldResolver` (`Task<string?>`) | `FieldResolver` (sync) or `AsyncFieldResolver` (resolution phase) |
| `IncludeResolver` (`Task<string?>`) | `Includes` (dictionary) or `IncludeResolver` (resolution phase) |
| Geo location resolver (`UseGeo`) | `GeoLocationResolver` (resolution phase) |
| Server mapping loads | `ElasticMappingResolver.EnsureLoadedAsync` (resolution phase); lookups are synchronous |
| `RuntimeFieldResolver` | `RuntimeFieldResolver` (resolution phase) |
| `NestedFilterResolver` | `NestedFilterResolver` (resolution phase) |
| Async custom visitors | Synchronous visitors; do lookups in a resolver or before building |

## Configuration

Settings that Foundatio.Parsers kept on the visitor context move to options. Configure the parser once, and pass per-request (or per-tenant, per-index) settings as an immutable `ElasticsearchQueryOptions` record, which is safe to cache and share.

| Foundatio.Parsers | Foundatio.Lucene |
|---|---|
| `new ElasticQueryParser(c => ...)` | `new ElasticsearchQueryParser(c => ...)` |
| `c.UseMappings(...)` | `c.UseMappings(...)` or `c.MappingResolver = ...` |
| `c.UseFieldMap(map)` | `c.FieldMap = new FieldMap { ... }` (case-insensitive, hierarchical) |
| `c.UseFieldResolver(resolver)` | `c.FieldResolver = ...` or `c.AsyncFieldResolver = ...` |
| `c.UseIncludes(...)` | `c.Includes = ...` or `c.IncludeResolver = ...` |
| `c.UseGeo(resolver)` | `c.GeoLocationResolver = ...` (geo queries work without it) |
| `c.UseNested()` | on by default (`c.UseNested = false` to disable) |
| `c.UseNestedFilter(...)` | `c.NestedFilterResolver = ...` |
| `c.UseRuntimeFieldResolver(...)` | `c.RuntimeFieldResolver = ...`; read `context.RuntimeFields` after building |
| `c.SetValidationOptions(...)` | `c.ValidationOptions = ...` |
| `c.SetDefaultFields(...)` | `c.DefaultFields = [...]` |
| `c.AddVisitor(visitor, priority)` | `c.AddVisitor(visitor, priority)` (same priorities: includes 0, field resolution 10, validation 30) |
| `context.UseScoring` / `UseSearchMode()` | `c.UseScoring`, `c.UseSearchMode()`, or `options.UseScoring` |
| `context.DefaultOperator` | `c.DefaultOperator` or `options.DefaultOperator` (default AND, as before) |
| `context.DefaultTimeZone` | `c.DefaultTimeZone` or `options.DefaultTimeZone` |
| `context.SetValue("StartDate", ...)` | `options.StartDate` / `options.EndDate` |

`BuildAggregationsAsync` returned an `AggregationMap`; `BuildAggregations` returns an `IDictionary<string, Aggregation>` you can assign directly to the search request.

## The syntax tree

The node model is new. Foundatio.Parsers used a binary tree of `GroupNode`s with `Left`/`Right`, and terms carried their field, prefix, and negation. Foundatio.Lucene uses explicit node types:

| Foundatio.Parsers | Foundatio.Lucene |
|---|---|
| `GroupNode` (implicit, `Left`/`Right`/`Operator`) | `BooleanQueryNode` with `Clauses`, each with an `Occur` (`Must`, `Should`, `MustNot`) |
| `GroupNode` with `HasParens` | `GroupNode` |
| `GroupNode` or `TermNode` with `Field` | `FieldQueryNode` wrapping the value |
| `TermNode` with `IsQuotedTerm` / `IsRegexTerm` | `PhraseNode` / `RegexNode` |
| `TermRangeNode` | `RangeNode` |
| `Prefix`, `IsNegated` | the clause's `Occur` (and `Modifier`, which records what was written) |
| `Boost`, `Proximity` (strings) | `BoostText`/`ProximityText` (raw) plus typed `Boost`, `FuzzyDistance`, `Slop` |
| `Data`, `Clone()` | `Data`, `Clone()` |
| `Parent` | not stored; visitors track their own path |

Visitors derive from `QueryVisitor` and override synchronous `Visit` methods, returning the node (or a replacement):

```csharp
public class LowercaseTermsVisitor : QueryVisitor
{
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        node.Term = node.Term.ToLowerInvariant();
        return node;
    }
}
```

`GenerateQueryVisitor` becomes `QueryStringBuilder.ToQueryString`, `DebugQueryVisitor` becomes `node.ToDebugString()`, and `InvertQueryVisitor`, `RemoveFieldsQueryVisitor`, `CleanupQueryVisitor`, and `GetReferencedFields()` are available with the same purpose.

To override the Elasticsearch query for a node, call `node.SetQuery(query)` from a visitor, as before.

## Behavior changes

Most queries behave exactly as before. These are the intentional differences, most of them fixes:

| Area | Foundatio.Parsers | Foundatio.Lucene |
|---|---|---|
| Operator precedence | none; `a AND b OR c` meant `a AND (b OR c)` | NOT, then AND, then OR; `a AND b OR c` means `(a AND b) OR c` |
| `-x` next to an explicit OR | `a OR -b` meant a or not b | `-` always prohibits (Lucene): `a OR -b` means a and not b; write `a OR NOT b` for the alternative |
| `*:*` | an `exists` query on the field `*` | match all |
| Field groups | the field applied only to the group's direct children, so `f:(>=1 AND <10)` silently dropped `<10` | the field applies to everything in the group |
| Invalid input | some errors produced empty or partial queries | every syntax error is reported, with its position |
| Unknown aggregation types | silently produced nothing | validation error |
| Values that don't fit the field type (`number:abc`) | sent to Elasticsearch, which rejected the request | validation error |
| `^` with a number on a date range | a time zone named `2` | validation error (on date ranges `^` is the time zone) |
| Terms order from sub-aggregations | reverse source order | source order |
| `InvertQueryVisitor` | inverted clauses one at a time depending on where scope fields appeared | negates everything outside the scope fields as one unit, which is the exact complement within the scope |
| Includes inside aggregation groups | `@include:x` could be expanded as a saved query | inside an aggregation group `@include` is always the terms/top hits modifier |
| `FieldMap` | case-sensitive | case-insensitive |

The syntax itself is a superset: everything Foundatio.Parsers accepted is accepted, and a few things it rejected now work, such as unquoted times in values (`time:10:30:00`) and escaping any character.

## SqlQueries to Entity Framework

`SqlQueryParser` produced Dynamic LINQ text; `EntityFrameworkQueryParser` builds expression trees, so there's no Dynamic LINQ dependency, no `FTS.Contains` function to map, and no way for query values to inject expressions.

| Foundatio.Parsers.SqlQueries | Foundatio.Lucene.EntityFramework |
|---|---|
| `new SqlQueryParser(c => ...)` | `new EntityFrameworkQueryParser(c => ...)` |
| `parser.GetContext(db.Employees.EntityType)` | not needed: `db.Employees.Where(query, parser)` reads the model, or use `c.UseModel(db.Model)` / `options.Model` |
| `await parser.ToDynamicLinqAsync(query, context)` + `Where(parsingConfig, text)` | `query.Where(text, parser)`, `parser.BuildFilter<T>(text)`, or `BuildFilterAsync` for async resolvers |
| `c.SetDefaultFields(fields, SqlSearchOperator.Contains)` | `c.SetDefaultFields(fields, SearchOperator.Contains)` |
| `c.SetFullTextFields([...])` | `c.AddFullTextFields(...)` |
| `c.SetSearchTokenizer(...)` | `c.UseSearchTokenizer(...)` |
| `c.SetDateTimeParser(...)` / `SetDateOnlyParser(...)` | `c.SetDefaultTimeZone(...)`, `c.SetDateTimeStorageTimeZone(...)`, `c.TimeProvider` |
| `c.SetFieldDepth(n)` | `c.SetMaxFieldDepth(n)` |
| `c.UseEntityTypePropertyFilter` / `NavigationFilter` / `SkipNavigationFilter` | same names |
| custom `EntityFieldInfo`s on the context + a visitor calling `node.SetQuery("...")` | `EntityFrameworkQueryOptions.AdditionalFields` + `c.UseCustomFieldExpressionBuilder(...)`, or `node.SetFilterExpression(lambda)` from a visitor |
| no sorting | `query.OrderBy(sort, parser)` / `parser.BuildSort<T>(sort)` |

Behavior differences, in addition to the ones above:

- Unknown fields and values that don't fit the field type (`salary:abc`) are validation errors instead of passing through or producing empty output.
- Field groups (`name:(a OR b)`), open-ended ranges (`salary:[* TO 5]`), and date rounding (`now/d`) work, and dates round like Elasticsearch: `created:>2024-01-01` starts on January 2.
- Terms on full-text fields use `CONTAINS` whether or not they have a field. `Contains` default-field searches on full-text fields use the prefix term `"john*"` instead of `"*john*"`, and advanced wildcards on full-text fields fall back to `LIKE` instead of being rejected.
- Field paths may revisit an entity type (`manager.manager.name`), bounded by `MaxFieldDepth`, and field metadata is cached per parser rather than globally.

See [Entity Framework](./entity-framework) for details.

## Validation

`QueryValidator` methods are synchronous (`ValidateQuery`, `ValidateSort`, `ValidateAggregations`), and the providers have their own `Validate*` methods that include mapping-based field resolution. `QueryValidationOptions` has the same properties (`AllowedFields`, `RestrictedFields`, `AllowLeadingWildcards`, `AllowUnresolvedFields`, `AllowUnresolvedIncludes`, `AllowedOperations`, `RestrictedOperations`, `AllowedMaxNodeDepth`, `ShouldThrow`) plus `AllowedMaxSortFields`, `MaxIncludeDepth`, and `MaxIncludeExpansions`. Allowed and restricted fields also cover sub-fields, so restricting `secret` restricts `secret.keyword`. Building a query always enforces validation.
