# Custom Visitors

This guide covers advanced patterns for creating custom visitors to transform, analyze, and validate queries.

## Visitor Basics

All visitors extend `QueryNodeVisitor` and override methods for specific node types:

```csharp
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

public class MyVisitor : QueryNodeVisitor
{
    public override async Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        // Transform the node
        node.Term = node.Term?.ToLowerInvariant();
        
        // Return the (possibly modified) node
        return node;
    }
}
```

## Visiting Child Nodes

Call `base.VisitAsync()` to visit child nodes:

```csharp
public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
{
    // Process this node first
    node.Field = node.Field?.ToLowerInvariant();
    
    // Then visit children (the field's value)
    return await base.VisitAsync(node, context);
}
```

::: warning
If you don't call `base.VisitAsync()`, child nodes won't be visited!
:::

## Common Patterns

### Transformation Visitor

Transform nodes in place:

```csharp
public class NormalizeTermsVisitor : QueryNodeVisitor
{
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        // Normalize whitespace and case
        node.Term = node.Term?.Trim().ToLowerInvariant();
        return Task.FromResult<QueryNode>(node);
    }

    public override Task<QueryNode> VisitAsync(PhraseNode node, IQueryVisitorContext context)
    {
        // Normalize phrase
        node.Phrase = node.Phrase?.Trim();
        return Task.FromResult<QueryNode>(node);
    }
}
```

### Collection Visitor

Collect information from the tree:

```csharp
public class FieldCollectorVisitor : QueryNodeVisitor
{
    public HashSet<string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Terms { get; } = new(StringComparer.OrdinalIgnoreCase);

    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field != null)
        {
            Fields.Add(node.Field);
        }
        
        return await base.VisitAsync(node, context);
    }

    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        if (node.Term != null)
        {
            Terms.Add(node.Term);
        }
        
        return Task.FromResult<QueryNode>(node);
    }
}

// Usage
var collector = new FieldCollectorVisitor();
await collector.AcceptAsync(result.Document, new QueryVisitorContext());
Console.WriteLine($"Fields: {string.Join(", ", collector.Fields)}");
Console.WriteLine($"Terms: {string.Join(", ", collector.Terms)}");
```

### Replacement Visitor

Replace nodes with different nodes:

```csharp
public class StatusExpanderVisitor : QueryNodeVisitor
{
    public override Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Expand status:all to (status:active OR status:pending OR status:review)
        if (node.Field == "status" && node.Value is TermNode term && term.Term == "all")
        {
            return Task.FromResult<QueryNode>(new GroupNode
            {
                Child = new BooleanQueryNode
                {
                    Left = new BooleanQueryNode
                    {
                        Left = CreateStatusNode("active"),
                        Operator = QueryOperator.Or,
                        Right = CreateStatusNode("pending")
                    },
                    Operator = QueryOperator.Or,
                    Right = CreateStatusNode("review")
                }
            });
        }
        
        return base.VisitAsync(node, context);
    }

    private static FieldQueryNode CreateStatusNode(string status) => new()
    {
        Field = "status",
        Value = new TermNode { Term = status }
    };
}
```

### Validation Visitor

Collect validation errors:

```csharp
public class ValidationVisitor : QueryNodeVisitor
{
    private readonly List<ValidationError> _errors = new();
    public IReadOnlyList<ValidationError> Errors => _errors;
    public bool IsValid => _errors.Count == 0;

    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
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
        
        return Task.FromResult<QueryNode>(node);
    }

    public override async Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
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
        
        return await base.VisitAsync(node, context);
    }
}

public class ValidationError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Value { get; set; }
}
```

### Async Visitor

Perform async operations during visitation:

```csharp
public class AsyncIncludeResolver : QueryNodeVisitor
{
    private readonly IQueryRepository _repository;

    public AsyncIncludeResolver(IQueryRepository repository)
    {
        _repository = repository;
    }

    public override async Task<QueryNode> VisitAsync(IncludeNode node, IQueryVisitorContext context)
    {
        if (node.Name == null)
        {
            return node;
        }

        // Load the saved query from the database
        var savedQuery = await _repository.GetQueryAsync(node.Name);
        if (savedQuery == null)
        {
            throw new InvalidOperationException($"Saved query '{node.Name}' not found");
        }

        // Parse the saved query
        var result = LuceneQuery.Parse(savedQuery);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Invalid saved query '{node.Name}'");
        }

        // Return the parsed document wrapped in a group
        return new GroupNode { Child = result.Document };
    }
}
```

## Using Visitor Context

The `IQueryVisitorContext` allows passing state:

```csharp
public class ContextAwareVisitor : QueryNodeVisitor
{
    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Get user from context
        var user = context.GetValue<User>("CurrentUser");
        
        // Check if user can access this field
        var allowedFields = context.GetValue<HashSet<string>>("AllowedFields");
        if (allowedFields != null && !allowedFields.Contains(node.Field ?? ""))
        {
            throw new UnauthorizedAccessException($"Field '{node.Field}' is not accessible");
        }
        
        return await base.VisitAsync(node, context);
    }
}

// Usage
var context = new QueryVisitorContext();
context.SetValue("CurrentUser", currentUser);
context.SetValue("AllowedFields", new HashSet<string> { "title", "author", "date" });

await visitor.AcceptAsync(document, context);
```

## Composing Visitors

### Sequential Composition

Run visitors in sequence:

```csharp
var visitors = new List<QueryNodeVisitor>
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
    current = await visitor.AcceptAsync(current, context);
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

await chain.AcceptAsync(document, context);
```

## Best Practices

### 1. Keep Visitors Focused

```csharp
// Good: Single responsibility
public class LowercaseFieldsVisitor : QueryNodeVisitor { }
public class ValidateFieldsVisitor : QueryNodeVisitor { }
public class ExpandAliasesVisitor : QueryNodeVisitor { }

// Bad: Too many responsibilities
public class DoEverythingVisitor : QueryNodeVisitor { }
```

### 2. Make Visitors Stateless When Possible

```csharp
// Good: Stateless, reusable
public class LowercaseVisitor : QueryNodeVisitor
{
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        node.Term = node.Term?.ToLowerInvariant();
        return Task.FromResult<QueryNode>(node);
    }
}

// If state is needed, use context
public class StatefulVisitor : QueryNodeVisitor
{
    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        var count = context.GetValue<int>("TermCount");
        context.SetValue("TermCount", count + 1);
        return Task.FromResult<QueryNode>(node);
    }
}
```

### 3. Handle Null Values

```csharp
public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
{
    // Always check for null
    if (node.Term != null)
    {
        node.Term = node.Term.ToLowerInvariant();
    }
    
    return Task.FromResult<QueryNode>(node);
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
public class StatusExpanderVisitor : QueryNodeVisitor
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
    await visitor.AcceptAsync(result.Document, new QueryVisitorContext());
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
    await visitor.AcceptAsync(result.Document, new QueryVisitorContext());
    
    // Assert
    Assert.False(visitor.IsValid);
    Assert.Contains(visitor.Errors, e => e.Code == "LEADING_WILDCARD");
}
```

## Next Steps

- [Visitors](./visitors) - Built-in visitors
- [Validation](./validation) - Query validation
- [Configuration](./configuration) - Parser configuration
