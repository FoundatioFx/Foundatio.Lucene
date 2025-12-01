# AI Agent Instructions for Foundatio.LuceneQuery

## Architecture Overview

This is a Lucene query string parser that converts query strings into an AST (Abstract Syntax Tree), with support for query transformation via visitors and Entity Framework integration.

### Core Pipeline
```
Query String → LuceneLexer (tokens) → LuceneParser (AST) → Visitors (transform) → Output
```

### Key Components
- **`LuceneQuery.Parse()`** - Main entry point; returns `LuceneParseResult` with `Document` (AST) and `Errors`
- **`src/Foundatio.LuceneQuery/Ast/`** - AST node types (`TermNode`, `PhraseNode`, `FieldQueryNode`, `RangeNode`, `BooleanQueryNode`, `GroupNode`, etc.)
- **`src/Foundatio.LuceneQuery/Visitors/`** - Visitor pattern for AST transformation
- **`QueryStringBuilder`** - Converts AST back to query string (round-trip capability)

### Visitor Pattern (Critical for Extensions)
Extend `QueryNodeVisitor` and override `VisitAsync` methods for specific node types:
```csharp
public class MyVisitor : QueryNodeVisitor
{
    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Transform the node
        return await base.VisitAsync(node, context); // Visits children
    }
}
```
- Use `ChainedQueryVisitor` to compose multiple visitors with priority ordering
- `IQueryVisitorContext` provides shared state via `SetValue`/`GetValue<T>` methods

### Built-in Visitors
- `FieldResolverQueryVisitor` - Maps field aliases using `FieldMap`
- `IncludeVisitor` - Expands `@include:name` references
- `DateMathEvaluatorVisitor` - Evaluates Elasticsearch date math expressions (`now+1d`, `2024-01-01||+1M/d`)
- `ValidationVisitor` - Validates queries against `QueryValidationOptions`

## Developer Workflows

### Build & Test
```bash
dotnet build                    # Build all projects
dotnet test                     # Run all tests
dotnet run -c Release --project benchmarks/Foundatio.LuceneQuery.Benchmarks  # Run benchmarks
```

### Project Structure
- `src/Foundatio.LuceneQuery/` - Core parser library (net8.0, net10.0)
- `src/Foundatio.LuceneQuery.EntityFramework/` - EF Core integration for LINQ expression generation
- `tests/` - xUnit test projects

## Patterns & Conventions

### Query Parsing Pattern
```csharp
var result = LuceneQuery.Parse("title:test AND status:active");
if (result.IsSuccess)
{
    var document = result.Document; // QueryDocument (root AST node)
}
// result.Errors contains parse errors; partial AST may still be available
```

### Field Resolution Pattern
```csharp
var fieldMap = new FieldMap { { "user", "account.user" }, { "created", "metadata.timestamp" } };
await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);
```
- `FieldMap` is case-insensitive (uses `StringComparer.OrdinalIgnoreCase`)
- `ToHierarchicalFieldResolver()` extension supports nested paths (`data.field` → `resolved.field`)

### Validation Pattern
```csharp
var options = new QueryValidationOptions { AllowLeadingWildcards = false };
options.AllowedFields.Add("title");
var validationResult = await QueryValidator.ValidateAsync(document, options);
```

### Entity Framework Integration
```csharp
var parser = new EntityFrameworkQueryParser();
Expression<Func<Employee, bool>> filter = parser.BuildFilter<Employee>("name:john AND salary:[50000 TO *]");
var results = context.Employees.Where(filter);
```
- `ExpressionBuilderVisitor` converts AST to LINQ expressions
- Entity field metadata auto-discovered via EF Core `IEntityType`

## Test Patterns

Tests follow xUnit conventions with descriptive method names:
```csharp
[Fact]
public void Parse_FieldQuery_ReturnsFieldQueryNode() { ... }

[Fact]
public async Task CanResolveComplexQuery() { ... }
```

Key test files for understanding behavior:
- `ParserTests.cs` - Comprehensive parsing scenarios (~1100 lines)
- `ChainableVisitorTests.cs` - Visitor composition patterns
- `EntityFrameworkQueryParserTests.cs` - EF integration with in-memory database

## Supported Query Syntax

- Terms: `hello`, `hello*`, `hel?o`
- Phrases: `"hello world"`, `"hello world"~2` (proximity)
- Fields: `title:test`, `user.name:john`
- Ranges: `price:[100 TO 500]`, `date:[* TO 2024-01-01}`
- Boolean: `AND`, `OR`, `NOT`, `+`, `-`
- Groups: `(a OR b) AND c`
- Special: `_exists_:field`, `_missing_:field`, `*:*` (match all)
- Regex: `/pattern/`
- Date math: `now-1d`, `2024-01-01||+1M/d`
- Includes: `@include:savedQuery`
