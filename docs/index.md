---
layout: home

hero:
  name: Foundatio Lucene
  text: Dynamic Lucene Queries for Apps
  tagline: Enable powerful user-driven search queries in your .NET applications with Entity Framework and Elasticsearch support
  image:
    src: https://raw.githubusercontent.com/FoundatioFx/Foundatio/main/media/foundatio-icon.png
    alt: Foundatio Lucene
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
    details: Terms, phrases, fields and field groups, ranges, boolean logic with clear precedence, wildcards, fuzzy, regex, date math, and saved-query includes.
  - icon: ⚡
    title: Fast
    details: 36-45× faster parsing and 7-11× faster Elasticsearch query building than Foundatio.Parsers, with a fraction of the allocations.
  - icon: 🔎
    title: Elasticsearch
    details: Mapping-aware Query DSL, aggregations, and sorts, including nested fields, geo, runtime fields, and time zones.
  - icon: 🗃️
    title: Entity Framework Core
    details: Queries and sorts become LINQ expressions that run on the database, restricted to the fields your model exposes.
  - icon: 📊
    title: Sorts and Aggregations
    details: The same syntax describes sorts (-created +title) and aggregations (terms:(status~10 max:created)).
  - icon: 🧭
    title: Sync Core, Explicit Async
    details: Parsing and building are synchronous; includes, field lookups, and mapping loads resolve in a separate async phase.
  - icon: 🛡️
    title: Safe for User Input
    details: Bounded nesting and include expansion, and field and operation rules enforced on every build.
  - icon: 🔧
    title: Visitors
    details: Transform, validate, invert, or analyze queries, and turn trees back into query text.
---

## Quick Example

```csharp
using Foundatio.Lucene;

var result = LuceneQuery.Parse("title:hello AND (status:open OR status:regressed)");
if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.Line}:{error.Column} {error.Message}");
}
```

### Elasticsearch

```csharp
using Foundatio.Lucene.Elasticsearch;

var parser = new ElasticsearchQueryParser(c => c.UseMappings(ElasticMappingResolver.Create(client, "events")));

var search = await parser.BuildSearchAsync(
    query: "type:error created:[now-7d TO now]",
    aggregations: "terms:(status~10 max:created)",
    sort: "-created");

var response = await client.SearchAsync<Event>(s => s.Indices("events").Apply(search));
```

### Entity Framework

```csharp
using Foundatio.Lucene.EntityFramework;

var parser = new EntityFrameworkQueryParser();
var employees = await db.Employees
    .Where("name:john AND salary:[50000 TO *]", parser)
    .OrderBy("-salary", parser)
    .ToListAsync();
```
