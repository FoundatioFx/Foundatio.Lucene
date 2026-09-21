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

FieldResolverQueryVisitor.Run(result.Document, fieldMap);

var resolved = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "account.username:john AND metadata.timestamp:[now-1d TO now]"
```

### Date Math Evaluator

Evaluate date math expressions to actual dates:

```csharp
var result = LuceneQuery.Parse("created:[now-7d TO now]");

new DateMathEvaluatorVisitor().Evaluate(result.Document);

// Date expressions are now evaluated to actual DateTime values
```

### Include Visitor

Expand `@include:name` references to saved queries:

```csharp
var result = LuceneQuery.Parse("@include:active-filter AND category:books");

// Pre-resolve saved queries (from a database, file, etc.) into a dictionary.
var includes = new Dictionary<string, string>
{
    ["active-filter"] = "status:active AND deleted:false"
};

result.Document.ExpandIncludes(includes);

var expanded = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "(status:active AND deleted:false) AND category:books"
```

### Get Referenced Fields

Extract all field names used in a query:

```csharp
var result = LuceneQuery.Parse("title:hello AND author:john AND date:[2024-01-01 TO *]");

var fields = result.Document.GetReferencedFields();
// Returns: ["title", "author", "date"]
```

## Creating Custom Visitors

Extend `QueryVisitor` to create custom transformations:

```csharp
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

public class LowercaseTermVisitor : QueryVisitor
{
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        // Lowercase the term
        node.Term = node.Term?.ToLowerInvariant();
        return node;
    }

    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Lowercase the field name
        node.Field = node.Field?.ToLowerInvariant();

        // Visit children (the field's value)
        return base.Visit(node, context);
    }
}

// Usage
var result = LuceneQuery.Parse("Title:HELLO");
var visitor = new LowercaseTermVisitor();
visitor.Accept(result.Document, new QueryVisitorContext());

var output = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "title:hello"
```

## Visitor Context

Use `IQueryVisitorContext` to pass state between visitors or across the traversal:

```csharp
public class FieldCollectorVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Get or create the field list in context
        var fields = context.GetValue<List<string>>("CollectedFields") ?? new List<string>();
        
        if (node.Field != null && !fields.Contains(node.Field))
        {
            fields.Add(node.Field);
            context.SetValue("CollectedFields", fields);
        }

        return base.Visit(node, context);
    }
}

// Usage
var context = new QueryVisitorContext();
new FieldCollectorVisitor().Accept(result.Document, context);

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

chain.Accept(document, context);
```

Visitors with lower priority numbers run first.

## Visitor Methods

Override these methods to handle specific node types:

```csharp
public class MyVisitor : QueryVisitor
{
    // Called for the root document
    protected override QueryNode Visit(QueryDocument node, IQueryVisitorContext context);

    // Simple terms like: hello
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context);

    // Quoted phrases like: "hello world"
    protected override QueryNode Visit(PhraseNode node, IQueryVisitorContext context);

    // Field queries like: title:hello
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context);

    // Range queries like: [1 TO 10]
    protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context);

    // Boolean combinations like: a AND b
    protected override QueryNode Visit(BooleanQueryNode node, IQueryVisitorContext context);

    // Parenthetical groups like: (a OR b)
    protected override QueryNode Visit(GroupNode node, IQueryVisitorContext context);

    // Negations like: NOT a
    protected override QueryNode Visit(NotNode node, IQueryVisitorContext context);

    // Existence checks like: _exists_:field
    protected override QueryNode Visit(ExistsNode node, IQueryVisitorContext context);

    // Missing checks like: _missing_:field
    protected override QueryNode Visit(MissingNode node, IQueryVisitorContext context);

    // Match all like: *:*
    protected override QueryNode Visit(MatchAllNode node, IQueryVisitorContext context);

    // Regex patterns like: /pattern/
    protected override QueryNode Visit(RegexNode node, IQueryVisitorContext context);
}
```

## Replacing Nodes

Return a different node to replace the current one:

```csharp
public class ExpandStatusVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Replace status:all with a group of all statuses
        if (node.Field == "status" && node.Query is TermNode { Term: "all" })
        {
            // Re-parsing is simpler and safer than hand-building AST nodes.
            return LuceneQuery.Parse("(status:active OR status:pending)").Document.Query!;
        }

        return base.Visit(node, context);
    }
}

// Input: "status:all"
// Output: "(status:active OR status:pending)"
```

## Removing Nodes

Return `null` to remove a node (parent must handle this):

```csharp
public class RemoveFieldVisitor : QueryVisitor
{
    private readonly HashSet<string> _fieldsToRemove;

    public RemoveFieldVisitor(params string[] fields)
    {
        _fieldsToRemove = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
    }

    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (_fieldsToRemove.Contains(node.Field ?? ""))
        {
            return null!;
        }

        return base.Visit(node, context);
    }
}
```

## Next Steps

- [Field Mapping](./field-mapping) - Detailed field aliasing
- [Validation](./validation) - Query validation
- [Custom Visitors](./custom-visitors) - Advanced visitor patterns
