# Visitors

Visitors walk a parsed query tree to analyze or transform it. The parsers run a visitor pipeline on every query before building it, and you can run visitors yourself on any parsed document.

## The query pipeline

Every provider (Elasticsearch, Entity Framework) processes a query in the same order:

1. **Parse** the text into a `QueryDocument`.
2. **Resolve** asynchronous dependencies (only in the `Async` methods): include text, field names, and provider data such as index mappings.
3. **Run the visitor pipeline**, in priority order:

   | Priority | Visitor | Purpose |
   |---:|---|---|
   | 0 | `IncludeVisitor` | expands `@include:name` references |
   | 0 | your visitors (default priority) | see field names as written |
   | 10 | `FieldResolverQueryVisitor` | resolves aliases and field names |
   | 30 | `ValidationVisitor` | records what the query uses and enforces `QueryValidationOptions` |

4. **Build** the provider's query from the processed tree.

Documents you pass in are cloned first, so a cached `QueryDocument` is never modified.

```csharp
var parser = new ElasticsearchQueryParser(c =>
{
    c.AddVisitor(new MyVisitor());                                   // priority 0
    c.AddVisitor(new AfterResolutionVisitor(), priority: 20);        // after field resolution
    c.AddVisitorAfter<ValidationVisitor>(new AfterValidationVisitor());
});
```

## Built-in visitors

| Visitor | What it does |
|---|---|
| `IncludeVisitor` | Replaces `@include:name` with the parsed text of the include, wrapped in a group so it keeps its meaning. Nested includes are expanded; recursion, depth (`MaxIncludeDepth`), and total expansions (`MaxIncludeExpansions`) are bounded. |
| `FieldResolverQueryVisitor` | Replaces field names using the context's `FieldResolver` and then `FieldMap`. The name as written is kept on the node (`node.GetOriginalField()`). Fields starting with `@` are left alone. |
| `ValidationVisitor` | Collects referenced fields, operations, and nesting depth into `context.ValidationResult` and applies `QueryValidationOptions`. |
| `DateMathEvaluatorVisitor` | Replaces date math (`now-1d/d`) with absolute dates, for data stores that don't evaluate date math themselves. |
| `InvertQueryVisitor` | Inverts a query while keeping scope fields (such as a tenant id) intact. |
| `RemoveFieldsQueryVisitor` | Removes clauses that use given fields. |
| `CleanupQueryVisitor` | Removes redundant parentheses and empty clauses. |

### Field resolution

```csharp
var document = LuceneQuery.Parse("user:john AND created:[now-1d TO now]").Document;

var fieldMap = new FieldMap
{
    { "user", "account.username" },
    { "created", "metadata.timestamp" }
};

FieldResolverQueryVisitor.Run(document, fieldMap);

string text = QueryStringBuilder.ToQueryString(document);
// account.username:john AND metadata.timestamp:[now-1d TO now]
```

See [Field Mapping](./field-mapping) for resolvers, hierarchical maps, and unresolved fields.

### Includes

```csharp
var includes = new Dictionary<string, string>
{
    ["active"] = "status:active AND deleted:false"
};

var document = LuceneQuery.Parse("@include:active category:books").Document;
document = IncludeVisitor.ExpandIncludes(document, includes);
// (status:active AND deleted:false) category:books
```

Configure `Includes` (or an async `IncludeResolver`) on a parser to expand includes automatically.

### Date math

```csharp
var document = LuceneQuery.Parse("created:[now-7d/d TO now]").Document;
var evaluated = DateMathEvaluatorVisitor.Evaluate(document, now: DateTimeOffset.UtcNow);
```

See [Date Math](./date-math).

### Inverting a query

`InvertQueryVisitor` returns the complement of a query within its scope fields:

```csharp
string inverted = InvertQueryVisitor.Run(
    "organization:1 status:open type:error",
    nonInvertedFields: ["organization"]);
// organization:1 (NOT (status:open type:error))

string withAlternate = InvertQueryVisitor.Run(
    "organization:1 status:open",
    nonInvertedFields: ["organization"],
    alternateInvertedCriteria: "is_deleted:true");
// organization:1 (is_deleted:true OR (NOT status:open))
```

### Removing fields and cleaning up

```csharp
RemoveFieldsQueryVisitor.Run("organization:1 status:open", ["organization"]); // status:open
CleanupQueryVisitor.Run("NOT ((status:fixed))");                             // NOT status:fixed
```

### Referenced fields

```csharp
ISet<string> fields = LuceneQuery.Parse("title:hello AND (status:open OR _exists_:tags)").Document.GetReferencedFields();
// title, status, tags
```

## Running visitors yourself

Visitors implement `IQueryVisitor` and take a context:

```csharp
var context = new QueryVisitorContext { FieldMap = fieldMap };
var result = new MyVisitor().Accept(document, context);
```

Compose several with `ChainedQueryVisitor`, which runs them in priority order and is safe to share between threads:

```csharp
var chain = new ChainedQueryVisitor()
    .AddVisitor(IncludeVisitor.Instance, 0)
    .AddVisitor(FieldResolverQueryVisitor.Instance, 10)
    .AddVisitor(new MyVisitor(), 20);

var processed = chain.Accept(document, context);
```

To write your own visitor, see [Custom Visitors](./custom-visitors).
