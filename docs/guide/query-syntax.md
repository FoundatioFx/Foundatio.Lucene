# Query Syntax

Foundatio.Lucene parses the Lucene query syntax used by Elasticsearch's `query_string` query and Foundatio.Parsers, with a few precisely defined extensions. This page describes every construct and exactly how it is interpreted.

## Terms

A term is a word to search for. Without a field it searches the configured default fields.

```
hello
```

Terms may contain any character except whitespace and `: ( ) [ ] { } " ^ ~`. Characters such as `- + ! / . @ # $ & | = < > *` and any Unicode (including emoji) are allowed inside a term: `a-b`, `C#`, `price:$100`, `.net`, `日本語`.

### Wildcards

`*` matches any number of characters and `?` matches exactly one:

```
hello*      prefix match
hel?o       single-character wildcard
*world      leading wildcard
f*o?bar*    any combination
```

A term whose only wildcard is a single trailing `*` is a **prefix** query; anything else with an unescaped `*` or `?` is a **wildcard** query.

::: warning
Leading wildcards (`*world`) are expensive in most data stores. Set `QueryValidationOptions.AllowLeadingWildcards = false` to reject them.
:::

### Escaping

A backslash makes the next character literal: `foo\*bar` searches for `foo*bar`, `c\:\\temp` for `c:\temp`, `my\ field:x` uses the field `my field`, and `\AND` searches for the word AND. Any character can be escaped.

## Phrases

Quotes search for an exact phrase. Inside quotes, `\"` and `\\` are the only escapes you normally need.

```
"hello world"
"say \"hi\""
```

### Proximity

`~N` after a phrase allows the words to be up to N positions apart:

```
"hello world"~2
```

## Fuzzy terms

`~` after a term matches terms within an edit distance (insertions, deletions, substitutions):

```
roam~       default distance (2)
roam~1      distance 1
roam~AUTO   Elasticsearch AUTO fuzziness
```

## Boosting

`^N` raises the relevance of a clause when scoring:

```
title:important^2 body:important
(a OR b)^1.5
```

The `^` value is kept as text on the node (`BoostText`), so providers can give it other meanings where a boost makes no sense: on a date range it is the time zone (`created:[2024-01-01 TO *]^"America/Chicago"`), and in aggregation expressions it is a time zone, minimum document count, or precision threshold.

## Fields

`field:value` restricts a clause to a field. Whitespace around the colon is allowed.

```
title:hello
user.name:john
title:"hello world"
title:hel*
```

### Field groups

A field followed by a group applies the field to everything inside it that has no field of its own:

```
title:(quick OR brown)
status:(open OR regressed) AND title:(crash -test)
price:(>=10 AND <20)
```

### Wildcard fields

A field name may contain wildcards (`book.*:quick`) for data stores that support them. When `AllowedFields` or `RestrictedFields` are configured, wildcard field names are rejected because they can't be checked.

### Operators must come before the field

`title:-foo`, `title:NOT foo`, and `price:-[1 TO 5]` are errors: put the operator before the field (`-title:foo`), or quote or escape the value (`title:"-foo"`, `title:\-foo`). Inside a field group operators are fine: `title:(-foo bar)`.

## Boolean logic

### Operators

| Syntax | Meaning |
|---|---|
| `a AND b`, `a && b` | both |
| `a OR b`, `a \|\| b` | either |
| `NOT a`, `!a` | not |
| `+a` | a is required |
| `-a` | a is prohibited |
| `a b` | the **default operator** (AND unless configured otherwise) |

Keywords are case-sensitive: `and`, `or`, and `not` are ordinary terms. `&&` and `||` must be surrounded by whitespace or parentheses; `a&&b` is a single term. A `+`, `-`, or `!` followed by whitespace is literal text, so `One - Two` searches for three terms.

### Precedence

NOT binds tightest, then AND, then OR. Juxtaposed clauses use the default operator at that operator's precedence. Use parentheses to be explicit.

| Query (default AND) | Means |
|---|---|
| `a OR b AND c` | `a OR (b AND c)` |
| `a AND b OR c` | `(a AND b) OR c` |
| `a b OR c` | `(a AND b) OR c` |
| `(a OR b) c` | `(a OR b) AND c` |

