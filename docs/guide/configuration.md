# Configuration

Both providers share one configuration model. Configure a parser once, when you create it, and pass settings that vary by request as options.

```csharp
var parser = new ElasticsearchQueryParser(c =>
{
    c.DefaultOperator = BooleanOperator.And;
    c.DefaultFields = ["title", "body"];
    c.FieldMap = new FieldMap { { "author", "metadata.author" } };
    c.Includes = savedQueries;
    c.ValidationOptions = validation;
});
```

## Shared settings

These are available on `ElasticsearchQueryParserConfiguration` and `EntityFrameworkQueryParserConfiguration`:

| Setting | Default | Purpose |
|---|---|---|
| `DefaultOperator` | `And` | Operator between clauses written next to each other. Use `Or` for search boxes. |
| `ParserOptions` | default | Full parser options (`DefaultOperator`, `MaxDepth`). |
| `DefaultFields` | none | Fields searched by terms without a field. |
| `FieldMap` | none | Field aliases. See [Field Mapping](./field-mapping). |
| `FieldResolver` | none | Synchronous field resolver, applied before `FieldMap`. |
| `AsyncFieldResolver` | none | Asynchronous field resolver (resolution phase). |
| `Includes` | none | Saved queries for `@include:name`. |
| `IncludeResolver` | none | Asynchronous include lookup (resolution phase). |
| `ShouldSkipInclude` | none | Leaves matching `@include` references unexpanded. |
| `ValidationOptions` | none | Rules enforced on every build. See [Validation](./validation). |
| `TimeProvider` | system clock | Clock for relative dates. |
| `QueryVisitor` | built-ins | The visitor pipeline (`AddVisitor`, `AddVisitorBefore<T>`, `AddVisitorAfter<T>`, `ReplaceVisitor<T>`, `RemoveVisitor<T>`). |

The provider pages describe their own settings: [Elasticsearch](./elasticsearch), [Entity Framework](./entity-framework).

## Per-request options

Pass settings that vary by request, user, or tenant as an options record. Options are immutable and can be cached; any setting you leave null falls back to the parser configuration.

```csharp
var options = new ElasticsearchQueryOptions
{
    DefaultOperator = BooleanOperator.Or,
    FieldMap = tenant.FieldMap,
    Includes = tenant.SavedQueries,
    ValidationOptions = tenant.Validation
};

var query = parser.BuildQuery(userQuery, options);
```

Options registered on the parser (`SetOptions`, by index for Elasticsearch or by entity type for Entity Framework) sit between the two: per-request options win over registered options, which win over the configuration.

## Includes

`@include:name` expands a saved query by name. Supply the queries as a dictionary:

```csharp
c.Includes = new Dictionary<string, string>
{
    ["active"] = "status:active AND deleted:false",
    ["mine"] = "owner:me"
};
```

```
@include:active category:books
-@include:mine
```

An expansion takes the reference's place, so `-@include:mine` excludes everything the saved query matches. Includes can use other includes; recursion is reported as an error, and depth and fan-out are limited by `MaxIncludeDepth` and `MaxIncludeExpansions`. Unresolved includes are errors unless `AllowUnresolvedIncludes` is set. Include text is parsed with the same parser options as the query.

## Asynchronous resolution

Some settings need I/O: include text in a database, field names from a custom field service, an Elasticsearch mapping loaded from the server, geo locations. Configure them as asynchronous resolvers:

```csharp
var parser = new ElasticsearchQueryParser(c =>
{
    c.IncludeResolver = async (name, context, cancellationToken) => await savedQueries.GetQueryAsync(name, cancellationToken);
    c.AsyncFieldResolver = async (field, context, cancellationToken) => await customFields.GetStorageNameAsync(field, cancellationToken);
});

var query = await parser.BuildQueryAsync("@include:mine status:open");
```

The `Async` methods run a resolution phase before building:

1. Parse the query.
2. Fetch the includes it references (and the includes those reference), in parallel per level.
3. Resolve every field the query and its includes use.
4. Run provider lookups (Elasticsearch mappings, runtime fields, geo locations, nested filters).
5. Run the synchronous pipeline and build.

Resolvers receive the context, so per-request data is available to them, and their exceptions become validation errors. Parsing and building never do I/O, and the synchronous methods throw `InvalidOperationException` when a resolver is configured, so nothing blocks.

## Custom visitors

```csharp
c.AddVisitor(new ShortcutVisitor());                          // priority 0: before field resolution
c.AddVisitor(new AuditVisitor(), priority: 40);                // after validation
c.AddVisitorBefore<FieldResolverQueryVisitor>(new MyVisitor());
c.ReplaceVisitor<FieldResolverQueryVisitor>(new MyFieldResolver());
```

See [Visitors](./visitors) and [Custom Visitors](./custom-visitors).

## Parser options

To parse without a provider:

```csharp
var options = new LuceneParserOptions { DefaultOperator = BooleanOperator.Or, MaxDepth = 50 };
var result = LuceneQuery.Parse("a b", options);
```

## Dependency injection

Parsers are thread-safe; register them as singletons:

```csharp
builder.Services.AddSingleton(sp => new ElasticsearchQueryParser(c =>
{
    c.UseMappings(ElasticMappingResolver.Create(sp.GetRequiredService<ElasticsearchClient>(), "events"));
    c.ValidationOptions = SearchFields.CreateValidationOptions();
}));
```
