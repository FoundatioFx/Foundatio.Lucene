# Custom Visitors

This guide covers advanced patterns for creating custom visitors to transform, analyze, and validate queries.

## Visitor Basics

All visitors extend `QueryVisitor` and override methods for specific node types:

```csharp
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

public class MyVisitor : QueryVisitor
{
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        // Transform the node
        node.Term = node.Term?.ToLowerInvariant();
        
        // Return the (possibly modified) node
        return node;
    }
}
```

## Visiting Child Nodes

Call `base.Visit()` to visit child nodes:

```csharp
protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
{
    // Process this node first
    node.Field = node.Field?.ToLowerInvariant();
    
    // Then visit children (the field's value)
    return base.Visit(node, context);
}
```

::: warning
If you don't call `base.Visit()`, child nodes won't be visited!
:::

## Common Patterns

### Transformation Visitor

Transform nodes in place:

```csharp
public class NormalizeTermsVisitor : QueryVisitor
{
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        // Normalize whitespace and case
        node.Term = node.Term?.Trim().ToLowerInvariant();
        return node;
    }

    protected override QueryNode Visit(PhraseNode node, IQueryVisitorContext context)
    {
        // Normalize phrase
        node.Phrase = node.Phrase?.Trim();
        return node;
    }
}
```

### Collection Visitor

Collect information from the tree:

```csharp
public class FieldCollectorVisitor : QueryVisitor
{
    public HashSet<string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Terms { get; } = new(StringComparer.OrdinalIgnoreCase);

    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field != null)
        {
            Fields.Add(node.Field);
        }
        
        return base.Visit(node, context);
    }

    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        if (node.Term != null)
        {
            Terms.Add(node.Term);
        }
        
        return node;
    }
}

// Usage
var collector = new FieldCollectorVisitor();
collector.Accept(result.Document, new QueryVisitorContext());
Console.WriteLine($"Fields: {string.Join(", ", collector.Fields)}");
Console.WriteLine($"Terms: {string.Join(", ", collector.Terms)}");
```

### Replacement Visitor

Replace nodes with different nodes:

```csharp
public class StatusExpanderVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Expand status:all to (status:active OR status:pending OR status:review).
        // Re-parsing the expansion is simpler and safer than hand-building AST nodes.
        if (node.Field == "status" && node.Query is TermNode { Term: "all" })
        {
            return LuceneQuery.Parse("(status:active OR status:pending OR status:review)").Document.Query!;
        }

        return base.Visit(node, context);
    }
}
```

### Validation Visitor

Collect validation errors:

```csharp
public class ValidationVisitor : QueryVisitor
{
    private readonly List<ValidationError> _errors = new();
    public IReadOnlyList<ValidationError> Errors => _errors;
    public bool IsValid => _errors.Count == 0;

    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        if (node.Term?.StartsWith('*') == true)
        {
            _errors.Add(new ValidationError
            {
                Code = "LEADING_WILDCARD",
                Message = "Leading wildcards are not allowed",
                Value = node.Term
            });
        }
        
        return node;
    }

    protected override QueryNode Visit(RangeNode node, IQueryVisitorContext context)
    {
        // Validate range bounds
        if (node.Min != null && node.Max != null)
        {
            if (DateTime.TryParse(node.Min, out var min) && 
                DateTime.TryParse(node.Max, out var max))
            {
                if (min > max)
                {
                    _errors.Add(new ValidationError
                    {
                        Code = "INVALID_RANGE",
                        Message = "Range minimum cannot be greater than maximum"
                    });
                }
            }
        }
        
        return base.Visit(node, context);
    }
}

public class ValidationError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Value { get; set; }
}
```

### Async work: resolve outside the pipeline

The visitor pipeline is **synchronous by design** — there is no `VisitAsync`. When a transformation
needs data from an async source (for example loading saved queries for `@include`), do the async
work *before* running the pipeline and pass the results in. The built-in include expansion takes a
pre-resolved dictionary:

```csharp
// Resolve saved queries asynchronously up front...
var names = new[] { "active-filter", "recent" };
var includes = await _repository.GetSavedQueriesAsync(names); // your async lookup -> dictionary

// ...then expand synchronously.
result.Document.ExpandIncludes(includes);
```

