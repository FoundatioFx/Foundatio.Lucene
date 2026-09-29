# Validation

Validation restricts what users can do with queries, sorts, and aggregations: which fields they can use, which operations are allowed, and how complex a query may be. The parsers enforce the configured rules on every build; you can also validate without building.

## Configuring rules

```csharp
var validation = new QueryValidationOptions
{
    AllowLeadingWildcards = false,
    AllowedMaxNodeDepth = 5
};
validation.AllowedFields.Add("title");
validation.AllowedFields.Add("status");
validation.RestrictedOperations.Add(QueryOperations.Regex);

var parser = new ElasticsearchQueryParser(c => c.ValidationOptions = validation);

parser.BuildQuery("title:hello");   // OK
parser.BuildQuery("secret:x");      // throws QueryValidationException
```

Rules can also be set per request with `options.ValidationOptions`. Configure an options instance once and share it; don't modify it while it is in use.

## Options

| Option | Default | Effect |
|---|---|---|
| `AllowedFields` | empty (all) | Field names that may be used, as written by the user. A name also allows its sub-fields (`data` allows `data.age`). |
| `RestrictedFields` | empty | Field names that may not be used, checked against the name as written and the resolved name, including sub-fields. |
| `AllowLeadingWildcards` | `true` | Whether terms may start with `*` or `?`. |
| `AllowUnresolvedFields` | `true` | Whether fields the resolvers (or the Elasticsearch mapping) can't resolve are allowed. The Entity Framework provider always rejects fields that aren't in the model. |
| `AllowUnresolvedIncludes` | `false` | Whether `@include` references that can't be resolved are allowed. |
| `AllowedOperations` / `RestrictedOperations` | empty | `QueryOperations` names for queries; aggregation types for aggregation expressions. |
| `AllowedMaxNodeDepth` | 0 (no limit) | Maximum nesting of parenthesized groups. |
| `AllowedMaxSortFields` | 0 (no limit) | Maximum number of sort fields. |
| `MaxIncludeDepth` | 10 | Maximum depth of nested includes. |
| `MaxIncludeExpansions` | 100 | Maximum number of include expansions in one query. |
| `ShouldThrow` | `false` | Whether the `Validate` methods throw instead of returning an invalid result (building always throws). |

When `AllowedFields` or `RestrictedFields` is configured, wildcard field names (`sec*:x`) are rejected, and terms without a field are checked against the default fields they search.

## Validating without building

```csharp
QueryValidationResult result = QueryValidator.ValidateQuery("title:hello AND secret:x", validation);
if (!result.IsValid)
    Console.WriteLine(result.Message); // Query uses field(s) (secret) that are not allowed to be used.
```

`QueryValidator` applies includes, field maps, and resolvers from an optional context, so it validates what would actually be queried:

```csharp
var context = new QueryVisitorContext
{
    FieldMap = new FieldMap { { "user", "account.username" } },
    Includes = new Dictionary<string, string> { ["mine"] = "owner:me" }
};

var result = QueryValidator.ValidateQuery("user:john @include:mine", validation, context);
```

Use `QueryValidator.ValidateSort` and `QueryValidator.ValidateAggregations` for the other expression types. The providers have their own `ValidateQuery` and `ValidateSort` methods (and `ValidateAggregations` for Elasticsearch) that also resolve fields against the Elasticsearch mapping or the Entity Framework model.

## What the result tells you

Besides errors, a validation result describes the query, which is useful for logging or for rules of your own:

| Property | Contents |
|---|---|
| `IsValid`, `ValidationErrors`, `Message` | errors, each with a message, position, and `QueryErrorCode` |
| `ReferencedFields` | fields as written |
| `ResolvedFields` | fields after resolution |
| `UnresolvedFields`, `ReferencedIncludes`, `UnresolvedIncludes` | fields and includes by resolution outcome |
| `Operations` | each operation used, with the fields it was used on |
| `MaxNodeDepth` | deepest nesting of parenthesized groups |

```csharp
var result = QueryValidator.ValidateQuery("title:hel* AND price:[1 TO 5]");
// result.Operations: prefix → {title}, range → {price}
```

## Handling validation errors

Building throws `QueryValidationException` (with the full `Result`) for syntax errors and rule violations. Its message is meant for users. (`QueryParseException` is only thrown by `LuceneParseResult.GetDocumentOrThrow` when you parse with `LuceneQuery` yourself.) In an API:

```csharp
app.MapGet("/search", (string q, ElasticsearchQueryParser parser) =>
{
    var result = parser.TryBuildQuery(q);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(new { error = result.ErrorMessage });
});
```

## Custom rules

Add rules with a visitor that reports errors on the context — see [Custom Visitors](./custom-visitors#adding-validation-rules). Register it before `ValidationVisitor` (priority 30) and the build fails the same way as for built-in rules.
