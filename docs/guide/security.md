# Security

Queries usually come from end users, so treat them as untrusted input. Foundatio.Lucene bounds the work a query can cause by default and gives you validation rules to restrict what a query may touch.

## What is bounded by default

| Risk | Protection |
|---|---|
| Stack exhaustion from deeply nested parentheses | `LuceneParserOptions.MaxDepth` (default 100) turns deeper nesting into a parse error. Parsing is iterative everywhere else and linear in the query length. |
| Recursive or self-referencing includes | Detected and reported as validation errors. |
| Include "bombs" (includes that reference other includes many times) | `QueryValidationOptions.MaxIncludeDepth` (default 10) and `MaxIncludeExpansions` (default 100). |
| Malformed input | Parsing never throws; every error is reported with its position. |
| Unresolved includes | Rejected by default (`AllowUnresolvedIncludes = false`). |
| Mutating shared state | Parsers, visitors, and options are thread-safe; caller documents and options are never modified. |

## Restrict fields

Without restrictions a query can reference any field the data store has. Use `AllowedFields` for an allowlist or `RestrictedFields` for a denylist:

```csharp
var validation = new QueryValidationOptions();
validation.AllowedFields.Add("title");
validation.AllowedFields.Add("status");
validation.AllowedFields.Add("data");       // also allows data.*, e.g. data.browser
validation.RestrictedFields.Add("internal"); // also restricts internal.*, e.g. internal.keyword

var parser = new ElasticsearchQueryParser(c => c.ValidationOptions = validation);
```

The rules are enforced every time a query, sort, or aggregation is built, not only when you call a `Validate` method:

- `AllowedFields` matches field names as the user wrote them (before aliases are resolved). An allowed name also allows its sub-fields.
- `RestrictedFields` is checked against both the name as written and the resolved name, including sub-fields, so a restricted field can't be reached through an alias, an include, or a sub-field such as `.keyword`.
- Includes are expanded before validation, so a saved query can't smuggle in a restricted field.
- When either list is configured, wildcard field names (`secr*:x`, `_exists_:secr*`) are rejected because they can't be checked.
- Terms without a field are validated against the default fields they search.

With Entity Framework, only properties discovered from the model (after your property and navigation filters) and fields you register explicitly are queryable. Any other property path is an unresolved field, so users can't reach columns you didn't intend to expose.

## Restrict expensive operations

```csharp
validation.AllowLeadingWildcards = false;             // reject *term and ?term
validation.RestrictedOperations.Add(QueryOperations.Regex);
validation.RestrictedOperations.Add(QueryOperations.Wildcard);
validation.AllowedMaxNodeDepth = 5;                   // parenthesized nesting
validation.AllowedMaxSortFields = 3;
```

`AllowedOperations` / `RestrictedOperations` accept the `QueryOperations` names (`term`, `phrase`, `prefix`, `wildcard`, `fuzzy`, `regex`, `range`, `exists`, `missing`, `match_all`) for queries and aggregation types (`terms`, `date`, `max`, ...) for aggregation expressions.

Regular expressions are rejected by the Entity Framework provider by default. Elasticsearch evaluates them with its own limits (`index.max_regex_length`).

## Report unknown fields

By default a field that can't be resolved (for example one that isn't in the Elasticsearch mapping) is passed through. The Entity Framework provider always rejects fields that aren't in the model. Set `AllowUnresolvedFields = false` to reject queries that use unresolved fields — useful to catch typos and to make sure queries only touch known fields:

```csharp
validation.AllowUnresolvedFields = false;
```

## Handling errors

Building throws `QueryParseException` for syntax errors and `QueryValidationException` for rule violations. Both derive from `QueryException`, carry an error code, and are safe to show to users: messages describe the query, never your data. Use the `TryBuild*` methods to get a result instead of an exception:

```csharp
var result = parser.TryBuildQuery(userQuery);
if (!result.IsSuccess)
    return Results.BadRequest(new { error = result.ErrorMessage });
```
