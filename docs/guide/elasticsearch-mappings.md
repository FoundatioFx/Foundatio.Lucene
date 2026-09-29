# Elasticsearch Mappings

`ElasticMappingResolver` answers questions about the fields of an Elasticsearch index: the canonical name of a field, whether it is analyzed, nested, a date or a number, and which sub-field to sort or aggregate on. It merges a mapping declared in code with the mapping the server reports.

## Creating a resolver

```csharp
// Server mapping of an index, alias, or pattern (uses the client's synchronous and asynchronous APIs)
using var serverResolver = ElasticMappingResolver.Create(client, "employees");

// Code mapping merged with the server mapping of the index inferred for Employee
using var mergedResolver = ElasticMappingResolver.Create<Employee>(m => m.Properties(p => p
    .Text(e => e.Name, t => t.AddKeywordAndSortFields())
    .Keyword(e => e.Status)), client);

// Code mapping only, no I/O (useful in unit tests)
using var codeResolver = ElasticMappingResolver.Create(new TypeMapping { Properties = properties });

// Custom loaders
using var asyncResolver = ElasticMappingResolver.CreateWithAsyncLoader(ct => LoadMappingAsync(ct), client.Infer);
using var customResolver = ElasticMappingResolver.CreateWithLoaders(LoadMapping, ct => LoadMappingAsync(ct), client.Infer);
```

`ElasticMappingResolver.NullInstance` has an empty mapping: every field is unmapped and nothing is ever loaded.

## Loading and lookups

Loading is asynchronous and explicit; lookups are synchronous and lock-free:

```csharp
// Resolution phase: loads the mapping if needed and reloads it once if a field is missing
bool allMapped = await resolver.EnsureFieldsAsync(["name", "status"], cancellationToken);

// Build phase: synchronous lookups against the loaded mapping
ElasticFieldMapping mapping = resolver.GetMapping("Name");   // FullPath == "name", Found == true
string sortField = resolver.GetSortFieldName("name");  // "name.sort"
string aggField = resolver.GetAggregationsFieldName("name"); // "name.keyword"
bool nested = resolver.IsNestedPropertyType("children");
```

When the mapping has not been loaded, a synchronous lookup loads it with the synchronous loader if the resolver has one (the client-based factories do). A resolver created with only an asynchronous loader throws `InvalidOperationException` until `EnsureLoadedAsync` or `EnsureFieldsAsync` has completed a load. Check `CanResolveSynchronously` to decide which applies.

## Refresh behavior

- A field that is missing from the loaded mapping triggers a reload, at most once per `UnmappedFieldRefreshInterval` (default 5 seconds). Fields created by dynamic templates only appear after a document using them is indexed, so this is how they are discovered. Dynamic templates are not simulated.
- Synchronous lookups only reload through a synchronous loader and never wait for an asynchronous load.
- Concurrent loads are coalesced; callers wait up to `MappingRefreshWaitTimeout` (default 30 seconds) to join a load someone else started.
- A failed reload keeps the last good mapping and is retried after the interval.
- `RefreshMapping()` invalidates the mapping so the next load bypasses the throttle; `RefreshAsync()` also reloads it.
- `ServerMappingRevisionResolver` lets reloads of an unchanged mapping revision reuse resolved fields.

## Merging rules

- The server mapping is authoritative. A code property contributes its sub-fields or object properties only when the server property has the same type, and code-only properties are included. Neither mapping is modified.
- When the target is an alias or pattern that resolves to several indices, their mappings are merged in descending order of index name, so for date or rollover suffixed indices the newest index wins type conflicts (which are logged). Fields that exist in only some indices are included.
- Field names match exactly first and then ignoring case, and results use the mapped spelling.

## Mapping helpers

`AddKeywordField`, `AddSortField`, and `AddKeywordAndSortFields` add `keyword` and `sort` keyword sub-fields to text properties, and `AddSortNormalizer` registers the `sort` normalizer (`lowercase` and `asciifolding`) that `AddSortField` uses:

```csharp
await client.Indices.CreateAsync("employees", c => c
    .Settings(s => s.Analysis(a => a.AddSortNormalizer()))
    .Mappings<Employee>(m => m.Properties(p => p.Text(e => e.Name, t => t.AddKeywordAndSortFields()))));
```

The preferred sub-field names used by `GetAggregationsFieldName` and `GetSortFieldName` can be changed with `KeywordSubFieldName` and `SortSubFieldName`.