## Using Visitor Context

The `IQueryVisitorContext` allows passing state:

```csharp
public class ContextAwareVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Get user from context
        var user = context.GetValue<User>("CurrentUser");
        
        // Check if user can access this field
        var allowedFields = context.GetValue<HashSet<string>>("AllowedFields");
        if (allowedFields != null && !allowedFields.Contains(node.Field ?? ""))
        {
            throw new UnauthorizedAccessException($"Field '{node.Field}' is not accessible");
        }
        
        return base.Visit(node, context);
    }
}

// Usage
var context = new QueryVisitorContext();
context.SetValue("CurrentUser", currentUser);
context.SetValue("AllowedFields", new HashSet<string> { "title", "author", "date" });

visitor.Accept(document, context);
```

## Composing Visitors

### Sequential Composition

Run visitors in sequence:

```csharp
var visitors = new List<QueryVisitor>
{
    new NormalizeTermsVisitor(),
    new FieldResolverQueryVisitor(fieldMap),
    new DateMathEvaluatorVisitor(),
    new ValidationVisitor()
};

var context = new QueryVisitorContext();
QueryNode current = document;

foreach (var visitor in visitors)
{
    current = visitor.Accept(current, context);
}
```

### Chained Visitor

Use `ChainedQueryVisitor` with priorities:

```csharp
var chain = new ChainedQueryVisitor()
    .AddVisitor(new NormalizeTermsVisitor(), priority: 10)
    .AddVisitor(new FieldResolverQueryVisitor(fieldMap), priority: 20)
    .AddVisitor(new DateMathEvaluatorVisitor(), priority: 30)
    .AddVisitor(new ValidationVisitor(), priority: 100);

chain.Accept(document, context);
```

## Best Practices

### 1. Keep Visitors Focused

```csharp
// Good: Single responsibility
public class LowercaseFieldsVisitor : QueryVisitor { }
public class ValidateFieldsVisitor : QueryVisitor { }
public class ExpandAliasesVisitor : QueryVisitor { }

// Bad: Too many responsibilities
public class DoEverythingVisitor : QueryVisitor { }
```

### 2. Make Visitors Stateless When Possible

```csharp
// Good: Stateless, reusable
public class LowercaseVisitor : QueryVisitor
{
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        node.Term = node.Term?.ToLowerInvariant();
        return node;
    }
}

// If state is needed, use context
public class StatefulVisitor : QueryVisitor
{
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        var count = context.GetValue<int>("TermCount");
        context.SetValue("TermCount", count + 1);
        return node;
    }
}
```

### 3. Handle Null Values

```csharp
protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
{
    // Always check for null
    if (node.Term != null)
    {
        node.Term = node.Term.ToLowerInvariant();
    }
    
    return node;
}
```

### 4. Document Your Visitors

```csharp
/// <summary>
/// Expands status aliases to their full form.
/// </summary>
/// <remarks>
/// Transformations:
/// - status:all -> (status:active OR status:pending OR status:review)
/// - status:closed -> (status:completed OR status:cancelled)
/// </remarks>
public class StatusExpanderVisitor : QueryVisitor
{
    // ...
}
```

## Testing Visitors

```csharp
[Fact]
public async Task LowercaseVisitor_LowercasesTerms()
{
    // Arrange
    var result = LuceneQuery.Parse("Title:HELLO");
    var visitor = new LowercaseTermsVisitor();
    
    // Act
    visitor.Accept(result.Document, new QueryVisitorContext());
    var output = QueryStringBuilder.ToQueryString(result.Document);
    
    // Assert
    Assert.Equal("title:hello", output);
}

[Fact]
public async Task ValidationVisitor_RejectsLeadingWildcards()
{
    // Arrange
    var result = LuceneQuery.Parse("*invalid");
    var visitor = new ValidationVisitor();
    
    // Act
    visitor.Accept(result.Document, new QueryVisitorContext());
    
    // Assert
    Assert.False(visitor.IsValid);
    Assert.Contains(visitor.Errors, e => e.Code == "LEADING_WILDCARD");
}
```

## Next Steps

- [Visitors](./visitors) - Built-in visitors
- [Validation](./validation) - Query validation
- [Configuration](./configuration) - Parser configuration
