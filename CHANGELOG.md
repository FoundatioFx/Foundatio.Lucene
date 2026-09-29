# Changelog

All notable changes to Foundatio.Lucene are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0] — Unreleased

1.0 brings the feature set of [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers) — Elasticsearch
queries, aggregations, and sorts, and SQL filters — to Foundatio.Lucene with a synchronous core, an explicit
asynchronous resolution phase, precisely specified semantics, and much better performance. It is a breaking release;
see [Migrating from Foundatio.Parsers](https://lucene.foundatio.dev/guide/migrating-from-parsers.html) for the
changes from Foundatio.Parsers.

### Parsing

- New context-aware lexer and recursive-descent parser. Parsing is linear, never throws for malformed input, and
  reports every problem with position, line, and column: unexpected tokens, dangling operators, unmatched
  parentheses and brackets, unterminated phrases and regular expressions, trailing escapes, operators after a
  field's colon, empty groups, and duplicate modifiers. Nothing is silently dropped.
- Precisely defined semantics: precedence NOT > AND > OR; default operator **AND** (like Foundatio.Parsers);
  `+`/`-` follow Lucene; `NOT` excludes like `-` except as an alternative of an explicit OR (`a OR NOT b`).
- `TO` is a keyword only inside ranges; `..` range delimiter; negative numbers in ranges and comparisons; unquoted
  times in values (`time:10:30:00`) while `field1:2` still works; a spaced `+`, `-`, or `!` is literal text.
- Modifiers keep their raw text (`BoostText`, `ProximityText`), so `^"America/Chicago"`, `~1d`, and `~5km` carry
  their meaning to providers, with typed `Boost`, `FuzzyDistance`, and `Slop` accessors.
- Escapes are processed exactly once (`TermNode.Term` keeps them, `UnescapedTerm` removes them).
- `LuceneParserOptions` (`DefaultOperator`, `MaxDepth`, default 100) replaces loose parameters.
- `QueryStringBuilder` preserves meaning for any default operator and keeps the operators users wrote; fuzzed with
  millions of random inputs.
- Culture-invariant modifier parsing.

### Syntax tree and visitors

- Nodes have a `Data` metadata bag and a deep `Clone()`. `BooleanClause` records both its semantic `Occur` and
  the syntax the user wrote. `IFieldNode`, `IBoostable`, and `IProximityModifiable` describe node capabilities.
- `QueryVisitor` and `ChainedQueryVisitor` live in `Foundatio.Lucene.Visitors`; the chain is thread-safe.
- `IQueryVisitorContext` has typed settings (parser options, default fields, field map and resolver, includes,
  validation options and result, time provider).
- New `InvertQueryVisitor`, `RemoveFieldsQueryVisitor`, and `CleanupQueryVisitor`. Inversion produces the exact
  complement of a query within its scope fields.
- `node.ToDebugString()` describes a tree compactly; `node.Walk()` visits it iteratively.
- Include expansion is bounded (`MaxIncludeDepth`, `MaxIncludeExpansions`), parses each include once, and keeps
  prefixes such as `-@include:x` applying to the whole expansion.
- Field resolution records the original field name on the node.

### Shared engine

- `QueryParserBase<TContext>` and `QueryParserConfiguration` give both providers the same pipeline, option
  precedence (request > registered > configuration), and behavior.
- Asynchronous dependencies — `IncludeResolver`, `AsyncFieldResolver`, and provider lookups — run in one explicit
  resolution phase in the `Async` methods; parsing and building are synchronous and the synchronous methods refuse
  to run with unresolved asynchronous dependencies.
- Caller documents and options are never modified.
- Sort expressions (`SortExpression`, `SortField`) and aggregation expressions (`AggregationExpressionParser`,
  `AggregationExpression`) with the Foundatio.Parsers syntax.

### Validation

- Validation is enforced on every build.
- `AllowedFields` covers sub-fields; `RestrictedFields` is checked against written and resolved names and
  sub-fields; wildcard field names are rejected when field rules are configured; default fields are validated for
  bare terms.
- New `AllowUnresolvedFields` (default true), `AllowUnresolvedIncludes` (default false), `AllowedMaxSortFields`,
  `MaxIncludeDepth`, `MaxIncludeExpansions`; `QueryOperations` names for query operations.
- `QueryValidator.ValidateSort` and `ValidateAggregations`; the `Validate` methods no longer modify the options
  passed to them.

### Date math

- Time-zone-correct evaluation: calendar units on the local wall clock, hours/minutes/seconds as durations, the
  offset in effect on each date, and correct handling of skipped and repeated daylight-saving times.
- Elasticsearch rounding of dates with missing components for upper bounds; overflow is an error; culture-invariant
  parsing; overloads that take an explicit `now` and time zone.
- `DateMathEvaluatorVisitor` reads the clock once per query and can be limited to date fields.

### Elasticsearch

- Mapping-aware query building ported from Foundatio.Parsers: match vs term vs phrase vs query_string vs
  prefix/wildcard/regexp/fuzzy by field type, typed values, keyword and sort sub-fields, default-field fan-out,
  and validation of values that don't fit the field type.
- `ElasticMappingResolver` (ported): server and code mappings, merged across the indices behind an alias or
  pattern, asynchronous loading with synchronous lock-free lookups, throttled refresh for unmapped fields.
- Nested queries with same-document correlation, explicit nested groups, nested sorts and aggregations, and
  `NestedFilterResolver`.
- Geo distance and bounding box queries with an asynchronous `GeoLocationResolver`.
- Date ranges with default and per-range time zones.
- Runtime fields via `RuntimeFieldResolver`.
- Aggregations (`BuildAggregations`) and sorts (`BuildSort`) for every Foundatio.Parsers aggregation type and
  modifier.
- `BuildSearch`/`BuildSearchAsync` build a query, aggregations, and sort together and apply them to a search request.
- `node.SetQuery(query)` lets visitors supply custom queries.

### Entity Framework Core

- Rebuilt on the shared engine: field maps, includes, date math, validation, and the async resolution phase
  (`BuildFilterAsync`, `WhereAsync`, `ValidateQueryAsync`, `BuildSortAsync`).
- Lucene boolean semantics matching the Elasticsearch provider.
- Sorting: `query.OrderBy(sort, parser)`, `BuildSort<T>`, and `ValidateSort<T>`.
- Queries are limited to fields discovered from the model (after property and navigation filters) and registered
  fields; anything else is a validation error, including paths through excluded navigations.
- Values are parsed with the invariant culture for every supported type (numbers, enums, `bool`, `Guid`, `char`,
  `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, nullable variants); values that don't fit the
  field are validation errors naming the field and value instead of matching nothing.
- Dates round like Elasticsearch (`[2024-01-01 TO 2024-02-01}`, `<=2024-01-31`, `>2024-01-31`), date math is
  evaluated with the configured clock and time zone, and `DateTime` columns are compared in a configurable storage
  time zone.
- Wildcards: `jo*` is `StartsWith`, `*oh*` is `Contains`, other patterns use `LIKE` with escaping; `\*` and `\?` are
  literal.
- Values are SQL parameters, so queries share compiled queries and plans.
- Full-text search conditions are always a single quoted phrase, so user text can't add full-text operators.
- `_exists_` on collections requires an element; primitive collections, owned types, complex properties, skip
  navigations, and shadow properties are supported.
- Regular expressions are rejected unless `UseRegex` supplies a translation; fuzzy, proximity, and boosts are errors.
- Field metadata is discovered lazily and cached per parser (not in a static cache shared by all parsers).
- `AddLuceneQuery` no longer creates an EF Core internal service provider per parser.
- Custom (dynamic/EAV) fields via `AdditionalFields` and `CustomFieldExpressionBuilder` with `BuildDefault`, and
  per-node overrides with `node.SetFilterExpression`.

### Performance

- Parsing is 36-45× faster than Foundatio.Parsers and allocates about 2% as much; Elasticsearch query, aggregation,
  and sort building is 4-11× faster with a fraction of the allocations. Benchmarks against Foundatio.Parsers are
  in `benchmarks/`.

### Removed (breaking)

- `MultiTermNode` and split-on-whitespace parsing.
- `TenantOptionsCache`, `EntityOptionsCache`, `TenantEntityOptionsCache`, and `VisitorPool`; cache the immutable
  option records instead.
- The `QueryOperator` enum (use `BooleanOperator`).
- `LuceneLexer` and `LuceneParser` are internal; use `LuceneQuery.Parse` and `LuceneQuery.Tokenize`.
- The context extension methods (`SetFieldMap`, `GetValidationResult`, ...) in favor of context properties.

### Infrastructure

- Dependabot updates are grouped and monthly, auto-merge waits for CI and skips major updates, and `main` is built
  on a schedule.
- Public API tracking with `Microsoft.CodeAnalysis.PublicApiAnalyzers`.
- Documentation rewritten, with guides for migration, security, performance, sorting and aggregations, and
  Elasticsearch mappings; the docs build fails on dead links and runs on pull requests.
