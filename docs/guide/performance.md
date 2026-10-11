# Performance

Foundatio.Lucene is designed for high-throughput services that build a query for every request:

- **A hand-written lexer and parser** that work on zero-copy slices of the query text. Parsing is linear in the length of the query and allocates only the nodes it creates.
- **Synchronous building.** No tasks or state machines are allocated unless a resolver actually needs I/O, and then only in the resolution phase.
- **Stateless, shared visitors and builders.** A parser is created once and serves every request; per-request settings are small immutable records.
- **Bounded work.** Nesting depth, include depth, and include fan-out are limited, so a hostile query can't exhaust the stack or blow up into millions of clauses.

## Compared with Foundatio.Parsers

The benchmarks in `benchmarks/` run identical scenarios (`benchmarks/Shared/BenchmarkScenarios.cs`) against Foundatio.Lucene and Foundatio.Parsers 8.0.2. Foundatio.Parsers runs in its own project because it uses the 8.x Elasticsearch client, which can't be loaded alongside the 9.x client.

| Scenario | Foundatio.Parsers | Foundatio.Lucene | Speedup |
|---|---:|---:|---:|
| Parse `status:open` | 5.5 µs / 26.0 KB | 0.13 µs / 0.7 KB | 43× |
| Parse a typical query | 29.3 µs / 160.6 KB | 0.81 µs / 3.2 KB | 36× |
| Parse a complex query | 60.4 µs / 282.8 KB | 1.36 µs / 5.3 KB | 45× |
| Query to text (complex) | 1,382 µs / 10.1 MB | 0.29 µs / 0.3 KB | 4,800× |
| Elasticsearch query, simple | 6.3 µs / 33 KB | 0.86 µs / 4.7 KB | 7× |
| Elasticsearch query, typical | 35.8 µs / 184 KB | 4.6 µs / 19 KB | 8× |
| Elasticsearch query, complex | 61.2 µs / 315 KB | 6.1 µs / 26 KB | 10× |
| Elasticsearch query, nested | 12.1 µs / 64 KB | 2.7 µs / 12 KB | 4× |
| Elasticsearch aggregations | 34.9 µs / 181 KB | 3.3 µs / 17 KB | 11× |
| Elasticsearch sort | 16.4 µs / 93 KB | 1.6 µs / 8 KB | 10× |

Measured with BenchmarkDotNet on .NET 10 (x64). Times are means; sizes are allocations per operation.

## Running the benchmarks

```bash
dotnet run -c Release --project benchmarks/Foundatio.Lucene.Benchmarks -- --filter '*'
dotnet run -c Release --project benchmarks/Foundatio.Parsers.Benchmarks -- --filter '*'
```

## Tips

- **Create parsers once.** Parsers are thread-safe. Creating one per request re-runs configuration and, for Entity Framework, model discovery.
- **Cache options, not queries.** `ElasticsearchQueryOptions` and `EntityFrameworkQueryOptions` are immutable records that can be cached per tenant or index.
- **Reuse parsed documents.** `BuildQuery(QueryDocument)` never modifies the document, so a parsed saved query can be cached and built many times.
- **Prefer the synchronous methods** when you have no asynchronous resolvers; they do no async work at all.
- **Keep mappings warm.** Load Elasticsearch mappings once at startup (`EnsureLoadedAsync`) so request-time lookups never wait on the server.
