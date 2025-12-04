# Date Math

Foundatio.Lucene supports Elasticsearch-style date math expressions for working with dates dynamically.

## Basic Syntax

### Current Time

Use `now` to reference the current time:

```
created:now           // Current timestamp
modified:[now-1d TO now]   // Last 24 hours
```

### Adding/Subtracting Time

Add or subtract time from a base date:

```
now-1d     // 1 day ago
now+1h     // 1 hour from now
now-1w     // 1 week ago
now+30m    // 30 minutes from now
```

### Time Units

| Unit | Description |
|------|-------------|
| `y`  | Year        |
| `M`  | Month       |
| `w`  | Week        |
| `d`  | Day         |
| `h`  | Hour        |
| `m`  | Minute      |
| `s`  | Second      |

Examples:

```
now-1y     // 1 year ago
now-6M     // 6 months ago
now-2w     // 2 weeks ago
now-3d     // 3 days ago
now-12h    // 12 hours ago
now-30m    // 30 minutes ago
now-45s    // 45 seconds ago
```

## Rounding

Use `/` to round to a time unit:

```
now/d      // Start of current day (00:00:00)
now/M      // Start of current month
now/y      // Start of current year
now/h      // Start of current hour
```

Combine with arithmetic:

```
now-1d/d   // Start of yesterday
now/M-1M   // Start of last month
now/w      // Start of current week
```

## Anchored Date Math

Start from a specific date using `||`:

```
2024-01-01||+1M      // January 1st 2024 plus one month
2024-06-15||+1d      // June 15th 2024 plus one day
2024-03-01||-1w      // March 1st 2024 minus one week
```

With rounding:

```
2024-01-15||/M       // Start of January 2024
2024-06-15||+1M/d    // July 15th 2024, rounded to start of day
```

## Using Date Math

### In Queries

```csharp
using Foundatio.Lucene;

// Parse a query with date math
var result = LuceneQuery.Parse("created:[now-7d TO now]");

// Evaluate the date math expressions
await DateMathEvaluatorVisitor.RunAsync(result.Document);

// Now the date values are resolved to actual dates
```

### In Elasticsearch Queries

The Elasticsearch parser automatically evaluates date math:

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    config.IsDateField = field => 
        field.EndsWith("date") || 
        field.EndsWith("At") ||
        field == "created" || 
        field == "modified";
    config.DefaultTimeZone = "America/Chicago";
});

// Date math is automatically evaluated
var query = parser.BuildQuery("created:[now-7d TO now]");
```

### Custom Evaluation

Evaluate date math with custom options:

```csharp
using Foundatio.Lucene;

var result = LuceneQuery.Parse("date:[now-1d TO now]");

// Custom "now" reference point
var referenceTime = new DateTime(2024, 6, 15, 12, 0, 0);
await DateMathEvaluatorVisitor.RunAsync(result.Document, referenceTime);
```

## Common Patterns

### Last N Days

```
created:[now-7d TO now]     // Last 7 days
created:[now-30d TO now]    // Last 30 days
created:[now-90d TO now]    // Last 90 days
```

### This Period

```
created:[now/d TO now]      // Today
created:[now/w TO now]      // This week
created:[now/M TO now]      // This month
created:[now/y TO now]      // This year
```

### Previous Period

```
created:[now-1d/d TO now/d}    // Yesterday (exclusive of today)
created:[now-1w/w TO now/w}    // Last week
created:[now-1M/M TO now/M}    // Last month
```

### Date Ranges

```
created:[2024-01-01 TO 2024-12-31]           // Full year
created:[2024-01-01||/M TO 2024-01-01||+1M}  // January 2024
```

### Relative to Specific Date

```
event_date:[2024-06-15||-7d TO 2024-06-15]   // Week before event
event_date:[2024-06-15 TO 2024-06-15||+7d]   // Week after event
```

## Date Formats

The parser accepts various date formats:

```
2024-01-15                    // ISO date
2024-01-15T10:30:00          // ISO datetime
2024-01-15T10:30:00Z         // ISO datetime UTC
2024-01-15T10:30:00+05:00    // ISO datetime with offset
```

## Timezone Handling

### Elasticsearch Integration

Configure the default timezone:

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    config.DefaultTimeZone = "America/Chicago";
});
```

### Manual Evaluation

Specify timezone during evaluation:

```csharp
var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
// Use with DateMathEvaluatorVisitor
```

## API Examples

### Search API with Date Math

```csharp
[HttpGet("logs")]
public async Task<IActionResult> SearchLogs(
    [FromQuery] string q = "level:error AND created:[now-1h TO now]")
{
    var parser = new ElasticsearchQueryParser(config =>
    {
        config.IsDateField = f => f == "created" || f == "timestamp";
        config.DefaultTimeZone = "UTC";
    });

    var query = parser.BuildQuery(q);
    
    var response = await _client.SearchAsync<LogEntry>(s => s
        .Index("logs")
        .Query(query)
        .Sort(so => so.Field("timestamp", f => f.Order(SortOrder.Desc)))
    );

    return Ok(response.Documents);
}
```

### Predefined Date Filters

```csharp
public static class DateFilters
{
    public static string Today => "created:[now/d TO now]";
    public static string Yesterday => "created:[now-1d/d TO now/d}";
    public static string ThisWeek => "created:[now/w TO now]";
    public static string LastWeek => "created:[now-1w/w TO now/w}";
    public static string ThisMonth => "created:[now/M TO now]";
    public static string LastMonth => "created:[now-1M/M TO now/M}";
    public static string Last7Days => "created:[now-7d TO now]";
    public static string Last30Days => "created:[now-30d TO now]";
    public static string Last90Days => "created:[now-90d TO now]";
    public static string ThisYear => "created:[now/y TO now]";
}

// Usage
var query = $"{DateFilters.Last7Days} AND level:error";
```

## Testing Date Math

When testing, use a fixed reference time:

```csharp
[Fact]
public async Task DateMath_EvaluatesCorrectly()
{
    // Arrange
    var result = LuceneQuery.Parse("date:[now-1d TO now]");
    var referenceTime = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    
    // Act
    await DateMathEvaluatorVisitor.RunAsync(result.Document, referenceTime);
    
    // Assert - check the resolved values
    // Expected: date:[2024-06-14T12:00:00Z TO 2024-06-15T12:00:00Z]
}
```

## Next Steps

- [Query Syntax](./query-syntax) - Complete query syntax reference
- [Elasticsearch](./elasticsearch) - Elasticsearch integration
- [Visitors](./visitors) - Date math evaluation visitor
