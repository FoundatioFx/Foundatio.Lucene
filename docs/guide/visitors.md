# Visitors

Visitors are the core mechanism for transforming, validating, and analyzing parsed queries. They implement the visitor pattern to traverse and optionally modify the AST (Abstract Syntax Tree).

## Built-in Visitors

Foundatio.Lucene includes several built-in visitors:

| Visitor | Description |
|---------|-------------|
| `FieldResolverQueryVisitor` | Maps field aliases using `FieldMap` |
| `IncludeVisitor` | Expands `@include:name` references |
| `DateMathEvaluatorVisitor` | Evaluates date math expressions |
| `ValidationVisitor` | Validates queries against `QueryValidationOptions` |
| `GetReferencedFieldsVisitor` | Extracts all referenced field names |

## Using Built-in Visitors

### Field Resolver

Map user-friendly field names to actual field names:

```csharp
using Foundatio.Lucene;
using Foundatio.Lucene.Visitors;

var result = LuceneQuery.Parse("user:john AND created:[now-1d TO now]");

var fieldMap = new FieldMap
{
    { "user", "account.username" },
    { "created", "metadata.timestamp" }
};

await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

var resolved = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "account.username:john AND metadata.timestamp:[now-1d TO now]"
```

### Date Math Evaluator

Evaluate date math expressions to actual dates:

```csharp
var result = LuceneQuery.Parse("created:[now-7d TO now]");

await DateMathEvaluatorVisitor.RunAsync(result.Document);

// Date expressions are now evaluated to actual DateTime values
```

### Include Visitor

Expand `@include:name` references to saved queries:

```csharp
var result = LuceneQuery.Parse("@include:active-filter AND category:books");

Func<string, Task<string?>> resolver = async name =>
{
    // Load saved query from database, file, etc.
    return name switch
    {
        "active-filter" => "status:active AND deleted:false",
        _ => null
    };
};

await IncludeVisitor.RunAsync(result.Document, resolver);

var expanded = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "(status:active AND deleted:false) AND category:books"
```

### Get Referenced Fields

Extract all field names used in a query:

```csharp
var result = LuceneQuery.Parse("title:hello AND author:john AND date:[2024-01-01 TO *]");

var fields = await GetReferencedFieldsVisitor.RunAsync(result.Document);
// Returns: ["title", "author", "date"]
```

## Creating Custom Visitors

Extend `QueryNodeVisitor` to create custom transformations:

```csharp
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

public class LowercaseTermVisitor : QueryNodeVisitor
{
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        // Lowercase the term
        node.Term = node.Term?.ToLowerInvariant();
        return Task.FromResult<QueryNode>(node);
    }

    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Lowercase the field name
        node.Field = node.Field?.ToLowerInvariant();

        // Visit children (the field's value)
        return await base.VisitAsync(node, context);
    }
}

// Usage
var result = LuceneQuery.Parse("Title:HELLO");
var visitor = new LowercaseTermVisitor();
await visitor.AcceptAsync(result.Document, new QueryVisitorContext());

var output = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "title:hello"
```

## Visitor Context

Use `IQueryVisitorContext` to pass state between visitors or across the traversal:

```csharp
public class FieldCollectorVisitor : QueryNodeVisitor
{
    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Get or create the field list in context
        var fields = context.GetValue<List<string>>("CollectedFields") ?? new List<string>();
        
        if (node.Field != null && !fields.Contains(node.Field))
        {
            fields.Add(node.Field);
            context.SetValue("CollectedFields", fields);
        }

        return await base.VisitAsync(node, context);
    }
}

// Usage
var context = new QueryVisitorContext();
await new FieldCollectorVisitor().AcceptAsync(result.Document, context);

var fields = context.GetValue<List<string>>("CollectedFields");
```

## Chaining Visitors

Use `ChainedQueryVisitor` to run multiple visitors in sequence:

```csharp
var chain = new ChainedQueryVisitor()
    .AddVisitor(new FieldResolverQueryVisitor(fieldMap), priority: 10)
    .AddVisitor(new DateMathEvaluatorVisitor(), priority: 20)
    .AddVisitor(new LowercaseTermVisitor(), priority: 30)
    .AddVisitor(new ValidationVisitor(), priority: 100);

await chain.AcceptAsync(document, context);
```

Visitors with lower priority numbers run first.

## Visitor Methods

Override these methods to handle specific node types:

```csharp
public class MyVisitor : QueryNodeVisitor
{
    // Called for the root document
    public override Task<QueryNode> VisitAsync(QueryDocument node, IQueryVisitorContext context);

    // Simple terms like: hello
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context);

    // Quoted phrases like: "hello world"
    public override Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context);

    // Field queries like: title:hello
    public override Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context);

    // Range queries like: [1 TO 10]
    public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context);

    // Boolean combinations like: a AND b
    public override Task<QueryNode> VisitAsync(BooleanQueryNode node, IQueryVisitorContext context);

    // Parenthetical groups like: (a OR b)
    public override Task<QueryNode> VisitAsync(GroupNode node, IQueryVisitorContext context);

    // Negations like: NOT a
    public override Task<QueryNode> VisitAsync(NotNode node, IQueryVisitorContext context);

    // Existence checks like: _exists_:field
    public override Task<QueryNode> VisitAsync(ExistsNode node, IQueryVisitorContext context);

    // Missing checks like: _missing_:field
    public override Task<QueryNode> VisitAsync(MissingNode node, IQueryVisitorContext context);

    // Match all like: *:*
    public override Task<QueryNode> VisitAsync(MatchAllNode node, IQueryVisitorContext context);

    // Regex patterns like: /pattern/
    public override Task<QueryNode> VisitAsync(RegexNode node, IQueryVisitorContext context);

    // Include references like: @include:name
    public override Task<QueryNode> VisitAsync(IncludeNode node, IQueryVisitorContext context);
}
```

## Replacing Nodes

Return a different node to replace the current one:

```csharp
public class ExpandStatusVisitor : QueryNodeVisitor
{
    public override Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Replace status:all with a group of all statuses
        if (node.Field == "status" && node.Value is TermNode term && term.Term == "all")
        {
            var group = new GroupNode
            {
                Child = new BooleanQueryNode
                {
                    Left = new FieldQueryNode { Field = "status", Value = new TermNode { Term = "active" } },
                    Operator = QueryOperator.Or,
                    Right = new FieldQueryNode { Field = "status", Value = new TermNode { Term = "pending" } }
                }
            };
            return Task.FromResult<QueryNode>(group);
        }

        return base.VisitAsync(node, context);
    }
}

// Input: "status:all"
// Output: "(status:active OR status:pending)"
```

## Removing Nodes

Return `null` to remove a node (parent must handle this):

```csharp
public class RemoveFieldVisitor : QueryNodeVisitor
{
    private readonly HashSet<string> _fieldsToRemove;

    public RemoveFieldVisitor(params string[] fields)
    {
        _fieldsToRemove = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
    }

    public override Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (_fieldsToRemove.Contains(node.Field ?? ""))
        {
            return Task.FromResult<QueryNode>(null!);
        }

        return base.VisitAsync(node, context);
    }
}
```

## Next Steps

- [Field Mapping](./field-mapping) - Detailed field aliasing
- [Validation](./validation) - Query validation
- [Custom Visitors](./custom-visitors) - Advanced visitor patterns
