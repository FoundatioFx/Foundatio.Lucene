# Field Mapping

Field mapping allows you to create user-friendly aliases for your actual field names. This provides security by hiding internal field structures and improves usability by offering intuitive names.

## Basic Field Mapping

Use `FieldMap` to define aliases:

```csharp
using Foundatio.Lucene;
using Foundatio.Lucene.Visitors;

var fieldMap = new FieldMap
{
    { "user", "account.username" },
    { "email", "account.emailAddress" },
    { "created", "metadata.createdAt" },
    { "updated", "metadata.updatedAt" }
};

var result = LuceneQuery.Parse("user:john AND created:[2024-01-01 TO *]");
await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

var resolved = QueryStringBuilder.ToQueryString(result.Document);
// Returns: "account.username:john AND metadata.createdAt:[2024-01-01 TO *]"
```

## Case Insensitivity

`FieldMap` is case-insensitive by default:

```csharp
var fieldMap = new FieldMap
{
    { "user", "account.username" }
};

// All of these work:
// "user:john"  -> "account.username:john"
// "User:john"  -> "account.username:john"
// "USER:john"  -> "account.username:john"
```

## Hierarchical Field Resolution

For nested field structures, use `ToHierarchicalFieldResolver()`:

```csharp
var fieldMap = new FieldMap
{
    { "data", "payload" },
    { "data.user", "payload.account.username" },
    { "data.created", "payload.metadata.createdAt" }
};

var resolver = fieldMap.ToHierarchicalFieldResolver();
await FieldResolverQueryVisitor.RunAsync(result.Document, resolver);

// "data.user:john" -> "payload.account.username:john"
// "data.status:active" -> "payload.status:active" (partial match on "data")
```

## Validation with Field Mapping

Combine field mapping with validation to restrict allowed fields:

```csharp
var fieldMap = new FieldMap
{
    { "name", "fullName" },
    { "dept", "department.name" },
    { "salary", "compensation.baseSalary" }
};

var validationOptions = new QueryValidationOptions();
validationOptions.AllowedFields.AddRange(fieldMap.Keys);

// First resolve aliases
var result = LuceneQuery.Parse(userQuery);
await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

// Then validate
var validation = await QueryValidator.ValidateAsync(result.Document, validationOptions);
```

## Dynamic Field Mapping

Create field mappings dynamically based on user permissions:

```csharp
public FieldMap GetFieldMapForUser(User user)
{
    var fieldMap = new FieldMap
    {
        { "name", "fullName" },
        { "email", "emailAddress" },
        { "dept", "department.name" }
    };

    // Add sensitive fields only for admins
    if (user.IsAdmin)
    {
        fieldMap.Add("salary", "compensation.baseSalary");
        fieldMap.Add("ssn", "personalInfo.socialSecurityNumber");
    }

    return fieldMap;
}
```

## Custom Field Resolvers

For complex scenarios, implement a custom resolver function:

```csharp
Func<string, string?> customResolver = field =>
{
    // Handle dynamic prefixes
    if (field.StartsWith("custom."))
    {
        return $"customFields.{field[7..]}";
    }

    // Use static mappings
    return field switch
    {
        "name" => "fullName",
        "email" => "emailAddress",
        _ => null  // Return null to keep original field
    };
};

await FieldResolverQueryVisitor.RunAsync(result.Document, customResolver);
```

## Field Mapping in Parsers

Both Entity Framework and Elasticsearch parsers support field mapping:

### Entity Framework

```csharp
var parser = new EntityFrameworkQueryParser();

var fieldMap = new FieldMap
{
    { "name", "FullName" },
    { "dept", "Department.Name" }
};

var filter = parser.BuildFilter<Employee>(query, fieldMap);
```

### Elasticsearch

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    config.FieldMap = new FieldMap
    {
        { "author", "metadata.author" },
        { "date", "metadata.publishedAt" }
    };
});
```

## Exposing Available Fields

Document available fields for API consumers:

```csharp
[HttpGet("search/fields")]
public IActionResult GetSearchFields()
{
    return Ok(new
    {
        Fields = new[]
        {
            new { Name = "name", Description = "Employee full name", Examples = new[] { "name:john", "name:john*" } },
            new { Name = "dept", Description = "Department name", Examples = new[] { "dept:engineering" } },
            new { Name = "hired", Description = "Hire date", Examples = new[] { "hired:[2024-01-01 TO *]", "hired:now-1y" } },
            new { Name = "salary", Description = "Base salary", Examples = new[] { "salary:[50000 TO 100000]" } },
            new { Name = "status", Description = "Employment status", Examples = new[] { "status:active", "status:(active OR leave)" } }
        }
    });
}
```

## Best Practices

### 1. Use Consistent Naming

```csharp
// Good - consistent, lowercase, intuitive
var fieldMap = new FieldMap
{
    { "name", "fullName" },
    { "email", "emailAddress" },
    { "created", "createdAt" },
    { "updated", "updatedAt" }
};

// Avoid - inconsistent naming
var fieldMap = new FieldMap
{
    { "Name", "fullName" },           // Mixed case
    { "user_email", "emailAddress" }, // Snake case
    { "createdDate", "createdAt" }    // Verbose
};
```

### 2. Hide Implementation Details

```csharp
// Good - hides internal structure
var fieldMap = new FieldMap
{
    { "author", "document.metadata.author.fullName" },
    { "date", "document.metadata.timestamps.publishedAt" }
};

// Users write: author:john AND date:[2024-01-01 TO *]
// Instead of: document.metadata.author.fullName:john AND ...
```

### 3. Protect Sensitive Fields

```csharp
// Only expose fields users should access
var publicFieldMap = new FieldMap
{
    { "name", "fullName" },
    { "dept", "department.name" },
    { "location", "office.city" }
};

// Internal fields like SSN, salary, etc. are not exposed
```

### 4. Document Your Mappings

```csharp
/// <summary>
/// Field mappings for the Employee search API.
/// </summary>
/// <remarks>
/// Available fields:
/// - name: Maps to FullName (supports wildcards)
/// - dept: Maps to Department.Name
/// - hired: Maps to HireDate (supports date ranges)
/// - manager: Maps to Manager.FullName
/// </remarks>
public static readonly FieldMap EmployeeFieldMap = new()
{
    { "name", "FullName" },
    { "dept", "Department.Name" },
    { "hired", "HireDate" },
    { "manager", "Manager.FullName" }
};
```

## Next Steps

- [Validation](./validation) - Validate queries
- [Visitors](./visitors) - Custom transformations
- [Entity Framework](./entity-framework) - EF Core integration
