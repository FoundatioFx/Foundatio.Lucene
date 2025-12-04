# Query Validation

Query validation allows you to restrict what users can query, preventing expensive or dangerous operations and enforcing business rules.

## Basic Validation

Use `QueryValidationOptions` and `QueryValidator`:

```csharp
using Foundatio.Lucene;

var result = LuceneQuery.Parse("*wildcard AND title:test");

var options = new QueryValidationOptions
{
    AllowLeadingWildcards = false
};
options.AllowedFields.Add("title");
options.AllowedFields.Add("status");

var validationResult = await QueryValidator.ValidateAsync(result.Document, options);

if (!validationResult.IsValid)
{
    Console.WriteLine(validationResult.Message);
    // "Leading wildcards are not allowed"
}
```

## Validation Options

### AllowLeadingWildcards

Control whether wildcards can appear at the start of terms:

```csharp
var options = new QueryValidationOptions
{
    AllowLeadingWildcards = false  // Disallow *suffix patterns
};

// Allowed:  prefix*, mid*dle
// Blocked:  *suffix, *
```

::: tip
Leading wildcards cause expensive full-index scans in most databases and search engines.
:::

### AllowedFields

Restrict which fields can be queried:

```csharp
var options = new QueryValidationOptions();
options.AllowedFields.Add("title");
options.AllowedFields.Add("author");
options.AllowedFields.Add("status");
options.AllowedFields.Add("date");

// Allowed: title:hello, author:john
// Blocked: password:*, salary:[* TO *]
```

### DisallowedFields

Explicitly block certain fields:

```csharp
var options = new QueryValidationOptions();
options.DisallowedFields.Add("password");
options.DisallowedFields.Add("ssn");
options.DisallowedFields.Add("internalId");

// Even if AllowedFields is empty, these fields are blocked
```

### AllowWildcardOnlyQueries

Control whether `*` or `*:*` queries are allowed:

```csharp
var options = new QueryValidationOptions
{
    AllowWildcardOnlyQueries = false
};

// Blocked: *, *:*
// Allowed: title:*, title:hello*
```

## Using with Field Mapping

When using field mapping, validate against the user-facing field names:

```csharp
var fieldMap = new FieldMap
{
    { "name", "fullName" },
    { "dept", "department.name" },
    { "hired", "hireDate" }
};

var validationOptions = new QueryValidationOptions
{
    AllowLeadingWildcards = false
};
// Validate the aliased names
validationOptions.AllowedFields.AddRange(fieldMap.Keys);

// Parse and validate
var result = LuceneQuery.Parse(userQuery);

var validation = await QueryValidator.ValidateAsync(result.Document, validationOptions);
if (!validation.IsValid)
{
    return BadRequest(validation.Message);
}

// Then resolve and execute
await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);
var filter = parser.BuildFilter<Employee>(QueryStringBuilder.ToQueryString(result.Document));
```

## Custom Validation

For complex validation rules, create a custom visitor:

```csharp
public class CustomValidationVisitor : QueryNodeVisitor
{
    private readonly List<string> _errors = new();

    public IReadOnlyList<string> Errors => _errors;
    public bool IsValid => _errors.Count == 0;

    public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
    {
        // Validate date ranges don't span more than 1 year
        if (node.Min != null && node.Max != null)
        {
            if (DateTime.TryParse(node.Min, out var minDate) &&
                DateTime.TryParse(node.Max, out var maxDate))
            {
                if ((maxDate - minDate).TotalDays > 365)
                {
                    _errors.Add("Date ranges cannot span more than 1 year");
                }
            }
        }

        return base.VisitAsync(node, context);
    }

    public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
    {
        // Validate specific field requirements
        if (node.Field == "email" && node.Value is TermNode term)
        {
            if (!term.Term?.Contains("@") == true)
            {
                _errors.Add("Email searches must contain @");
            }
        }

        return await base.VisitAsync(node, context);
    }
}

// Usage
var validator = new CustomValidationVisitor();
await validator.AcceptAsync(result.Document, new QueryVisitorContext());

if (!validator.IsValid)
{
    return BadRequest(new { Errors = validator.Errors });
}
```

## Validation in API Endpoints

Complete validation example for an API:

