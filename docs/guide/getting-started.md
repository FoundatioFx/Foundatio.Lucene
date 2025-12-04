# Getting Started

Foundatio.Lucene is a library for parsing Lucene-style query strings and converting them to various output formats. This guide will walk you through the basic setup and your first query.

## Installation

Install the NuGet packages for your use case:

::: code-group

```bash [Core Parser]
dotnet add package Foundatio.Lucene
```

```bash [Entity Framework]
dotnet add package Foundatio.Lucene.EntityFramework
```

```bash [Elasticsearch]
dotnet add package Foundatio.Lucene.Elasticsearch
```

:::

## Basic Parsing

The simplest usage is to parse a query string into an AST:

```csharp
using Foundatio.Lucene;

var result = LuceneQuery.Parse("title:hello AND status:active");

if (result.IsSuccess)
{
    var document = result.Document; // QueryDocument (root AST node)
    Console.WriteLine($"Parsed {document.Children.Count} clauses");
}
else
{
    // Handle errors - partial AST may still be available
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error at {error.Line}:{error.Column}: {error.Message}");
    }
}
```

## Converting Back to Query String

You can convert the AST back to a query string:

```csharp
using Foundatio.Lucene;

var result = LuceneQuery.Parse("title:test AND (status:active OR status:pending)");
var queryString = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "title:test AND (status:active OR status:pending)"
```

## Field Aliasing

Map user-friendly field names to your actual data model:

```csharp
using Foundatio.Lucene;
using Foundatio.Lucene.Visitors;

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

## Query Validation

Restrict what users can query:

```csharp
using Foundatio.Lucene;

var result = LuceneQuery.Parse("*wildcard AND title:test");

var options = new QueryValidationOptions
{
    AllowLeadingWildcards = false
};
options.AllowedFields.Add("title");
options.AllowedFields.Add("status");

var validationResult = await QueryValidator.ValidateAsync(result.Document, options);

if (!validationResult.IsValid)
{
    Console.WriteLine(validationResult.Message);
}
```

## Entity Framework Integration

Enable dynamic, user-driven queries in your API endpoints:

```csharp
using Foundatio.Lucene.EntityFramework;

// In your API controller or service
[HttpGet("employees")]
public async Task<IActionResult> SearchEmployees([FromQuery] string query)
{
    var parser = new EntityFrameworkQueryParser();

    // User provides: "name:john AND salary:[50000 TO *] AND department:engineering"
    var filter = parser.BuildFilter<Employee>(query);

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
var filter = parser.BuildFilter<Employee>(userQuery, fieldMap);
```

## Elasticsearch Integration

Generate Elasticsearch Query DSL from Lucene syntax:

```csharp
using Foundatio.Lucene.Elasticsearch;
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

## Next Steps

Now that you have the basics working, explore more advanced features:

- [Query Syntax](./query-syntax) - Learn all the supported syntax
- [Visitors](./visitors) - Transform and analyze queries
- [Field Mapping](./field-mapping) - Advanced field aliasing
- [Entity Framework](./entity-framework) - Deep dive into EF integration
- [Elasticsearch](./elasticsearch) - Deep dive into ES integration
