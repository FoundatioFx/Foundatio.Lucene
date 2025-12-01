# Foundatio.LuceneQueryParser

A high-performance Lucene query string parser for .NET that converts query strings into an Abstract Syntax Tree (AST). Supports query transformation via visitors and includes Entity Framework Core integration for generating LINQ expressions.

## Features

- **Full Lucene Query Syntax** - Terms, phrases, fields, ranges, boolean operators, wildcards, regex, and more
- **Elasticsearch Extensions** - Date math expressions (`now-1d`, `2024-01-01||+1M/d`), `_exists_`, `_missing_`
- **Visitor Pattern** - Transform, validate, or analyze queries with composable visitors
- **Round-Trip Capable** - Parse queries to AST and convert back to query strings
- **Entity Framework Integration** - Convert Lucene queries directly to LINQ expressions
- **Error Recovery** - Resilient parser returns partial AST with detailed error information

## Installation

```bash
# Core parser
dotnet add package Foundatio.LuceneQueryParser

# Entity Framework integration (optional)
dotnet add package Foundatio.LuceneQueryParser.EntityFramework
```

## Quick Start

### Basic Parsing

```csharp
using Foundatio.LuceneQueryParser;

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
using Foundatio.LuceneQueryParser;

var result = LuceneQuery.Parse("title:test AND (status:active OR status:pending)");
var queryString = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "title:test AND (status:active OR status:pending)"
```

### Field Aliasing

```csharp
using Foundatio.LuceneQueryParser;
using Foundatio.LuceneQueryParser.Visitors;

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
using Foundatio.LuceneQueryParser;

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

```csharp
using Foundatio.LuceneQueryParser.EntityFramework;

var parser = new EntityFrameworkQueryParser();

// Build a filter expression from a Lucene query
Expression<Func<Employee, bool>> filter = parser.BuildFilter<Employee>(
    "name:john AND salary:[50000 TO *] AND isActive:true"
);

// Use with EF Core
var results = await context.Employees.Where(filter).ToListAsync();
```

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
using Foundatio.LuceneQueryParser.Ast;
using Foundatio.LuceneQueryParser.Visitors;

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
