# Foundatio.LuceneQuery

A library for adding dynamic Lucene-style query capabilities to your .NET applications. Enable your users to write powerful search queries using familiar Lucene syntax, with support for Entity Framework and Elasticsearch.

This project is a modern replacement for [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers).

## Features

- **Dynamic User Queries** - Let users write powerful search queries using Lucene syntax
- **Entity Framework Integration** - Convert Lucene queries directly to LINQ expressions for EF Core
- **Elasticsearch Support** - Generate Elasticsearch Query DSL from Lucene syntax using the official .NET client
- **Full Lucene Query Syntax** - Terms, phrases, fields, ranges, boolean operators, wildcards, regex, and more
- **Date Math Expressions** - Support for Elasticsearch-style date math (`now-1d`, `2024-01-01||+1M/d`)
- **Visitor Pattern** - Transform, validate, or analyze queries with composable visitors
- **Field Aliasing** - Map user-friendly field names to your actual data model
- **Query Validation** - Restrict allowed fields, operators, and patterns
- **Round-Trip Capable** - Parse queries to AST and convert back to query strings
- **Error Recovery** - Resilient parser returns partial AST with detailed error information

## Installation

```bash
# Core parser
dotnet add package Foundatio.LuceneQuery

# Entity Framework integration (optional)
dotnet add package Foundatio.LuceneQuery.EntityFramework

# Elasticsearch integration (optional)
dotnet add package Foundatio.LuceneQuery.Elasticsearch
```

## Quick Start

### Basic Parsing

```csharp
using Foundatio.LuceneQuery;

var result = LuceneQuery.Parse("title:hello AND status:active");

if (result.IsSuccess)
{
    var document = result.Document; // QueryDocument (root AST node)
}
else
{
    // Handle errors - partial AST may still be available
    foreach (var error in result.Errors)
        Console.WriteLine($"Error at {error.Line}:{error.Column}: {error.Message}");
}
```

### Convert AST Back to Query String

```csharp
using Foundatio.LuceneQuery;

var result = LuceneQuery.Parse("title:test AND (status:active OR status:pending)");
var queryString = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "title:test AND (status:active OR status:pending)"
```

### Field Aliasing

```csharp
using Foundatio.LuceneQuery;
using Foundatio.LuceneQuery.Visitors;

var result = LuceneQuery.Parse("user:john AND created:[2020-01-01 TO 2020-12-31]");

var fieldMap = new FieldMap
{
    { "user", "account.username" },
    { "created", "metadata.timestamp" }
};

await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

var resolved = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "account.username:john AND metadata.timestamp:[2020-01-01 TO 2020-12-31]"
```

### Query Validation

```csharp
using Foundatio.LuceneQuery;

var result = LuceneQuery.Parse("*wildcard AND title:test");

var options = new QueryValidationOptions
{
    AllowLeadingWildcards = false
};
options.AllowedFields.Add("title");
options.AllowedFields.Add("status");

var validationResult = await QueryValidator.ValidateAsync(result.Document, options);

if (!validationResult.IsValid)
    Console.WriteLine(validationResult.Message);
```

### Entity Framework Integration

Enable dynamic, user-driven queries in your API endpoints:

```csharp
using Foundatio.LuceneQuery.EntityFramework;

// In your API controller or service
[HttpGet("employees")]
public async Task<IActionResult> SearchEmployees([FromQuery] string query)
{
    var parser = new EntityFrameworkQueryParser();

    // User provides: "name:john AND salary:[50000 TO *] AND department:engineering"
    Expression<Func<Employee, bool>> filter = parser.BuildFilter<Employee>(query);

    var results = await _context.Employees
        .Where(filter)
        .ToListAsync();

    return Ok(results);
}
```

With field aliasing to protect your data model:

```csharp
var parser = new EntityFrameworkQueryParser();

// Map user-friendly names to actual entity properties
var fieldMap = new FieldMap
{
    { "name", "FullName" },
    { "dept", "Department.Name" },
    { "hired", "HireDate" }
};

// User query: "name:john AND dept:engineering AND hired:[2020-01-01 TO *]"
Expression<Func<Employee, bool>> filter = parser.BuildFilter<Employee>(userQuery, fieldMap);
```

### Elasticsearch Integration

Generate Elasticsearch Query DSL from Lucene syntax:

```csharp
using Foundatio.LuceneQuery.Elasticsearch;
using Elastic.Clients.Elasticsearch;

var parser = new ElasticsearchQueryParser();

// Build an Elasticsearch Query from a Lucene query string
var query = parser.BuildQuery("title:hello AND status:active");

// Use with the Elasticsearch client
var client = new ElasticsearchClient();
var response = await client.SearchAsync<Document>(s => s
    .Index("my-index")
    .Query(query)
);
```

