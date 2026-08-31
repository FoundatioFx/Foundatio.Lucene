# Changelog

All notable changes to Foundatio.Lucene are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased] — 1.0 hardening

This release hardens the library for a stable 1.0, with a focus on high-performance per-scope
configuration swapping that works equally well with Entity Framework (SQL) and Elasticsearch.

### Added

- **Cross-engine parity gate.** A new `Foundatio.Lucene.Parity.Tests` suite seeds identical data into
  SQL Server and Elasticsearch (via Testcontainers) and asserts that the same Lucene query returns
  the same result set on both engines across the supported construct set (ranges, comparisons,
  equality, boolean composition, `_exists_`, `NOT`, match-all).
- **Entity Framework field maps, `@includes`, and date math.** The EF parser now runs the same
  pre-build visitor pipeline as the Elasticsearch parser, so per-scope `FieldMap`, `@include`
  expansion, and date math (`now-7d`, `now/d`, `2024-01-01||+1M`) behave identically on SQL. These
  were previously accepted on the options API but silently ignored.
- **Configurable `TimeProvider` on the EF parser** (`config.SetTimeProvider(...)`) controlling `now`
  in date math.
- **Parser depth bound** (`LuceneParser.MaxDepth`, default 100): deeply nested input now records a
  `MaxDepthExceeded` `ParseError` instead of risking a `StackOverflowException`.
- **`ParseError.Code`** (`QueryErrorCode`): parse errors are now classified (depth, unmatched
  bracket, invalid range, unexpected character).
- **`ConfigSwapBenchmarks`**: measures per-query allocations when swapping a different scope's field
  map / default fields against a shared parser, for both EF and Elasticsearch.
- Error-recovery, concurrency, and pipeline test coverage; root `LICENSE` (Apache-2.0).

### Changed

- **Capability model (breaking).** The EF builder now throws `QueryBuildException`
  (`QueryErrorCode.UnsupportedQueryType`) for fuzzy (`term~N`) and proximity/slop (`"a b"~N`)
  queries instead of silently degrading them to substring matches. Use `TryBuildFilter` for a
  `QueryResult` instead of an exception.
- **Elasticsearch scope swapping is allocation-free on the hot path.** The parser carries the
  per-scope field map and includes on the visitor context and reuses singleton visitors, instead of
  allocating a new `FieldResolverQueryVisitor`/`IncludeVisitor` per query.
- Boost (`^`) and fuzzy/slop (`~N`) parsing is now culture-invariant.
- `EntityFieldInfo` equality/hash now key on `FullName` (was the leaf `Name`), so nested fields that
  share a leaf name no longer collide.

### Removed (breaking)

- **`TenantOptionsCache` / `EntityOptionsCache` / `TenantEntityOptionsCache` / `VisitorPool`** —
  orphaned, untested infrastructure. Per-request options (`ElasticsearchQueryOptions` /
  `EntityFrameworkQueryOptions`, both immutable records) are the supported scope-swap mechanism;
  applications own caching of those option objects.
- **Elasticsearch geo configuration** (`IsGeoPointField`, `GeoLocationResolver`,
  `UseGeoFields`/`UseGeoLocationResolver`/`WithGeoPointFields`/`SetGeoPointFieldResolver`/
  `SetGeoLocationResolver`). The builder never emitted geo queries and the async resolver could not
  be awaited in the synchronous pipeline. Geo is deferred to a post-1.0 design that keeps the
  visitor pipeline synchronous (collect geo references → resolve coordinates outside the pipeline →
  annotate the AST).
- **`QueryOperator` enum** — the Elasticsearch integration now uses
  `Foundatio.Lucene.Ast.BooleanOperator` everywhere.
- The unused `EntityFieldInfo.IsMoney` property.

### Fixed

- Lexer no longer advances past the end of the buffer on a trailing backslash in a quoted string or
  regex (could throw while slicing).
- Documentation examples updated to the real **synchronous** API (no `ParseAsync`/`BuildQueryAsync`/
  `RunAsync`/`AcceptAsync`/`VisitAsync`), and to reflect the geo removal and the EF pipeline support.

### Compatibility

- `LuceneParseResult.Document` and `Errors` are now `init`-only.

### Known follow-ups

- Adopt `Microsoft.CodeAnalysis.PublicApiAnalyzers` with committed `PublicAPI.Shipped.txt` /
  `PublicAPI.Unshipped.txt` baselines to lock the public surface against accidental changes. (The
  surface itself has been cleaned for 1.0; this adds the build-time guardrail.)
