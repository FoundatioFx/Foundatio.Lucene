# Query Syntax

Foundatio.Lucene supports the full Lucene query syntax. This page documents all supported syntax elements.

## Terms

Simple terms search for exact matches:

```
hello
```

### Wildcards

Use `*` for multiple characters and `?` for single characters:

```
hello*      // Matches: hello, helloworld, hello123
hel?o       // Matches: hello, helpo, helao
*world      // Matches: world, helloworld (leading wildcard)
```

::: warning
Leading wildcards (`*world`) can be expensive. Use `QueryValidationOptions.AllowLeadingWildcards = false` to disable them.
:::

## Phrases

Use quotes for exact phrase matching:

```
"hello world"
```

### Proximity Search

Add `~N` to find words within N positions of each other:

```
"hello world"~2    // "hello" and "world" within 2 words of each other
```

## Fuzzy Search

Add `~` to a term to perform fuzzy matching based on edit distance (Levenshtein distance):

```
roam~              // Fuzzy search with default edit distance of 2
roam~1             // Fuzzy search with edit distance of 1
roam~0             // Exact match (edit distance of 0)
```

The edit distance specifies the maximum number of character changes (insertions, deletions, substitutions) allowed to match a term. For example, `roam~1` would match:

- `roam` (exact)
- `foam` (1 substitution)
- `roams` (1 insertion)

::: tip
Edit distance must be an integer. The default value is 2 when omitted. Lower values (0-1) are more restrictive and generally faster.
:::

::: info AST Representation
The AST preserves whether the fuzzy distance was explicitly specified:
- `term~` → `FuzzyDistance = TermNode.DefaultFuzzyDistance` (sentinel value -1)
- `term~2` → `FuzzyDistance = 2` (explicitly specified)

Both resolve to an effective distance of 2, but you can distinguish between default and explicit using `GetEffectiveFuzzyDistance()`.
:::

### Fuzzy with Field Queries

Fuzzy search works with field queries:

```
title:hello~2      // Fuzzy search in the "title" field
user.name:john~1   // Fuzzy search in nested field
```

## Field Queries

Specify which field to search:

```
title:hello
user.name:john       // Nested field
status:active
```

### Default Field

Terms without a field prefix search the default field (or all fields depending on configuration):

```
hello                // Searches default field(s)
title:hello          // Searches only "title" field
```

## Ranges

### Inclusive Ranges

Use square brackets for inclusive ranges (includes boundaries):

```
price:[100 TO 500]           // 100 <= price <= 500
date:[2020-01-01 TO 2020-12-31]
```

### Exclusive Ranges

Use curly braces for exclusive ranges (excludes boundaries):

```
price:{100 TO 500}           // 100 < price < 500
```

### Mixed Ranges

Mix inclusive and exclusive boundaries:

```
price:[100 TO 500}           // 100 <= price < 500
price:{100 TO 500]           // 100 < price <= 500
```

### Open-Ended Ranges

Use `*` for unbounded ranges:

```
price:[100 TO *]             // price >= 100
price:[* TO 500]             // price <= 500
age:{18 TO *}                // age > 18
```

## Boolean Operators

### AND

Both terms must match:

```
title:hello AND status:active
title:hello && status:active   // Alternative syntax
```

### OR

Either term must match:

```
status:active OR status:pending
status:active || status:pending  // Alternative syntax
```

### NOT

Exclude matches:

```
status:active NOT archived:true
status:active !archived:true     // Alternative syntax
```

### Prefix Operators

Use `+` (must match) and `-` (must not match):

```
+status:active              // Must match
-archived:true              // Must not match
+title:hello -status:draft  // Must match title, must not be draft
```

## Grouping

Use parentheses to group clauses:

```
(status:active OR status:pending) AND priority:high
title:(hello OR goodbye)
```

## Special Queries

### Exists / Missing

Check if a field exists or is missing:

```
_exists_:email           // Documents with email field
_missing_:phone          // Documents without phone field
```

### Match All

Match all documents:

```
*:*
```

## Regular Expressions

Use forward slashes for regex patterns:

```
/joh?n(athan)?/          // Matches: john, jon, jonathan, jonatan
name:/[a-z]+/
```

::: tip
Regex patterns follow the .NET `Regex` syntax.
:::

## Date Math

Elasticsearch-style date math is supported for date fields:

### Relative Dates

```
created:now              // Current time
created:now-1d           // 1 day ago
created:now+1h           // 1 hour from now
created:now-1w           // 1 week ago
```

### Date Math Units

| Unit | Description |
|------|-------------|
| `y`  | Year        |
| `M`  | Month       |
| `w`  | Week        |
| `d`  | Day         |
| `h`  | Hour        |
| `m`  | Minute      |
| `s`  | Second      |

### Anchored Date Math

Start from a specific date:

```
2024-01-01||+1M          // January 1st 2024 plus one month
2024-01-01||+1M/d        // Same, rounded to day
```

### Rounding

Use `/` to round to a time unit:

