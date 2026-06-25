# Configuration

This guide covers configuration options for the various components of Foundatio.Lucene.

The parser APIs are **synchronous** — there are no `*Async` parse/build/visitor methods.

## Core Parser

The core parser is stateless and requires no configuration:

```csharp
var result = LuceneQuery.Parse("title:hello AND status:active");
```

`LuceneQuery.Parse` is resilient: it always returns a (possibly partial) AST plus any
`ParseError`s, and bounds nesting depth (`LuceneParser.MaxDepth`, default 100) so deeply nested
input can never overflow the stack.

## Entity Framework Parser

```csharp
var parser = new EntityFrameworkQueryParser(config =>
{
    config.SetDefaultFields("Name", "Email");
    config.SetDefaultOperator(BooleanOperator.And);
    // Controls 'now' in date math (now-7d, now/d). Defaults to TimeProvider.System.
    config.SetTimeProvider(TimeProvider.System);
});

var filter = parser.BuildFilter<Employee>("name:john AND salary:[50000 TO *]");
var employees = context.Employees.Where(filter).ToList();
```

### Field aliasing, includes and date math (per request)

Field maps, `@include` references and date math are applied through the shared visitor pipeline,
exactly as in the Elasticsearch integration. Supply them per request via
`EntityFrameworkQueryOptions` (the recommended way to swap configuration per scope/tenant):

```csharp
var options = new EntityFrameworkQueryOptionsBuilder()
    .WithFieldMap(new FieldMap { { "user", "Name" }, { "dept", "Department.Name" } })
    .WithIncludes(new Dictionary<string, string> { ["seniors"] = "Title:Senior" })
    .WithValidationOptions(o => o.AllowLeadingWildcards = false)
    .Build();

var filter = parser.BuildFilter<Employee>("user:john AND @include:seniors", context: null, options);
```

You can also register default options per entity type with `parser.SetOptions<Employee>(options)`.
Per-request options take precedence over registered options, which take precedence over the
global configuration.

> SQL cannot honor fuzzy (`term~N`) or proximity/slop (`"a b"~N`) queries. Rather than silently
> returning the wrong rows, the EF builder throws `QueryBuildException` (use `TryBuildFilter` to get
> a `QueryResult` instead of an exception). Regex queries depend on the database provider.

## Elasticsearch Parser

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    config.UseScoring = true;                         // match queries (scoring) vs term queries (filter)
    config.DefaultFields = ["title", "content"];      // fields for unfielded terms
    config.DefaultOperator = BooleanOperator.And;     // implicit operator
    config.FieldMap = new FieldMap { { "author", "metadata.author" } };
    config.IsDateField = field => field.EndsWith("At") || field.EndsWith("date");
    config.DefaultTimeZone = "America/Chicago";
    config.ValidationOptions = new QueryValidationOptions { AllowLeadingWildcards = false };
});

var query = parser.BuildQuery("author:john AND status:active");
```

### Configuration properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseScoring` | `bool` | `false` | Use match queries (scoring) vs term queries (filtering) |
| `DefaultFields` | `string[]?` | `null` | Fields to search for unfielded terms |
| `DefaultOperator` | `BooleanOperator` | `Or` | Default boolean operator for implicit combinations |
| `FieldMap` | `FieldMap?` | `null` | Field name mappings |
| `Includes` | `IReadOnlyDictionary<string,string>?` | `null` | Pre-resolved `@include` content |
| `IsDateField` | `Func<string, bool>?` | `null` | Detects date fields for date range queries |
| `DefaultTimeZone` | `string?` | `null` | Default timezone for date range queries |
| `ValidationOptions` | `QueryValidationOptions?` | `null` | Query validation options |
| `TimeProvider` | `TimeProvider` | `System` | Controls `now` in date math |

### Per-request / per-scope options

Swap configuration per scope by passing a prebuilt `ElasticsearchQueryOptions` (or registering it
per index name). Options are immutable records, so an application can cache one per tenant and reuse
it across requests:

```csharp
var tenantOptions = new ElasticsearchQueryOptionsBuilder()
    .WithFieldMap(new FieldMap { { "user", "tenant42.userName" } })
    .WithDefaultFields("tenant42.title")
    .Build();

var query = parser.BuildQuery("user:john", tenantOptions);
```

The same parser instance is thread-safe and reuses its visitors across requests, so swapping
options per request does not allocate a visitor per call.

## Field Map

```csharp
var fieldMap = new FieldMap
{
    { "user", "account.username" },
    { "created", "metadata.createdAt" }
};
```

`FieldMap` is case-insensitive. By default `ResolutionMode` is `Hierarchical`, so nested paths
resolve by longest matching prefix:

```csharp
var fieldMap = new FieldMap
{
    { "data", "payload" },
    { "data.user", "payload.account.username" }
};
// "data.user:john"   -> "payload.account.username:john"
// "data.status:open" -> "payload.status:open"
```

Set `ResolutionMode = FieldResolutionMode.Direct` for exact-match-only resolution,
`ReportUnmappedFields = true` to flag unmapped fields as unresolved, or `ResultPrefix` to prefix
every resolved name. The parsers apply the field map automatically; to run it manually:

```csharp
FieldResolverQueryVisitor.Run(result.Document, fieldMap);
```

## Includes

`@include:name` references expand from a pre-resolved dictionary (resolve saved queries from your
store before parsing):

```csharp
var includes = new Dictionary<string, string> { ["recent"] = "created:[now-7d TO now]" };

var parser = new ElasticsearchQueryParser(c => c.Includes = includes);
var query = parser.BuildQuery("@include:recent AND status:active");

// Or run it manually against the AST:
result.Document.ExpandIncludes(includes);
```

Circular references and excessive nesting (`IncludeVisitor.MaxIncludeDepth`) are detected and
reported as validation errors.

## Validation

```csharp
var options = new QueryValidationOptions
{
    AllowLeadingWildcards = false,
    AllowWildcardOnlyQueries = false,
    AllowedFields = { "title", "author", "status", "date" },
    DisallowedFields = { "password", "ssn" }
};

var result = QueryValidator.ValidateQuery("title:hello", options);
if (!result.IsValid)
{
    // result.ValidationErrors describe what failed
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AllowLeadingWildcards` | `bool` | `true` | Allow patterns like `*suffix` |
| `AllowWildcardOnlyQueries` | `bool` | `true` | Allow `*` or `*:*` queries |
| `AllowedFields` | `HashSet<string>` | empty | Whitelist of allowed fields |
| `DisallowedFields` | `HashSet<string>` | empty | Blacklist of disallowed fields |

## Custom Visitors

Compose visitors with `ChainedQueryVisitor` and run them synchronously:

```csharp
var visitors = new ChainedQueryVisitor()
    .AddVisitor(new FieldResolverQueryVisitor(fieldMap), priority: 10)
    .AddVisitor(new DateMathEvaluatorVisitor(), priority: 30)
    .AddVisitor(new ValidationVisitor(), priority: 100);

var context = new QueryVisitorContext();
visitors.Accept(result.Document, context);
```

## Dependency Injection

```csharp
services.AddSingleton<EntityFrameworkQueryParser>();

services.AddSingleton(sp => new ElasticsearchQueryParser(config =>
{
    config.UseScoring = true;
    config.DefaultFields = ["title", "content"];
    config.ValidationOptions = new QueryValidationOptions { AllowLeadingWildcards = false };
}));
```

## Next Steps

- [Getting Started](./getting-started) - Quick start guide
- [Elasticsearch](./elasticsearch) - Elasticsearch integration
- [Validation](./validation) - Query validation
