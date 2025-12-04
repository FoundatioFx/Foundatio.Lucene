[![Build status](https://github.com/FoundatioFx/Foundatio.Lucene/actions/workflows/build.yml/badge.svg)](https://github.com/FoundatioFx/Foundatio.Lucene/actions)
[![NuGet Version](https://img.shields.io/nuget/v/Foundatio.Lucene.svg?style=flat)](https://www.nuget.org/packages/Foundatio.Lucene/)
[![feedz.io](https://img.shields.io/badge/endpoint.svg?url=https%3A%2F%2Ff.feedz.io%2Ffoundatio%2Ffoundatio%2Fshield%2FFoundatio.Lucene%2Flatest)](https://f.feedz.io/foundatio/foundatio/packages/Foundatio.Lucene/latest/download)
[![Discord](https://img.shields.io/discord/715744504891703319?logo=discord)](https://discord.gg/6HxgFCx)

# Foundatio.Lucene

A library for adding dynamic Lucene-style query capabilities to your .NET applications. Enable your users to write powerful search queries using familiar Lucene syntax, with support for Entity Framework and Elasticsearch.

This project is a modern replacement for [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers).

## ✨ Why Choose Foundatio Lucene?

- 🔍 **Full Lucene Syntax** - Terms, phrases, fields, ranges, boolean operators, wildcards, regex
- ⚡ **Entity Framework Integration** - Convert Lucene queries directly to LINQ expressions
- 🔎 **Elasticsearch Support** - Generate Elasticsearch Query DSL using the official .NET 9.x client
- 🧩 **Visitor Pattern** - Transform, validate, or analyze queries with composable visitors
- 🗺️ **Field Aliasing** - Map user-friendly field names to your actual data model
- ✅ **Query Validation** - Restrict allowed fields, operators, and patterns
- 🔄 **Round-Trip Capable** - Parse queries to AST and convert back to query strings
- 🛡️ **Error Recovery** - Resilient parser returns partial AST with detailed error information

## 🚀 Quick Example

```csharp
using Foundatio.Lucene;
using Foundatio.Lucene.EntityFramework;

// Parse a Lucene query
var result = LuceneQuery.Parse("title:hello AND status:active");

// Or build LINQ expressions for Entity Framework
var parser = new EntityFrameworkQueryParser();
Expression<Func<Employee, bool>> filter = parser.BuildFilter<Employee>(
    "name:john AND salary:[50000 TO *]"
);

var employees = await context.Employees.Where(filter).ToListAsync();
```

## 📚 Learn More

👉 **[Complete Documentation](https://lucene.foundatio.dev/)**

Key topics:

- [Getting Started](https://lucene.foundatio.dev/guide/getting-started.html) - Installation and basic usage
- [Query Syntax](https://lucene.foundatio.dev/guide/query-syntax.html) - Full syntax reference
- [Entity Framework](https://lucene.foundatio.dev/guide/entity-framework.html) - EF Core integration
- [Elasticsearch](https://lucene.foundatio.dev/guide/elasticsearch.html) - Elasticsearch Query DSL generation
- [Visitors](https://lucene.foundatio.dev/guide/visitors.html) - AST transformation patterns
- [Validation](https://lucene.foundatio.dev/guide/validation.html) - Query validation options

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

CI builds are published with pre-release version tags (e.g. `1.0.0-alpha.12345+sha.abcdef`). Use them to try new features early—avoid in production unless you understand the changes.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request. See our [documentation](https://lucene.foundatio.dev/) for development guidelines.

## 🔗 Related Projects

- [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers) - The predecessor to this library
- [Foundatio](https://github.com/FoundatioFx/Foundatio) - Pluggable foundation blocks for building distributed apps

## 📄 License

Apache 2.0