```
now/d                    // Round to start of current day
now/M                    // Round to start of current month
now-1d/d                 // Start of yesterday
```

### Rounding with Inclusive/Exclusive Ranges

Rounding behavior changes depending on whether a range boundary is inclusive or exclusive. This follows Elasticsearch's conventions:

**Inclusive boundaries** round to maximize the matched range:

- Inclusive min (`[`): rounds **down** (start of period) — e.g., `[now/d` → start of today
- Inclusive max (`]`): rounds **up** (end of period) — e.g., `now/d]` → end of today

**Exclusive boundaries** round to minimize the matched range:

- Exclusive min (`{`): rounds **up** (end of period) — e.g., `{now/d` → end of today, then `>`
- Exclusive max (`}`): rounds **down** (start of period) — e.g., `now/d}` → start of today, then `<`

This applies to all rounding units (`/d`, `/M`, `/h`, `/y`, etc.) and to short-form operators:

| Query | Rounding | Effective |
| ----- | -------- | --------- |
| `[now/d TO now/d]` | min→start, max→end | Entire current day |
| `[now/d TO now/d}` | min→start, max→start | Empty (start ≤ x < start) |
| `{now/d TO now/d]` | min→end, max→end | Empty (end < x ≤ end) |
| `>=now/d` | start of day | From start of today onward |
| `>now/d` | end of day | After today |
| `<now/d` | start of day | Before today |
| `<=now/d` | end of day | Through end of today |
| `[now/M TO now/M]` | min→start of month, max→end of month | Entire current month |
| `>=now/h` | start of hour | From start of current hour |

::: tip
The same `/unit` rounding expression produces different concrete dates depending on whether the boundary is inclusive or exclusive. This matches [Elasticsearch's native date math rounding behavior](https://www.elastic.co/docs/reference/query-languages/query-dsl/query-dsl-range-query#range-query-date-math-rounding).
:::

### Common Date Range Patterns

```
// This month (start of current month through end of current month)
created:[now/M TO now/M]

// Last month
created:[now-1M/M TO now-1M/M]

// Year to date (start of current year through now)
created:[now/y TO now]

// Last year
created:[now-1y/y TO now-1y/y]

// Today
created:[now/d TO now/d]

// Yesterday
created:[now-1d/d TO now-1d/d]

// Last 7 full days (not including today)
created:[now-7d/d TO now-1d/d]

// Last 30 days (rolling, including partial today)
created:[now-30d TO now]

// This week (start of current week through end of current week)
created:[now/w TO now/w]

// Last hour
created:[now-1h/h TO now-1h/h]

// Last 15 minutes (rolling)
created:[now-15m TO now]

// Last 4 hours (rolling)
created:[now-4h TO now]

// Last 4 full hours (rounded to hour boundaries)
created:[now-4h/h TO now/h]
```

## Includes

Reference saved or named queries:

```
@include:savedQuery
@include:my-filter
```

::: info
Include expansion requires configuring an `IncludeResolver` function.
:::

## Escaping Special Characters

Escape special characters with backslash:

```
title:hello\:world       // Searches for "hello:world" in title
name:John\ Doe           // Searches for "John Doe"
```

Special characters that need escaping: `+ - && || ! ( ) { } [ ] ^ " ~ * ? : \ /`

## Query Examples

Here are some real-world query examples:

### E-commerce Product Search

```
category:electronics AND price:[100 TO 500] AND brand:(apple OR samsung) AND _exists_:inStock
```

### Log Analysis

```
level:error AND timestamp:[now-1h TO now] AND (service:api OR service:web) NOT test:true
```

### User Search

```
name:john* AND role:(admin OR moderator) AND status:active AND lastLogin:[now-30d TO *]
```

### Fuzzy Name Search

```
firstName:john~1 AND lastName:smith~2
```

### Document Search

```
"annual report" AND year:2024 AND department:(finance OR legal) -draft:true
```

## AST Node Types

When parsing, queries are converted to these AST node types:

| Node Type | Description | Example |
|-----------|-------------|---------|
| `QueryDocument` | Root node | - |
| `TermNode` | Simple term | `hello` |
| `PhraseNode` | Quoted phrase | `"hello world"` |
| `FieldQueryNode` | Field:value pair | `title:test` |
| `RangeNode` | Range query | `[1 TO 10]` |
| `BooleanQueryNode` | Boolean combination | `a AND b` |
| `GroupNode` | Parenthetical group | `(a OR b)` |
| `NotNode` | Negation wrapper | `NOT a` |
| `ExistsNode` | `_exists_:field` check | `_exists_:email` |
| `MissingNode` | `_missing_:field` check | `_missing_:phone` |
| `MatchAllNode` | `*:*` match all | `*:*` |
| `RegexNode` | Regular expression | `/pattern/` |
| `MultiTermNode` | Multiple terms without operators | `hello world` |

## Next Steps

- [Visitors](./visitors) - Transform and analyze the AST
- [Field Mapping](./field-mapping) - Map field names
- [Validation](./validation) - Validate queries
