[![Build status](https://github.com/FoundatioFx/Foundatio.Lucene/actions/workflows/build.yml/badge.svg)](https://github.com/FoundatioFx/Foundatio.Lucene/actions)
[![NuGet Version](https://img.shields.io/nuget/v/Foundatio.Lucene.svg?style=flat)](https://www.nuget.org/packages/Foundatio.Lucene/)
[![feedz.io](https://img.shields.io/endpoint?url=https%3A%2F%2Ff.feedz.io%2Ffoundatio%2Ffoundatio%2Fshield%2FFoundatio.Lucene%2Flatest)](https://f.feedz.io/foundatio/foundatio/packages/Foundatio.Lucene/latest/download)
[![Discord](https://img.shields.io/discord/715744504891703319?logo=discord)](https://discord.gg/6HxgFCx)

# Foundatio Lucene

A library for adding dynamic Lucene-style query capabilities to your .NET applications. Enable your users to write powerful search queries using familiar Lucene syntax, with support for Entity Framework and Elasticsearch.

This project is a modern replacement for [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers).

## ✨ Why Choose Foundatio Lucene?

- 🔍 **Full Lucene syntax** - terms, phrases, fields, field groups, ranges, boolean logic with clear precedence, wildcards, fuzzy, regex, date math, includes
- ⚡ **Fast** - 36-45× faster parsing and 7-11× faster Elasticsearch query building than Foundatio.Parsers, with a fraction of the allocations ([benchmarks](https://lucene.foundatio.dev/guide/performance.html))
- 🔎 **Elasticsearch** - mapping-aware Query DSL, aggregations, and sorts (nested, geo, runtime fields, time zones) with the official 9.x client
- 🗄️ **Entity Framework Core** - translate queries and sorts to LINQ expressions that run on the server
- 📊 **Sort and aggregation expressions** - `-created +title`, `terms:(status~10 max:created) date:created~1d`
- 🧭 **Synchronous core, explicit async phase** - no async overhead unless a resolver actually needs I/O
- 🗺️ **Field aliasing and resolution** - friendly names, per-tenant maps, sync or async resolvers
- 🛡️ **Safe for user input** - bounded depth and include expansion, field and operation allowlists enforced on every build
- 🧩 **Visitors** - transform, validate, invert, or analyze queries
- 🔄 **Round-trip** - turn a modified tree back into query text

## 🚀 Quick Example

```csharp
using Foundatio.Lucene;
using Foundatio.Lucene.Elasticsearch;
using Foundatio.Lucene.EntityFramework;

// Parse a query (never throws for bad input; errors carry positions)
var result = LuceneQuery.Parse("title:hello AND (status:open OR status:regressed) -tags:ignore");

// Elasticsearch: query, aggregations, and sort from one call
var esParser = new ElasticsearchQueryParser(c => c.UseMappings(ElasticMappingResolver.Create(client, "events")));
var search = await esParser.BuildSearchAsync(
    query: "type:error created:[now-7d TO now]",
    aggregations: "terms:(status~10 max:created)",
    sort: "-created");
var response = await client.SearchAsync<Event>(s => s.Indices("events").Apply(search));

// Entity Framework Core: a LINQ expression that runs on the database
var efParser = new EntityFrameworkQueryParser();
Expression<Func<Employee, bool>> filter = efParser.BuildFilter<Employee>("name:john AND salary:[50000 TO *]");
var employees = await db.Employees.Where(filter).ToListAsync();
```

## 📚 Learn More

👉 **[Complete Documentation](https://lucene.foundatio.dev/)**

Key topics:

- [Getting Started](https://lucene.foundatio.dev/guide/getting-started.html) - Installation and basic usage
- [Query Syntax](https://lucene.foundatio.dev/guide/query-syntax.html) - Full syntax and semantics reference
- [Elasticsearch](https://lucene.foundatio.dev/guide/elasticsearch.html) - Query DSL, mappings, nested, geo, runtime fields
- [Entity Framework](https://lucene.foundatio.dev/guide/entity-framework.html) - EF Core integration
- [Sorting and Aggregations](https://lucene.foundatio.dev/guide/sorting-and-aggregations.html) - Sort and aggregation expressions
- [Validation](https://lucene.foundatio.dev/guide/validation.html) and [Security](https://lucene.foundatio.dev/guide/security.html) - Restricting what users can query
- [Migrating from Foundatio.Parsers](https://lucene.foundatio.dev/guide/migrating-from-parsers.html) - What changes when you switch

## 📦 CI Packages (Feedz)

Want the latest CI build before it hits NuGet? Add the Feedz source (read-only public) and install the pre-release version:

```bash
dotnet nuget add source https://f.feedz.io/foundatio/foundatio/nuget -n foundatio-feedz
dotnet add package Foundatio.Lucene --prerelease
```

Or add to your `NuGet.config`:

```xml
<configuration>
    <packageSources>
        <add key="foundatio-feedz" value="https://f.feedz.io/foundatio/foundatio/nuget" />
    </packageSources>
    <packageSourceMapping>
        <packageSource key="foundatio-feedz">
            <package pattern="Foundatio.*" />
        </packageSource>
    </packageSourceMapping>
</configuration>
```

CI builds are published with pre-release version tags (e.g. `1.0.0-preview.12345+sha.abcdef`). Use them to try new features early—avoid in production unless you understand the changes.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request. See our [documentation](https://lucene.foundatio.dev/) for development guidelines.

## 🔗 Related Projects

- [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers) - The predecessor to this library
- [Foundatio](https://github.com/FoundatioFx/Foundatio) - Pluggable foundation blocks for building distributed apps

## 📄 License

Apache 2.0
