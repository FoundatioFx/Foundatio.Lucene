# Agent Guidelines for Foundatio.Lucene

You are an expert .NET engineer working on Foundatio.Lucene, a production-grade library for adding dynamic Lucene-style query capabilities to .NET applications. This library parses Lucene query syntax into an AST (Abstract Syntax Tree) and supports query transformation via visitors, Entity Framework Core integration for LINQ expression generation, and Elasticsearch integration for Query DSL generation. Your changes must maintain backward compatibility, performance, and reliability. Approach each task methodically: research existing patterns, make surgical changes, and validate thoroughly.

**Craftsmanship Mindset**: Every line of code should be intentional, readable, and maintainable. Write code you'd be proud to have reviewed by senior engineers. Prefer simplicity over cleverness. When in doubt, favor explicitness and clarity.

## Repository Overview

Foundatio.Lucene provides powerful Lucene-style query capabilities for .NET applications:

- **Core Parser** (`LuceneQuery.Parse()`) - Parses query strings into AST with error recovery
- **AST Nodes** (`Ast/`) - Typed nodes: `TermNode`, `PhraseNode`, `FieldQueryNode`, `RangeNode`, `BooleanQueryNode`, `GroupNode`, etc.
- **Visitor Pattern** (`Visitors/`) - Extensible AST transformation and validation
- **Field Mapping** (`FieldMap`) - Alias user-friendly field names to actual data model fields
- **Query Validation** (`QueryValidator`) - Restrict allowed fields, operators, and patterns
- **Round-Trip** (`QueryStringBuilder`) - Convert AST back to query string

**Integrations**:

- **Entity Framework** (`Foundatio.Lucene.EntityFramework`) - Convert Lucene queries to LINQ expressions
- **Elasticsearch** (`Foundatio.Lucene.Elasticsearch`) - Generate Elasticsearch Query DSL (9.x client)

This project is a modern replacement for [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers).

Design principles: **visitor-based extensibility**, **testable**, **Lucene/Elasticsearch compatible**, **error-resilient parsing**.

### Core Pipeline

```text
Query String → LuceneLexer (tokens) → LuceneParser (AST) → Visitors (transform) → Output
```

## Quick Start

```bash
# Build
dotnet build Foundatio.Lucene.slnx

# Test
dotnet test Foundatio.Lucene.slnx

# Format code
dotnet format Foundatio.Lucene.slnx

# Run benchmarks
dotnet run -c Release --project benchmarks/Foundatio.Lucene.Benchmarks
```

## Project Structure

```text
src
├── Foundatio.Lucene                    # Core parser library
│   ├── Ast                             # AST node types (TermNode, PhraseNode, RangeNode, etc.)
│   ├── Visitors                        # Query visitors for traversal/transformation
│   ├── Extensions                      # Extension methods for nodes and strings
│   ├── LuceneQuery.cs                  # Main entry point for parsing
│   ├── LuceneParser.cs                 # Recursive-descent parser implementation
│   ├── LuceneLexer.cs                  # Tokenizer for query strings
│   ├── QueryStringBuilder.cs           # Converts AST back to query string
│   ├── QueryValidator.cs               # Query validation against options
│   └── FieldMap.cs                     # Field alias mapping
├── Foundatio.Lucene.EntityFramework    # EF Core integration
│   ├── EntityFrameworkQueryParser.cs   # Main parser for EF queries
│   ├── ExpressionBuilderVisitor.cs     # Converts AST to LINQ expressions
│   └── EntityFieldInfo.cs              # Entity field metadata
└── Foundatio.Lucene.Elasticsearch      # Elasticsearch integration
    ├── ElasticsearchQueryParser.cs     # Main parser for ES queries
    └── ElasticsearchQueryBuilderVisitor.cs  # Converts AST to ES Query DSL
tests
├── Foundatio.Lucene.Tests              # Core parser unit tests
├── Foundatio.Lucene.EntityFramework.Tests  # EF Core integration tests
└── Foundatio.Lucene.Elasticsearch.Tests    # Elasticsearch integration tests
benchmarks
└── Foundatio.Lucene.Benchmarks         # Performance benchmarks
docs                                    # VitePress documentation site
```

## Coding Standards

### Style & Formatting

