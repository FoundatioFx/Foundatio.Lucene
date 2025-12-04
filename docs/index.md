---
layout: home

hero:
  name: Foundatio Lucene
  text: Dynamic Lucene Queries for Apps
  tagline: Enable powerful user-driven search queries in your .NET applications with Entity Framework and Elasticsearch support
  image:
    src: https://raw.githubusercontent.com/FoundatioFx/Foundatio/main/media/foundatio-icon.png
    alt: Foundatio.Lucene
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/FoundatioFx/Foundatio.Lucene

features:
  - icon: 🔍
    title: Full Lucene Syntax
    details: Support for terms, phrases, fields, ranges, boolean operators, wildcards, regex, and more.
  - icon: 🗃️
    title: Entity Framework Integration
    details: Convert Lucene queries directly to LINQ expressions for EF Core database queries.
  - icon: 🔎
    title: Elasticsearch Support
    details: Generate Elasticsearch Query DSL from Lucene syntax using the official .NET client.
  - icon: 🔄
    title: Round-Trip Capable
    details: Parse queries to an AST and convert back to query strings with full fidelity.
  - icon: 🛡️
    title: Query Validation
    details: Restrict allowed fields, operators, and patterns with comprehensive validation.
  - icon: 📅
    title: Date Math Support
    details: Elasticsearch-style date math expressions like `now-1d` and `2024-01-01||+1M/d`.
  - icon: 🔧
    title: Visitor Pattern
    details: Transform, validate, or analyze queries with composable visitors.
  - icon: 🏷️
    title: Field Aliasing
    details: Map user-friendly field names to your actual data model for security and usability.
---

## Quick Example

```csharp
using Foundatio.Lucene;

// Parse a user query
var result = LuceneQuery.Parse("title:hello AND status:active");

if (result.IsSuccess)
{
    var document = result.Document; // QueryDocument (root AST node)
}
```

### Entity Framework

```csharp
using Foundatio.Lucene.EntityFramework;

var parser = new EntityFrameworkQueryParser();
var filter = parser.BuildFilter<Employee>("name:john AND salary:[50000 TO *]");
var results = await context.Employees.Where(filter).ToListAsync();
```

### Elasticsearch

```csharp
using Foundatio.Lucene.Elasticsearch;

var parser = new ElasticsearchQueryParser(config =>
{
    config.UseScoring = true;
    config.DefaultFields = ["title", "content"];
});
var query = parser.BuildQuery("author:john AND status:active");
```
