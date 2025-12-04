# Entity Framework Integration

Foundatio.Lucene.EntityFramework converts Lucene query strings to LINQ expressions for use with Entity Framework Core.

## Installation

```bash
dotnet add package Foundatio.Lucene.EntityFramework
```

## Basic Usage

```csharp
using Foundatio.Lucene.EntityFramework;
using Microsoft.EntityFrameworkCore;

var parser = new EntityFrameworkQueryParser();

// Build a LINQ expression from a Lucene query
var filter = parser.BuildFilter<Employee>("name:john AND department:engineering");

// Use with EF Core
var results = await context.Employees
    .Where(filter)
    .ToListAsync();
```

## API Endpoints

Enable dynamic, user-driven queries in your API:

```csharp
[HttpGet("employees")]
public async Task<IActionResult> SearchEmployees([FromQuery] string query)
{
    var parser = new EntityFrameworkQueryParser();

    // User provides: "name:john AND salary:[50000 TO *]"
    var filter = parser.BuildFilter<Employee>(query);

    var results = await _context.Employees
        .Where(filter)
        .ToListAsync();

    return Ok(results);
}
```

## Field Mapping

Map user-friendly field names to entity properties:

```csharp
var parser = new EntityFrameworkQueryParser();

var fieldMap = new FieldMap
{
    { "name", "FullName" },
    { "dept", "Department.Name" },
    { "hired", "HireDate" },
    { "salary", "Compensation.BaseSalary" }
};

// User query: "name:john AND dept:engineering"
var filter = parser.BuildFilter<Employee>(query, fieldMap);
```

## Supported Query Types

### Term Queries

Simple equality matching:

```csharp
// Input
"status:active"

// Generates LINQ equivalent to:
employee => employee.Status == "active"
```

### Phrase Queries

Exact string matching:

```csharp
// Input
"title:\"Senior Engineer\""

// Generates LINQ equivalent to:
employee => employee.Title == "Senior Engineer"
```

### Wildcard Queries

Pattern matching with wildcards:

```csharp
// Input
"name:john*"

// Generates LINQ equivalent to:
employee => employee.Name.StartsWith("john")

// Input
"email:*@company.com"

// Generates LINQ equivalent to:
employee => employee.Email.EndsWith("@company.com")

// Input
"code:A*Z"

// Generates LINQ equivalent to:
employee => employee.Code.StartsWith("A") && employee.Code.EndsWith("Z")
```

### Range Queries

Comparison operators:

```csharp
// Input
"salary:[50000 TO 100000]"

// Generates LINQ equivalent to:
employee => employee.Salary >= 50000 && employee.Salary <= 100000

// Input
"age:{21 TO *}"

// Generates LINQ equivalent to:
employee => employee.Age > 21
```

### Boolean Operators

Complex boolean logic:

```csharp
// Input
"status:active AND department:(engineering OR sales)"

// Generates LINQ equivalent to:
employee => employee.Status == "active" && 
    (employee.Department == "engineering" || employee.Department == "sales")

// Input
"active:true NOT role:admin"

// Generates LINQ equivalent to:
employee => employee.Active == true && employee.Role != "admin"
```

### Nested Properties

Access nested object properties:

```csharp
// Input
"department.name:engineering"

// Generates LINQ equivalent to:
employee => employee.Department.Name == "engineering"

// With field mapping
var fieldMap = new FieldMap { { "dept", "Department.Name" } };
// Input: "dept:engineering"
```

### Exists/Missing Queries

Check for null values:

```csharp
// Input
"_exists_:email"

// Generates LINQ equivalent to:
employee => employee.Email != null

// Input
"_missing_:phone"

// Generates LINQ equivalent to:
employee => employee.Phone == null
```

## Type Handling

The parser automatically handles type conversions based on entity property types:

### Numeric Properties

```csharp
// For int/decimal properties
"salary:50000"        // Converts "50000" to numeric
"salary:[50000 TO *]" // Range comparison
```

### Boolean Properties

```csharp
// For bool properties
"active:true"
"active:false"
```

### DateTime Properties

```csharp
// For DateTime properties
"hireDate:2024-01-01"
"hireDate:[2024-01-01 TO 2024-12-31]"
```

### Enum Properties

```csharp
// For enum properties
"status:Active"  // Matches enum value name
"status:1"       // Or numeric value
```

## Configuration

### Custom Configuration