- Follow `.editorconfig` rules and [Microsoft C# conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Run `dotnet format` to auto-format code
- Match existing file style; minimize diffs
- No code comments unless necessary—code should be self-explanatory

### Architecture Patterns

- **Visitor-based design**: Query transformations use visitor pattern (`IQueryVisitor`, `ChainableQueryVisitor`)
- **AST-based parsing**: Queries are parsed into Abstract Syntax Trees with typed nodes
- **Chainable visitors**: Multiple visitors can be composed via `ChainedQueryVisitor`
- **Dependency Injection**: Use constructor injection; extend via configuration lambdas
- **Naming**: `Foundatio.Lucene.[Feature]` for projects, visitor classes end with `Visitor`

### Code Quality

- Write complete, runnable code—no placeholders, TODOs, or `// existing code...` comments
- Use modern C# features: pattern matching, nullable references, `is` expressions, target-typed `new()`
- Follow SOLID, DRY principles; remove unused code and parameters
- Clear, descriptive naming; prefer explicit over clever
- Use `ConfigureAwait(false)` in library code (not in tests)
- Prefer `ValueTask<T>` for hot paths that may complete synchronously
- Always dispose resources: use `using` statements or `IAsyncDisposable`
- Handle cancellation tokens properly: check `token.IsCancellationRequested`, pass through call chains

### Common Patterns

- **Async suffix**: Methods that are genuinely asynchronous end with `Async`. The parser, query builders, and visitor pipeline are synchronous
- **CancellationToken**: Last parameter, defaulted to `default` in public APIs
- **Extension methods**: Place in `Extensions/` directory, use descriptive class names (e.g., `QueryNodeExtensions`)
- **Logging**: Use structured logging with `ILogger`, log at appropriate levels
- **Exceptions**: Throw `QueryParseException` for parse errors with position info. Use `ArgumentException.ThrowIfNullOrEmpty(parameter)` for validation. Throw `ArgumentNullException`, `ArgumentException`, `InvalidOperationException` with clear messages for general validation and operation errors.

### Visitor Pattern (Critical for Extensions)

Extend `QueryVisitor` and override the synchronous `Visit` methods for specific node types:

```csharp
public class MyVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Transform the node
        return base.Visit(node, context); // Visits children
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

### Single Responsibility

- Each class has one reason to change
- Methods do one thing well; extract when doing multiple things
- Keep files focused: one primary type per file
- Separate concerns: don't mix I/O, business logic, and presentation
- If a method needs a comment explaining what it does, it should probably be extracted

### Performance Considerations

- **Avoid allocations in hot paths**: Use `Span<T>`, `Memory<T>`, pooled buffers
- **Prefer structs for small, immutable types**: But be aware of boxing
- **Cache expensive computations**: Use `Lazy<T>` or explicit caching
- **Batch operations when possible**: Reduce round trips for I/O
- **Profile before optimizing**: Don't guess—measure with benchmarks
- **Consider concurrent access**: Use `ConcurrentDictionary`, `Interlocked`, or proper locking
- **Avoid async in tight loops**: Consider batching or `ValueTask` for hot paths
- **Dispose resources promptly**: Don't hold connections/handles longer than needed

## Making Changes

### Before Starting

1. **Gather context**: Read related files, search for similar implementations, understand the full scope
2. **Research patterns**: Find existing usages of the code you're modifying using grep/semantic search
3. **Understand completely**: Know the problem, side effects, and edge cases before coding
4. **Plan the approach**: Choose the simplest solution that satisfies all requirements
5. **Check dependencies**: Verify you understand how changes affect dependent code

### Pre-Implementation Analysis

Before writing any implementation code, think critically:

1. **What could go wrong?** Consider race conditions, null references, edge cases, resource exhaustion
2. **What are the failure modes?** Network failures, timeouts, out-of-memory, concurrent access
3. **What assumptions am I making?** Validate each assumption against the codebase
4. **Is this the root cause?** Don't fix symptoms—trace to the core problem
5. **Will this scale?** Consider performance under load, memory allocation patterns
6. **Is there existing code that does this?** Search before creating new utilities

### Test-First Development

**Always write or extend tests before implementing changes:**

1. **Find existing tests first**: Search for tests covering the code you're modifying
2. **Extend existing tests**: Add test cases to existing test classes/methods when possible for maintainability
3. **Write failing tests**: Create tests that demonstrate the bug or missing feature
4. **Implement the fix**: Write minimal code to make tests pass
5. **Refactor**: Clean up while keeping tests green
6. **Verify edge cases**: Add tests for boundary conditions and error paths

**Why extend existing tests?** Consolidates related test logic, reduces duplication, improves discoverability, maintains consistent test patterns.

### While Coding

- **Minimize diffs**: Change only what's necessary, preserve formatting and structure
- **Preserve behavior**: Don't break existing functionality or change semantics unintentionally
- **Build incrementally**: Run `dotnet build` after each logical change to catch errors early
- **Test continuously**: Run `dotnet test` frequently to verify correctness
- **Match style**: Follow the patterns in surrounding code exactly

### Validation

Before marking work complete, verify:

1. **Builds successfully**: `dotnet build Foundatio.Lucene.slnx` exits with code 0
2. **All tests pass**: `dotnet test Foundatio.Lucene.slnx` shows no failures
3. **No new warnings**: Check build output for new compiler warnings
4. **API compatibility**: Public API changes are intentional and backward-compatible when possible
5. **Documentation updated**: XML doc comments added/updated for public APIs
6. **Interface documentation**: Update interface definitions and docs with any API changes
7. **Feature documentation**: Add entries to [docs/](docs/) folder for new features or significant changes
8. **Breaking changes flagged**: Clearly identify any breaking changes for review

### Error Handling

- **Validate inputs**: Check for null, empty strings, invalid ranges at method entry
- **Fail fast**: Throw exceptions immediately for invalid arguments (don't propagate bad data)
- **Meaningful messages**: Include parameter names and expected values in exception messages
- **Don't swallow exceptions**: Log and rethrow, or let propagate unless you can handle properly
- **Use guard clauses**: Early returns for invalid conditions, keep happy path unindented

## Security

- **Validate all inputs**: Use guard clauses, check bounds, validate formats before processing
- **Sanitize external data**: Never trust data from queries or external sources
- **Avoid injection attacks**: Validate field names, escape user input, validate query patterns
- **No sensitive data in logs**: Never log passwords, tokens, keys, or PII
- **Use secure defaults**: Restrict query capabilities by default (e.g., disallow leading wildcards)
- **Follow OWASP guidelines**: Review [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- **Dependency security**: Check for known vulnerabilities before adding dependencies
- **No deprecated APIs**: Avoid obsolete cryptography, serialization, or framework features

## Testing

### Philosophy: Battle-Tested Code

Tests are not just validation—they're **executable documentation** and **design tools**. Well-tested code is:

- **Trustworthy**: Confidence to refactor and extend
- **Documented**: Tests show how the API should be used
- **Resilient**: Edge cases are covered before they become production bugs

### Framework

- **xUnit** as the primary testing framework
- Follow [Microsoft unit testing best practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)

### Test-First Workflow

1. **Search for existing tests**: `dotnet test --filter "FullyQualifiedName~MethodYouAreChanging"`
2. **Extend existing test classes**: Add new `[Fact]` or `[Theory]` cases to existing files
3. **Write the failing test first**: Verify it fails for the right reason
4. **Implement minimal code**: Just enough to pass the test
5. **Add edge case tests**: Null inputs, empty collections, boundary values, concurrent access
6. **Run full test suite**: Ensure no regressions

### Test Principles (FIRST)

- **Fast**: Tests execute quickly
- **Isolated**: No dependencies on external services or execution order
- **Repeatable**: Consistent results every run
- **Self-checking**: Tests validate their own outcomes
- **Timely**: Write tests alongside code

### Naming Convention

Use the pattern: `MethodName_StateUnderTest_ExpectedBehavior`

Examples:

- `Parse_SimpleTerm_ReturnsSingleTermNode`
- `Parse_FieldQuery_ReturnsFieldQueryNode`
- `BuildFilter_WithRangeQuery_ReturnsMatchingResults`

### Test Structure

Follow the AAA (Arrange-Act-Assert) pattern:

```csharp
[Fact]
public void Parse_SimpleTerm_ReturnsSingleTermNode()
{
    // Arrange - implicit: using LuceneQuery

    // Act
    var result = LuceneQuery.Parse("hello");

    // Assert
    Assert.True(result.IsSuccess);
    Assert.IsType<TermNode>(result.Document.Query);
    var term = (TermNode)result.Document.Query;
    Assert.Equal("hello", term.Term);
}
```

### Parameterized Tests

Use `[Theory]` with `[InlineData]` for multiple scenarios:

```csharp
[Theory]
[InlineData("hello")]
[InlineData("world")]
[InlineData("test123")]
public void Parse_SimpleTerm_ReturnsTermNode(string term)
{
    var result = LuceneQuery.Parse(term);

    Assert.True(result.IsSuccess);
    Assert.IsType<TermNode>(result.Document.Query);
}
```

### Test Organization

- Mirror the main code structure (e.g., `Visitors/` tests for visitor implementations)
- Use constructors and `IDisposable` for setup/teardown
- Inject `ITestOutputHelper` for test logging

### Key Test Files

- `ParserTests.cs` - Comprehensive parsing scenarios (~1100 test cases)
- `ChainableVisitorTests.cs` - Visitor composition patterns
- `FieldResolverQueryVisitorTests.cs` - Field alias resolution
- `QueryValidatorTests.cs` - Query validation scenarios
- `EntityFrameworkQueryParserTests.cs` - EF Core integration with in-memory database
- `ElasticsearchQueryParserTests.cs` - Elasticsearch Query DSL generation
- `ElasticsearchIntegrationTests.cs` - Integration tests with Elasticsearch using Testcontainers

### Integration Testing

- Use in-memory database for EF Core tests
- For Elasticsearch tests, use Testcontainers for real ES instance
- Verify query execution and result accuracy
- Keep integration tests separate from unit tests

### Running Tests

```bash
# All tests
dotnet test Foundatio.Lucene.slnx

# Specific test file
dotnet test --filter "FullyQualifiedName~ParserTests"

# With logging
dotnet test --logger "console;verbosity=detailed"
```

## Debugging

1. **Reproduce** with minimal steps
2. **Understand** the root cause before fixing
3. **Test** the fix thoroughly
4. **Document** non-obvious fixes in code if needed

## Resilience & Reliability

- **Error recovery**: Parser returns partial AST with detailed error information
- **Graceful degradation**: Continue parsing after errors when possible
- **Timeouts everywhere**: Never wait indefinitely; use cancellation tokens
- **Resource limits**: Bound query complexity to prevent DoS attacks
- **Idempotency**: Design operations to be safely retryable

## API Patterns

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
FieldResolverQueryVisitor.Run(result.Document, fieldMap);
```

- `FieldMap` is case-insensitive (uses `StringComparer.OrdinalIgnoreCase`)
- `FieldMap.ResolutionMode` is `Hierarchical` by default, so nested paths resolve by longest matching prefix (`data.field` → `resolved.field`)

### Validation Pattern

```csharp
var options = new QueryValidationOptions { AllowLeadingWildcards = false };
options.AllowedFields.Add("title");
var validationResult = QueryValidator.Validate(document, options);
```

### Entity Framework Integration

```csharp
var parser = new EntityFrameworkQueryParser(c => c.SetDefaultFields("Name"));
Expression<Func<Employee, bool>> filter = parser.BuildFilter<Employee>("name:john AND salary:[50000 TO *]");
var results = context.Employees.Where(filter).ToList();
```

- `ExpressionBuilderVisitor` converts AST to LINQ expressions
- Entity field metadata auto-discovered via EF Core `IEntityType`

### Elasticsearch Integration

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    config.UseScoring = true;
    config.DefaultFields = ["title", "content"];
    config.FieldMap = new FieldMap { { "author", "metadata.author" } };
});
var query = parser.BuildQuery("author:john AND status:active");
// Returns Elastic.Clients.Elasticsearch.QueryDsl.Query
```

- `ElasticsearchQueryBuilderVisitor` converts AST to Elasticsearch Query DSL
- Supports geo queries (distance, bounding box), date ranges, wildcards, regex
- Uses Elastic.Clients.Elasticsearch 9.x (official .NET client)

## Supported Query Syntax

- **Terms**: `hello`, `hello*`, `hel?o`
- **Phrases**: `"hello world"`, `"hello world"~2` (proximity)
- **Fields**: `title:test`, `user.name:john`
- **Ranges**: `price:[100 TO 500]`, `date:[* TO 2024-01-01}`
- **Boolean**: `AND`, `OR`, `NOT`, `+`, `-`
- **Groups**: `(a OR b) AND c`
- **Special**: `_exists_:field`, `_missing_:field`, `*:*` (match all)
- **Regex**: `/pattern/`
- **Date math**: `now-1d`, `2024-01-01||+1M/d`
- **Includes**: `@include:savedQuery`

## Resources

- [README.md](README.md) - Overview and quick start
- [docs/](docs/) - Full VitePress documentation
  - [Getting Started](docs/guide/getting-started.md)
  - [Query Syntax](docs/guide/query-syntax.md)
  - [Entity Framework](docs/guide/entity-framework.md)
  - [Elasticsearch](docs/guide/elasticsearch.md)
  - [Visitors](docs/guide/visitors.md)
  - [Validation](docs/guide/validation.md)
- [benchmarks/](benchmarks/) - Performance testing
- [Foundatio.Parsers](https://github.com/FoundatioFx/Foundatio.Parsers) - The predecessor to this library
- [Foundatio](https://github.com/FoundatioFx/Foundatio) - Core Foundatio building blocks
