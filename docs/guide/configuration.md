# Configuration

This guide covers configuration options for the various components of Foundatio.Lucene.

## Core Parser

The core parser is stateless and doesn't require configuration:

```csharp
var result = LuceneQuery.Parse("title:hello AND status:active");
```

## Entity Framework Parser

The `EntityFrameworkQueryParser` configuration:

```csharp
var parser = new EntityFrameworkQueryParser();

// Build a filter with field mapping
var fieldMap = new FieldMap
{
    { "name", "FullName" },
    { "dept", "Department.Name" }
};

var filter = parser.BuildFilter<Employee>(query, fieldMap);
```

## Elasticsearch Parser

The `ElasticsearchQueryParser` has extensive configuration options:

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    // Scoring configuration
    config.UseScoring = true;

    // Default fields for unfielded terms
    config.DefaultFields = ["title", "content", "description"];

    // Default boolean operator
    config.DefaultOperator = QueryOperator.And;

    // Field aliasing
    config.FieldMap = new FieldMap
    {
        { "author", "metadata.author" },
        { "date", "metadata.publishedAt" }
    };

    // Geo field detection
    config.IsGeoPointField = field => 
        field == "location" || 
        field.EndsWith("_geo");

    // Date field detection
    config.IsDateField = field =>
        field.EndsWith("date") ||
        field.EndsWith("At") ||
        field.EndsWith("timestamp");

    // Timezone for date queries
    config.DefaultTimeZone = "America/Chicago";

    // Geo location resolver (for named locations)
    config.GeoLocationResolver = async name =>
    {
        var coords = await _geocodingService.ResolveAsync(name);
        return coords != null ? $"{coords.Lat},{coords.Lon}" : null;
    };

    // Include resolver (for @include syntax)
    config.IncludeResolver = async name =>
    {
        return await _savedQueryService.GetQueryAsync(name);
    };

    // Validation options
    config.ValidationOptions = new QueryValidationOptions
    {
        AllowLeadingWildcards = false
    };
});
```

### Configuration Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UseScoring` | `bool` | `false` | Use match queries (scoring) vs term queries (filtering) |
| `DefaultFields` | `string[]?` | `null` | Fields to search for unfielded terms |
| `DefaultOperator` | `QueryOperator` | `Or` | Default boolean operator for implicit combinations |
| `FieldMap` | `FieldMap?` | `null` | Field name mappings |
| `IsGeoPointField` | `Func<string, bool>?` | `null` | Function to detect geo_point fields |
| `IsDateField` | `Func<string, bool>?` | `null` | Function to detect date fields |
| `DefaultTimeZone` | `string?` | `null` | Default timezone for date range queries |
| `GeoLocationResolver` | `Func<string, Task<string?>>?` | `null` | Async function to resolve location names to coordinates |
| `IncludeResolver` | `IncludeResolver?` | `null` | Function to resolve @include references |
| `ValidationOptions` | `QueryValidationOptions?` | `null` | Query validation options |

## Validation Options

Configure query validation:

```csharp
var options = new QueryValidationOptions
{
    // Wildcard restrictions
    AllowLeadingWildcards = false,
    AllowWildcardOnlyQueries = false,
    
    // Field restrictions
    AllowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "title", "author", "status", "date"
    },
    
    DisallowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "ssn", "internalId"
    }
};
```

### Validation Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AllowLeadingWildcards` | `bool` | `true` | Allow patterns like `*suffix` |
| `AllowWildcardOnlyQueries` | `bool` | `true` | Allow `*` or `*:*` queries |
| `AllowedFields` | `HashSet<string>` | empty | Whitelist of allowed fields |
| `DisallowedFields` | `HashSet<string>` | empty | Blacklist of disallowed fields |

## Field Map

Configure field aliasing:

```csharp
var fieldMap = new FieldMap
{
    { "user", "account.username" },
    { "email", "account.emailAddress" },
    { "created", "metadata.createdAt" }
};

// Case-insensitive by default
// "User:john" -> "account.username:john"
// "USER:john" -> "account.username:john"
```

### Hierarchical Resolution

For nested field structures:

```csharp
var fieldMap = new FieldMap
{
    { "data", "payload" },
    { "data.user", "payload.account.username" }
};

var resolver = fieldMap.ToHierarchicalFieldResolver();
await FieldResolverQueryVisitor.RunAsync(document, resolver);

// "data.user:john" -> "payload.account.username:john"
// "data.status:active" -> "payload.status:active"
```