::: info Foundatio.Parsers compatibility
Foundatio.Parsers had no precedence and grouped everything to the right, so it read `a AND b OR c` as `a AND (b OR c)`. Queries that mix AND and OR without parentheses can change meaning when you migrate; see [Migrating from Foundatio.Parsers](./migrating-from-parsers).
:::

### Required and prohibited clauses

`+` and `-` follow Lucene: within a boolean level, every required clause must match, no prohibited clause may match, and when there are no required clauses at least one of the others must match. With required clauses present, the other clauses only affect scoring.

| Query | Default OR | Default AND |
|---|---|---|
| `a b` | a or b | a and b |
| `+a b` | a (b only boosts the score) | a and b |
| `-a b` | b and not a | b and not a |
| `a -b c` | (a or c) and not b | a and c and not b |

### NOT

`NOT x` (and `!x`) excludes, exactly like `-x`, with one exception: an alternative of an explicit OR is a boolean negation.

| Query | Means |
|---|---|
| `a NOT b` | a and not b (with either default operator) |
| `a AND NOT b` | a and not b |
| `a OR NOT b` | a, or anything that is not b |
| `NOT a OR b` | anything that is not a, or b |
| `NOT a` | everything except a |

A query made only of prohibited clauses matches everything those clauses exclude.

## Ranges

Brackets are inclusive and braces exclusive; they can be mixed. `*` is an open end, and `..` can replace `TO`.

```
price:[10 TO 100]
price:{10 TO 100}
price:[10 TO 100}
price:[10 TO *]
created:[2024-01-01 TO now]
price:[1..5]
temperature:[-10 TO -5]
name:["a b" TO "c d"]
```

`TO` is only a keyword inside a range; elsewhere it's an ordinary term (`go TO school`).

### Comparison operators

```
price:>10
price:>=10
price:<100
price:<=100
temperature:<-5
created:>=2024-01-01T10:30:00
```

Times such as `10:30:00` can be written without quotes in field values and ranges. A colon that isn't between digits ends the value, so `a:b:c` is an error.

## Special queries

| Syntax | Meaning |
|---|---|
| `_exists_:field` or `field:*` | the field has a value |
| `_missing_:field` | the field has no value |
| `*` or `*:*` | all documents |
| `/regex/` | regular expression (on a field: `name:/jo.*/`) |
| `@include:name` | expands a saved query (see [Includes](./configuration#includes)) |

Regular expressions keep their escapes (`/a\/b/`), and support depends on the data store.

## Date math

Date values in ranges and terms can use Elasticsearch date math:

```
created:[now-7d TO now]
created:>=now/d
created:[2024-01-01||+1M/d TO *]
```

See [Date Math](./date-math) for the full syntax, rounding, and time zones.

## Errors

Parsing never throws for malformed input. `LuceneQuery.Parse` returns every problem (with position, line, and column) and a document containing everything that could be parsed. Unexpected tokens, dangling operators (`a AND`), unmatched parentheses or brackets, unterminated phrases or regular expressions, a trailing lone backslash, empty groups, operators after a field's colon, and duplicate modifiers are all reported.

```csharp
var result = LuceneQuery.Parse("title:(hello OR");
if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.Line}:{error.Column} {error.Message}");
}
```

Parenthesized groups can be nested at most `LuceneParserOptions.MaxDepth` (default 100) levels deep; deeper input is reported as an error instead of exhausting the stack.

## Round-tripping

`QueryStringBuilder.ToQueryString(document)` turns a parsed (or modified) tree back into query text. The result parses back into a tree with the same meaning, and the operators and prefixes the user wrote are kept where they are still valid:

```csharp
var document = LuceneQuery.Parse("title:(a OR b) -status:closed").Document;
string text = QueryStringBuilder.ToQueryString(document); // title:(a OR b) -status:closed
```

When the text will be parsed with a different default operator, pass it: `QueryStringBuilder.ToQueryString(document, BooleanOperator.Or)` makes juxtaposed AND clauses explicit.