With configuration options:

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    // Use scoring queries (match) instead of filter queries (term)
    config.UseScoring = true;

    // Set default fields for unfielded terms
    config.DefaultFields = ["title", "content"];

    // Map user-friendly field names
    config.FieldMap = new FieldMap
    {
        { "author", "metadata.author" },
        { "created", "metadata.timestamp" }
    };

    // Configure geo field detection for geo queries
    config.IsGeoPointField = field => field == "location";

    // Configure date field detection for date ranges
    config.IsDateField = field => field.EndsWith("date") || field.EndsWith("timestamp");
    config.DefaultTimeZone = "America/Chicago";
});

var query = parser.BuildQuery("author:john AND created:[2024-01-01 TO now]");
```

#### Geo Queries

The Elasticsearch integration supports geo queries:

```csharp
// Distance query
parser.BuildQuery("location:40.7128,-74.0060~10km");

// Bounding box (min_lon,min_lat TO max_lon,max_lat)
parser.BuildQuery("location:[-74.1,40.6 TO -73.9,40.8]");
```

## Use Cases

- **Search APIs** - Let users filter data with powerful query syntax
- **Admin Dashboards** - Enable complex filtering without custom UI for each field
- **Reporting** - Allow dynamic report criteria using familiar search syntax
- **Data Export** - Let users specify exactly what data they need
- **Audit/Log Search** - Search through logs with date ranges, terms, and boolean logic

## Supported Query Syntax

| Syntax | Example | Description |
|--------|---------|-------------|
| Terms | `hello`, `hello*`, `hel?o` | Simple terms with optional wildcards |
| Phrases | `"hello world"`, `"hello world"~2` | Exact phrases with optional proximity |
| Fields | `title:test`, `user.name:john` | Field-specific queries, supports nested paths |
| Ranges | `price:[100 TO 500]`, `date:{* TO 2024-01-01}` | Inclusive `[]` or exclusive `{}` ranges |
| Boolean | `AND`, `OR`, `NOT`, `+`, `-` | Boolean operators and prefix modifiers |
| Groups | `(a OR b) AND c` | Parenthetical grouping |
| Exists | `_exists_:field`, `_missing_:field` | Field existence checks |
| Match All | `*:*` | Matches all documents |
| Regex | `/pattern/` | Regular expression patterns |
| Date Math | `now-1d`, `2024-01-01\|\|+1M/d` | Elasticsearch date math expressions |
| Includes | `@include:savedQuery` | Reference saved/named queries |

## Creating Custom Visitors

Extend `QueryNodeVisitor` to create custom transformations:

```csharp
using Foundatio.LuceneQuery.Ast;
using Foundatio.LuceneQuery.Visitors;

public class LowercaseTermVisitor : QueryNodeVisitor
{
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        node.Term = node.Term?.ToLowerInvariant();
        return Task.FromResult<QueryNode>(node);
    }

    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Process this node's field
        node.Field = node.Field?.ToLowerInvariant();

        // Visit children
        return await base.VisitAsync(node, context);
    }
}

// Usage
var visitor = new LowercaseTermVisitor();
await visitor.RunAsync(result.Document);
```

### Chaining Multiple Visitors

```csharp
var chain = new ChainedQueryVisitor()
    .AddVisitor(new FieldAliasVisitor(aliases), priority: 10)
    .AddVisitor(new LowercaseTermVisitor(), priority: 20)
    .AddVisitor(new ValidationVisitor(), priority: 30);

await chain.AcceptAsync(document, context);
```

## AST Node Types

| Node Type | Description |
|-----------|-------------|
| `QueryDocument` | Root node containing the parsed query |
| `TermNode` | Simple term (e.g., `hello`) |
| `PhraseNode` | Quoted phrase (e.g., `"hello world"`) |
| `FieldQueryNode` | Field:value pair (e.g., `title:test`) |
| `RangeNode` | Range query (e.g., `[1 TO 10]`) |
| `BooleanQueryNode` | Boolean combination of clauses |
| `GroupNode` | Parenthetical group |
| `NotNode` | Negation wrapper |
| `ExistsNode` | `_exists_:field` check |
| `MissingNode` | `_missing_:field` check |
| `MatchAllNode` | `*:*` match all |
| `RegexNode` | Regular expression |
| `MultiTermNode` | Multiple terms without explicit operators |

## Building

```bash
dotnet build
dotnet test
```

## License

Apache 2.0
