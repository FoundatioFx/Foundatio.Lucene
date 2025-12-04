# Elasticsearch Integration

Foundatio.Lucene.Elasticsearch converts Lucene query strings to Elasticsearch Query DSL using the official Elastic.Clients.Elasticsearch 9.x client.

## Installation

```bash
dotnet add package Foundatio.Lucene.Elasticsearch
```

## Basic Usage

```csharp
using Foundatio.Lucene.Elasticsearch;
using Elastic.Clients.Elasticsearch;

var parser = new ElasticsearchQueryParser();

// Parse a Lucene query and convert to Elasticsearch Query DSL
var query = parser.BuildQuery("title:hello AND status:active");

// Use with the Elasticsearch client
var client = new ElasticsearchClient();
var response = await client.SearchAsync<Document>(s => s
    .Index("my-index")
    .Query(query)
);
```

## Configuration Options

The `ElasticsearchQueryParser` supports extensive configuration:

```csharp
var parser = new ElasticsearchQueryParser(config =>
{
    // Use scoring queries (match) vs filter queries (term)
    config.UseScoring = true;

    // Default fields for unfielded terms
    config.DefaultFields = ["title", "content", "description"];

    // Default boolean operator (AND or OR)
    config.DefaultOperator = QueryOperator.And;

    // Field aliasing
    config.FieldMap = new FieldMap
    {
        { "author", "metadata.author" },
        { "created", "metadata.createdAt" },
        { "updated", "metadata.updatedAt" }
    };

    // Geo field detection
    config.IsGeoPointField = field => field == "location" || field.EndsWith("_geo");

    // Date field detection
    config.IsDateField = field => 
        field.EndsWith("date") || 
        field.EndsWith("timestamp") ||
        field == "created" || 
        field == "updated";

    // Default timezone for date ranges
    config.DefaultTimeZone = "America/Chicago";

    // Include resolver for @include syntax
    config.IncludeResolver = async name =>
    {
        return await _savedQueryService.GetQueryAsync(name);
    };

    // Query validation
    config.ValidationOptions = new QueryValidationOptions
    {
        AllowLeadingWildcards = false
    };
});
```

## Query Types

### Term Queries

Simple field queries become term queries (or match queries if scoring is enabled):

```csharp
// Input
"status:active"

// Output (UseScoring = false)
{ "term": { "status": "active" } }

// Output (UseScoring = true)
{ "match": { "status": "active" } }
```

### Phrase Queries

Quoted phrases become match_phrase queries:

```csharp
// Input
"title:\"hello world\""

// Output
{ "match_phrase": { "title": "hello world" } }
```

With proximity:

```csharp
// Input
"title:\"hello world\"~2"

// Output
{ "match_phrase": { "title": { "query": "hello world", "slop": 2 } } }
```

### Range Queries

Range syntax maps directly to Elasticsearch range queries:

```csharp
// Input
"price:[100 TO 500]"

// Output
{ "range": { "price": { "gte": 100, "lte": 500 } } }

// Input with exclusive boundaries
"price:{100 TO 500}"

// Output
{ "range": { "price": { "gt": 100, "lt": 500 } } }
```

### Boolean Queries

Boolean operators map to Elasticsearch bool queries:

```csharp
// Input
"title:hello AND status:active"

// Output
{
    "bool": {
        "must": [
            { "term": { "title": "hello" } },
            { "term": { "status": "active" } }
        ]
    }
}
```

### Wildcard Queries

Wildcards are converted to wildcard queries:

```csharp
// Input
"name:john*"

// Output
{ "wildcard": { "name": "john*" } }
```

### Regex Queries

Regex patterns become regexp queries:

```csharp
// Input
"name:/joh?n/"

// Output
{ "regexp": { "name": "joh?n" } }
```

### Exists/Missing Queries

Field existence checks:

```csharp
// Input
"_exists_:email"

// Output
{ "exists": { "field": "email" } }

// Input
"_missing_:phone"

// Output
{ "bool": { "must_not": { "exists": { "field": "phone" } } } }
```

## Geo Queries

The Elasticsearch integration supports geo queries when `IsGeoPointField` is configured.

### Distance Queries

```csharp
config.IsGeoPointField = field => field == "location";

// Input: field:lat,lon~distance
"location:40.7128,-74.0060~10km"

// Output
{
    "geo_distance": {
        "distance": "10km",
        "location": {
            "lat": 40.7128,
            "lon": -74.0060
        }
    }
}
```

### Bounding Box Queries

```csharp
// Input: field:[min_lon,min_lat TO max_lon,max_lat]
"location:[-74.1,40.6 TO -73.9,40.8]"

// Output
{
    "geo_bounding_box": {
        "location": {
            "top_left": { "lat": 40.8, "lon": -74.1 },
            "bottom_right": { "lat": 40.6, "lon": -73.9 }
        }
    }
}
```

### Geo Location Resolution

You can resolve location names to coordinates:

```csharp
config.GeoLocationResolver = async locationName =>
{
    // Resolve "New York" to coordinates
    var coords = await _geocodingService.ResolveAsync(locationName);
    return coords != null ? $"{coords.Lat},{coords.Lon}" : null;
};

// Input
"location:\"New York\"~50mi"
```

## Date Queries

Date fields with date math are automatically handled:

```csharp
config.IsDateField = field => field.EndsWith("date");
config.DefaultTimeZone = "America/Chicago";

// Input
"created:[now-7d TO now]"

// Output (date math evaluated)
{
    "range": {
        "created": {
            "gte": "2024-11-26T00:00:00",
            "lte": "2024-12-03T00:00:00",
            "time_zone": "America/Chicago"
        }
    }
}
```

## Async API

For async operations (like include resolution or geo location resolution):

```csharp
var query = await parser.BuildQueryAsync("@include:saved-filter AND status:active");
```

## Error Handling

The parser throws `QueryParseException` for invalid queries:

```csharp
try
{
    var query = parser.BuildQuery("invalid::[query");
}
catch (QueryParseException ex)
{
    Console.WriteLine($"Parse error: {ex.Message}");
}
```

## Custom Visitors

Add custom visitors to transform the query before building:

```csharp
var parser = new ElasticsearchQueryParser();

// Add a custom visitor
parser.AddVisitor(new MyCustomVisitor());

var query = parser.BuildQuery("...");
```

## Complete Example

Here's a complete example with an ASP.NET Core API:

```csharp
[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchQueryParser _parser;

    public SearchController(ElasticsearchClient client)
    {
        _client = client;
        _parser = new ElasticsearchQueryParser(config =>
        {
            config.UseScoring = true;
            config.DefaultFields = ["title", "content"];
            config.FieldMap = new FieldMap
            {
                { "author", "metadata.author" },
                { "date", "metadata.publishedAt" }
            };
            config.IsDateField = f => f.Contains("date") || f.Contains("At");
            config.ValidationOptions = new QueryValidationOptions
            {
                AllowLeadingWildcards = false
            };
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        try
        {
            var query = _parser.BuildQuery(q);

            var response = await _client.SearchAsync<Article>(s => s
                .Index("articles")
                .Query(query)
                .From((page - 1) * size)
                .Size(size)
                .Sort(so => so.Field("_score", f => f.Order(SortOrder.Desc)))
            );

            return Ok(new
            {
                Total = response.Total,
                Page = page,
                Results = response.Documents
            });
        }
        catch (QueryParseException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}
```

## Next Steps

- [Query Syntax](./query-syntax) - All supported query syntax
- [Visitors](./visitors) - Custom query transformation
- [Validation](./validation) - Query validation options