## Include Resolver

Configure @include syntax resolution:

```csharp
IncludeResolver resolver = async name =>
{
    // Load from database
    var savedQuery = await _db.SavedQueries
        .Where(q => q.Name == name)
        .Select(q => q.QueryText)
        .FirstOrDefaultAsync();
    
    return savedQuery;
};

// Use in Elasticsearch parser
var parser = new ElasticsearchQueryParser(config =>
{
    config.IncludeResolver = resolver;
});

// Or with IncludeVisitor directly
await IncludeVisitor.RunAsync(document, resolver);
```

## Visitor Configuration

Configure visitor chains:

```csharp
var visitors = new ChainedQueryVisitor()
    .AddVisitor(new FieldResolverQueryVisitor(fieldMap), priority: 10)
    .AddVisitor(new IncludeVisitor(includeResolver), priority: 20)
    .AddVisitor(new DateMathEvaluatorVisitor(), priority: 30)
    .AddVisitor(new CustomTransformVisitor(), priority: 50)
    .AddVisitor(new ValidationVisitor(), priority: 100);

var context = new QueryVisitorContext();
await visitors.AcceptAsync(document, context);
```

## Dependency Injection

Register parsers with DI:

```csharp
// Program.cs or Startup.cs
services.AddSingleton<EntityFrameworkQueryParser>();

services.AddSingleton(sp => new ElasticsearchQueryParser(config =>
{
    config.UseScoring = true;
    config.DefaultFields = ["title", "content"];
    config.IncludeResolver = sp.GetRequiredService<ISavedQueryService>().GetQueryAsync;
    config.GeoLocationResolver = sp.GetRequiredService<IGeocodingService>().ResolveAsync;
    config.ValidationOptions = new QueryValidationOptions
    {
        AllowLeadingWildcards = false
    };
}));

// In controllers/services
public class SearchController
{
    private readonly ElasticsearchQueryParser _parser;
    
    public SearchController(ElasticsearchQueryParser parser)
    {
        _parser = parser;
    }
}
```

## Environment-Specific Configuration

Use configuration files for environment-specific settings:

```csharp
// appsettings.json
{
    "Search": {
        "UseScoring": true,
        "DefaultFields": ["title", "content"],
        "DefaultTimeZone": "America/Chicago",
        "AllowLeadingWildcards": false
    }
}

// Configuration
services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var searchConfig = config.GetSection("Search");

    return new ElasticsearchQueryParser(parserConfig =>
    {
        parserConfig.UseScoring = searchConfig.GetValue<bool>("UseScoring");
        parserConfig.DefaultFields = searchConfig.GetSection("DefaultFields").Get<string[]>();
        parserConfig.DefaultTimeZone = searchConfig.GetValue<string>("DefaultTimeZone");
        parserConfig.ValidationOptions = new QueryValidationOptions
        {
            AllowLeadingWildcards = searchConfig.GetValue<bool>("AllowLeadingWildcards")
        };
    });
});
```

## Best Practices

### 1. Centralize Configuration

```csharp
public static class SearchConfiguration
{
    public static readonly FieldMap FieldMap = new()
    {
        { "name", "fullName" },
        { "email", "emailAddress" }
    };

    public static readonly QueryValidationOptions ValidationOptions = new()
    {
        AllowLeadingWildcards = false
    };

    public static ElasticsearchQueryParser CreateParser()
    {
        return new ElasticsearchQueryParser(config =>
        {
            config.FieldMap = FieldMap;
            config.ValidationOptions = ValidationOptions;
        });
    }
}
```

### 2. Use Constants for Field Names

```csharp
public static class SearchFields
{
    public const string Name = "name";
    public const string Email = "email";
    public const string Status = "status";
    public const string Created = "created";
    
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Name, Email, Status, Created
    };
}
```

### 3. Document Configuration

```csharp
/// <summary>
/// Search API configuration.
/// </summary>
/// <remarks>
/// Field mappings:
/// - name -> fullName
/// - email -> emailAddress
/// - created -> createdAt
/// 
/// Validation:
/// - Leading wildcards disabled for performance
/// - Only whitelisted fields allowed
/// </remarks>
public static class SearchConfiguration { }
```

## Next Steps

- [Getting Started](./getting-started) - Quick start guide
- [Elasticsearch](./elasticsearch) - Elasticsearch integration
- [Validation](./validation) - Query validation
