# Getting Started

## Installation

```bash
# Parsing, visitors, validation
dotnet add package Foundatio.Lucene

# Elasticsearch queries, aggregations, and sorts
dotnet add package Foundatio.Lucene.Elasticsearch

# Entity Framework Core filters and sorts
dotnet add package Foundatio.Lucene.EntityFramework
```

The packages target .NET 8 and .NET 10.

## Parsing

```csharp
using Foundatio.Lucene;

var result = LuceneQuery.Parse("title:hello AND (status:open OR status:regressed)");

if (result.IsSuccess)
{
    var document = result.Document;
}
else
{
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.Line}:{error.Column} {error.Message}");
}
```

Parsing never throws for malformed input. The document contains everything that could be parsed, and each error has a position you can use to highlight the problem.

Clauses written next to each other are AND-ed by default. For a search box where `apple banana` should match either word, use OR:

```csharp
var result = LuceneQuery.Parse("apple banana", BooleanOperator.Or);
```

See [Query Syntax](./query-syntax) for the complete syntax and how operators combine.

## Back to text

```csharp
var document = LuceneQuery.Parse("title:(a OR b) -status:closed").Document;
string text = QueryStringBuilder.ToQueryString(document); // title:(a OR b) -status:closed
```

## Elasticsearch

```csharp
using Foundatio.Lucene.Elasticsearch;

var parser = new ElasticsearchQueryParser(c =>
{
    c.UseMappings(ElasticMappingResolver.Create(client, "events"));
    c.DefaultFields = ["message"];
});

var query = await parser.BuildQueryAsync("type:error AND created:[now-7d TO now]");
var response = await client.SearchAsync<Event>(s => s.Indices("events").Query(query));
```

Aggregations and sorts use the same syntax:

```csharp
var search = await parser.BuildSearchAsync(
    query: "type:error",
    aggregations: "terms:(status~10 max:created) date:created~1d",
    sort: "-created");

var response = await client.SearchAsync<Event>(s => s.Indices("events").Apply(search));
```

See [Elasticsearch](./elasticsearch).

## Entity Framework Core

```csharp
using Foundatio.Lucene.EntityFramework;

var parser = new EntityFrameworkQueryParser(c => c.DefaultFields = ["Name"]);

var employees = await db.Employees
    .Where("name:john AND salary:[50000 TO *]", parser)
    .OrderBy("-salary name", parser)
    .ToListAsync();
```

See [Entity Framework](./entity-framework).

## Field aliases

Let users query with friendly names:

```csharp
var parser = new ElasticsearchQueryParser(c => c.FieldMap = new FieldMap
{
    { "user", "account.username" },
    { "created", "metadata.timestamp" }
});

var query = parser.BuildQuery("user:john created:>now-1d");
```

See [Field Mapping](./field-mapping).

## Restricting what users can query

```csharp
var validation = new QueryValidationOptions { AllowLeadingWildcards = false };
validation.AllowedFields.Add("title");
validation.AllowedFields.Add("status");

var parser = new ElasticsearchQueryParser(c => c.ValidationOptions = validation);

var result = parser.TryBuildQuery("secret:x");
// result.IsSuccess == false
// result.ErrorMessage: "Invalid query: Query uses field(s) (secret) that are not allowed to be used."
```

See [Validation](./validation) and [Security](./security).

## Next steps

- [Query Syntax](./query-syntax)
- [Sorting and Aggregations](./sorting-and-aggregations)
- [Configuration](./configuration)
- [Visitors](./visitors)
- [Migrating from Foundatio.Parsers](./migrating-from-parsers)