```csharp
[HttpGet("search")]
public async Task<IActionResult> Search([FromQuery] string q)
{
    // 1. Parse the query
    var parseResult = LuceneQuery.Parse(q);
    if (!parseResult.IsSuccess)
    {
        return BadRequest(new
        {
            Error = "Invalid query syntax",
            Details = parseResult.Errors.Select(e => new
            {
                e.Message,
                e.Line,
                e.Column
            })
        });
    }

    // 2. Validate the query
    var validation = await QueryValidator.ValidateAsync(
        parseResult.Document,
        _validationOptions
    );
    if (!validation.IsValid)
    {
        return BadRequest(new { Error = validation.Message });
    }

    // 3. Run custom validation
    var customValidator = new CustomValidationVisitor();
    await customValidator.AcceptAsync(parseResult.Document, new QueryVisitorContext());
    if (!customValidator.IsValid)
    {
        return BadRequest(new { Errors = customValidator.Errors });
    }

    // 4. Resolve fields and execute
    await FieldResolverQueryVisitor.RunAsync(parseResult.Document, _fieldMap);
    var filter = _parser.BuildFilter<Document>(
        QueryStringBuilder.ToQueryString(parseResult.Document)
    );

    var results = await _context.Documents.Where(filter).ToListAsync();
    return Ok(results);
}
```

## Error Messages

Customize validation error messages:

```csharp
public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public List<ValidationError> Errors { get; set; } = new();
}

public class ValidationError
{
    public string Field { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

// Return user-friendly errors
return BadRequest(new ValidationResult
{
    IsValid = false,
    Message = "Query validation failed",
    Errors = new List<ValidationError>
    {
        new() { Field = "salary", Code = "FIELD_NOT_ALLOWED", Message = "The 'salary' field is not searchable" },
        new() { Field = "*", Code = "LEADING_WILDCARD", Message = "Searches cannot start with a wildcard" }
    }
});
```

## Rate Limiting Complex Queries

Track query complexity for rate limiting:

```csharp
public class QueryComplexityVisitor : QueryNodeVisitor
{
    public int Complexity { get; private set; } = 0;

    public override Task<QueryNode> VisitAsync(TermNode node, IQueryVisitorContext context)
    {
        Complexity += 1;
        
        // Wildcards are more expensive
        if (node.Term?.Contains('*') == true || node.Term?.Contains('?') == true)
        {
            Complexity += 5;
            
            // Leading wildcards are very expensive
            if (node.Term?.StartsWith('*') == true || node.Term?.StartsWith('?') == true)
            {
                Complexity += 20;
            }
        }
        
        return base.VisitAsync(node, context);
    }

    public override Task<QueryNode> VisitAsync(RangeNode node, IQueryVisitorContext context)
    {
        Complexity += 3;
        return base.VisitAsync(node, context);
    }

    public override Task<QueryNode> VisitAsync(RegexNode node, IQueryVisitorContext context)
    {
        Complexity += 10;
        return base.VisitAsync(node, context);
    }
}

// Usage
var complexityVisitor = new QueryComplexityVisitor();
await complexityVisitor.AcceptAsync(result.Document, new QueryVisitorContext());

if (complexityVisitor.Complexity > 50)
{
    return BadRequest("Query is too complex");
}
```

## Best Practices

### 1. Always Validate User Input

```csharp
// Always validate before executing
var parseResult = LuceneQuery.Parse(userQuery);
if (!parseResult.IsSuccess) { /* handle error */ }

var validation = await QueryValidator.ValidateAsync(parseResult.Document, options);
if (!validation.IsValid) { /* handle error */ }
```

### 2. Whitelist Over Blacklist

```csharp
// Better: Explicitly allow fields
options.AllowedFields.Add("title");
options.AllowedFields.Add("author");

// Worse: Try to block sensitive fields
options.DisallowedFields.Add("password");  // Easy to forget fields
```

### 3. Combine Multiple Validations

```csharp
// Parse error + Standard validation + Custom validation
var pipeline = new List<Func<QueryDocument, Task<ValidationResult>>>
{
    doc => QueryValidator.ValidateAsync(doc, standardOptions),
    doc => ValidateDateRanges(doc),
    doc => ValidateUserPermissions(doc, currentUser)
};

foreach (var validator in pipeline)
{
    var result = await validator(document);
    if (!result.IsValid) return BadRequest(result.Message);
}
```

### 4. Log Validation Failures

```csharp
if (!validation.IsValid)
{
    _logger.LogWarning("Query validation failed for user {UserId}: {Query} - {Error}",
        currentUser.Id, userQuery, validation.Message);
    
    return BadRequest(validation.Message);
}
```

## Next Steps

- [Visitors](./visitors) - Custom validation visitors
- [Field Mapping](./field-mapping) - Secure field aliasing
- [Configuration](./configuration) - Parser configuration
