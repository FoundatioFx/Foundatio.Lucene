# Field Mapping

Field mapping lets users query with friendly names while the data store uses its own. The parsers resolve every field in queries, sorts, and aggregations before building, and remember the name the user wrote (for example to name aggregations and to validate allowed fields).

## Field maps

A `FieldMap` maps names users write to real field names. Lookups are case-insensitive.

```csharp
var fieldMap = new FieldMap
{
    { "user", "account.username" },
    { "created", "metadata.timestamp" }
};

var parser = new ElasticsearchQueryParser(c => c.FieldMap = fieldMap);
var query = parser.BuildQuery("user:john created:>now-1d");
// fields: account.username, metadata.timestamp
```

Fields that aren't in the map pass through unchanged.

### Hierarchical resolution

By default (`FieldResolutionMode.Hierarchical`) a mapping also applies to sub-fields by the longest matching prefix:

```csharp
var fieldMap = new FieldMap { { "data", "idx.data" } };
// data.browser → idx.data.browser
```

Use `FieldResolutionMode.Direct` to resolve only exact names.

### Reporting unmapped fields

Set `ReportUnmappedFields = true` to treat fields that aren't in the map as unresolved. Combined with `QueryValidationOptions.AllowUnresolvedFields = false`, only mapped fields can be queried:

```csharp
var fieldMap = new FieldMap { ReportUnmappedFields = true };
fieldMap.Add("title", "document.title");

var validation = new QueryValidationOptions { AllowUnresolvedFields = false };
var parser = new ElasticsearchQueryParser(c =>
{
    c.FieldMap = fieldMap;
    c.ValidationOptions = validation;
});

parser.BuildQuery("title:x");   // OK
parser.BuildQuery("secret:x");  // QueryValidationException: field (secret) can't be resolved
```

### Building maps fluently

```csharp
var fieldMap = FieldMapBuilder.Create()
    .Map("user", "account.username")
    .MapMany("account.email", "email", "mail")
    .MapNamespace("meta", "document.metadata")
    .ReportUnmapped()
    .Build();
```

## Field resolvers

For dynamic fields, give the parser a resolver. It runs before the field map; return null when the resolver doesn't know the field.

```csharp
var parser = new ElasticsearchQueryParser(c =>
{
    c.FieldResolver = (field, context) => field.StartsWith("custom.", StringComparison.OrdinalIgnoreCase)
        ? "custom_fields." + field["custom.".Length..].ToLowerInvariant()
        : null;
});
```

When the mapping lives in a database, use an async resolver. It runs once per distinct field in the resolution phase of the `Async` methods:

```csharp
var parser = new ElasticsearchQueryParser(c =>
{
    c.AsyncFieldResolver = async (field, context, cancellationToken) =>
        await customFields.GetStorageNameAsync(field, cancellationToken);
});

var query = await parser.BuildQueryAsync("color:red");
```

Resolver exceptions become validation errors instead of escaping.

## Per-request maps

Field maps often differ per tenant. Pass them per request instead of creating parsers:

```csharp
var tenantOptions = new ElasticsearchQueryOptions
{
    FieldMap = new FieldMap { { "priority", $"tenant_{tenantId}.priority" } }
};

var query = parser.BuildQuery("priority:high", tenantOptions);
```

Options are immutable, so cache them per tenant.

## Elasticsearch mappings

With a mapping configured (`UseMappings`), the Elasticsearch provider also resolves field names against it: `TITLE:x` resolves to `title`, and a field that is neither mapped nor a runtime field is reported as unresolved. See [Elasticsearch](./elasticsearch).

## The original field name

Resolution records the name the user wrote on the node:

```csharp
var document = LuceneQuery.Parse("user:john").Document;
FieldResolverQueryVisitor.Run(document, new FieldMap { { "user", "account.username" } });

var field = (FieldQueryNode)document.Query!;
// field.Field == "account.username", field.GetOriginalField() == "user"
```

`QueryValidationOptions.AllowedFields` is checked against the original names — the names your users see — while `RestrictedFields` is checked against both. See [Validation](./validation).

## Exposing available fields

Keep the list of fields users may query in one place and use it for the field map, the allowed fields, and your UI:

```csharp
public static class SearchFields
{
    public static readonly IReadOnlyDictionary<string, string> Fields = new Dictionary<string, string>
    {
        ["title"] = "document.title",
        ["author"] = "document.author.name",
        ["created"] = "document.createdUtc"
    };

    public static FieldMap CreateFieldMap() => new(Fields.ToDictionary(f => f.Key, f => f.Value));

    public static QueryValidationOptions CreateValidationOptions()
    {
        var options = new QueryValidationOptions();
        foreach (string field in Fields.Keys)
            options.AllowedFields.Add(field);
        return options;
    }
}
```