```csharp
var parser = new EntityFrameworkQueryParser();

// The parser automatically discovers entity metadata from EF Core
// when used with a DbContext-backed entity type
```

### Validation

Add query validation to prevent expensive or dangerous queries:

```csharp
var options = new QueryValidationOptions
{
    AllowLeadingWildcards = false,  // Prevent slow queries like "*suffix"
    MaxDepth = 5                    // Limit query complexity
};
options.AllowedFields.Add("name");
options.AllowedFields.Add("department");

// Validate before executing
var parseResult = LuceneQuery.Parse(userQuery);
var validation = await QueryValidator.ValidateAsync(parseResult.Document, options);

if (!validation.IsValid)
{
    return BadRequest(validation.Message);
}

var filter = parser.BuildFilter<Employee>(userQuery);
```

## Error Handling

Handle parse and execution errors gracefully:

```csharp
try
{
    var parseResult = LuceneQuery.Parse(userQuery);
    
    if (!parseResult.IsSuccess)
    {
        var errors = string.Join("; ", parseResult.Errors.Select(e => e.Message));
        return BadRequest($"Invalid query: {errors}");
    }

    var filter = parser.BuildFilter<Employee>(userQuery);
    var results = await _context.Employees.Where(filter).ToListAsync();
    
    return Ok(results);
}
catch (InvalidOperationException ex)
{
    return BadRequest($"Query error: {ex.Message}");
}
```

## Performance Considerations

### Indexing

Ensure database indexes exist for commonly queried fields:

```csharp
modelBuilder.Entity<Employee>()
    .HasIndex(e => e.Name);

modelBuilder.Entity<Employee>()
    .HasIndex(e => e.Status);

modelBuilder.Entity<Employee>()
    .HasIndex(e => e.Department);
```

### Pagination

Always paginate results for user queries:

```csharp
var filter = parser.BuildFilter<Employee>(query);

var results = await _context.Employees
    .Where(filter)
    .OrderBy(e => e.Name)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

### Query Logging

Log queries for debugging and monitoring:

```csharp
var queryString = QueryStringBuilder.ToQueryString(parseResult.Document);
_logger.LogInformation("Executing query: {Query}", queryString);
```

## Complete Example

```csharp
[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EntityFrameworkQueryParser _parser;
    private readonly ILogger<EmployeesController> _logger;

    private static readonly FieldMap FieldMap = new()
    {
        { "name", "FullName" },
        { "dept", "Department.Name" },
        { "hired", "HireDate" },
        { "salary", "Compensation.BaseSalary" },
        { "manager", "Manager.FullName" }
    };

    private static readonly QueryValidationOptions ValidationOptions = new()
    {
        AllowLeadingWildcards = false
    };

    public EmployeesController(AppDbContext context, ILogger<EmployeesController> logger)
    {
        _context = context;
        _parser = new EntityFrameworkQueryParser();
        _logger = logger;
        
        ValidationOptions.AllowedFields.AddRange(FieldMap.Keys);
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string q = "*:*",
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        // Parse the query
        var parseResult = LuceneQuery.Parse(q);
        if (!parseResult.IsSuccess)
        {
            return BadRequest(new { 
                Error = "Invalid query syntax",
                Details = parseResult.Errors.Select(e => e.Message)
            });
        }

        // Resolve field aliases
        await FieldResolverQueryVisitor.RunAsync(parseResult.Document, FieldMap);

        // Validate the query
        var validation = await QueryValidator.ValidateAsync(
            parseResult.Document, 
            ValidationOptions
        );
        if (!validation.IsValid)
        {
            return BadRequest(new { Error = validation.Message });
        }

        // Build and execute
        var resolvedQuery = QueryStringBuilder.ToQueryString(parseResult.Document);
        _logger.LogInformation("Searching employees with: {Query}", resolvedQuery);

        var filter = _parser.BuildFilter<Employee>(resolvedQuery);

        var total = await _context.Employees.CountAsync(filter);
        var results = await _context.Employees
            .Where(filter)
            .OrderBy(e => e.FullName)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(e => new EmployeeDto(e))
            .ToListAsync();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = size,
            Results = results
        });
    }
}
```

## Next Steps

- [Query Syntax](./query-syntax) - All supported query syntax
- [Field Mapping](./field-mapping) - Advanced field aliasing
- [Validation](./validation) - Query validation options
