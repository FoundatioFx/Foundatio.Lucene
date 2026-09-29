# Custom Visitors

Write a visitor to transform queries, add validation rules, or change how particular fields are queried. Visitors are synchronous; do any I/O in a [resolver](./configuration#asynchronous-resolution) before the pipeline runs.

## Visitor basics

Derive from `QueryVisitor` and override `Visit` for the node types you care about. Each override returns the node to keep — the same node, a modified one, or a replacement. Call `base.Visit` to visit a node's children.

```csharp
using Foundatio.Lucene.Ast;
using Foundatio.Lucene.Visitors;

public class LowercaseTermsVisitor : QueryVisitor
{
    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        node.Term = node.Term.ToLowerInvariant();
        return node;
    }
}
```

The node types are:

| Node | Represents |
|---|---|
| `QueryDocument` | the root; `Query` is null for an empty query |
| `BooleanQueryNode` | clauses, each with an `Occur` (`Must`, `Should`, `MustNot`) |
| `GroupNode` | parentheses, with an optional boost |
| `NotNode` | `NOT x` as an alternative of an explicit OR |
| `FieldQueryNode` | `field:value`; `Query` is the value (a term, phrase, range, group, ...) |
| `TermNode` | a term: `Term` (raw, escapes kept), `UnescapedTerm`, `IsPrefix`, `IsWildcard`, `FuzzyDistance`, `Boost` |
| `PhraseNode` | a quoted phrase (`Phrase`, `Slop`) |
| `RegexNode` | `/pattern/` |
| `RangeNode` | `Min`, `Max` (null when unbounded), `MinInclusive`, `MaxInclusive` |
| `ExistsNode`, `MissingNode` | `_exists_:field`, `field:*`, `_missing_:field` |
| `MatchAllNode` | `*` or `*:*` |

`node.ToDebugString()` prints a compact description of any tree, which is handy in tests: `LuceneQuery.Parse("a -b").Document.ToDebugString()` is `(bool +a -b)`.

## Knowing the current field

A term's field is on the enclosing `FieldQueryNode`. Track it while visiting:

```csharp
public class MaskEmailSearchesVisitor : QueryVisitor
{
    private const string FieldKey = "@MaskEmailSearches.Field";

    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        string? previous = context.GetValue<string>(FieldKey);
        context.SetValue(FieldKey, node.Field);
        try
        {
            return base.Visit(node, context);
        }
        finally
        {
            context.SetValue(FieldKey, previous);
        }
    }

    protected override QueryNode Visit(TermNode node, IQueryVisitorContext context)
    {
        if (context.GetValue<string>(FieldKey) == "email" && !node.IsPrefix && !node.IsWildcard)
            node.Term = QueryText.Escape(node.UnescapedTerm.ToLowerInvariant());

        return node;
    }
}
```

Keep per-query state in the context, not in fields of the visitor, so one visitor instance can serve concurrent requests.

## Replacing nodes

Return a different node to replace one. This visitor turns a friendly `is:open` shortcut into a real clause:

```csharp
public class ShortcutVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field == "is" && node.Query is TermNode { UnescapedTerm: "open" })
            return LuceneQuery.Parse("(status:open OR status:regressed)").Document.Query!;

        return base.Visit(node, context);
    }
}
```

Register it before field resolution (the default priority 0) so the fields it introduces are resolved and validated like any others.

## Adding validation rules

Add errors to the context's validation result; building then fails with a `QueryValidationException`:

```csharp
public class NoFuzzyOnIdsVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field.EndsWith("id", StringComparison.OrdinalIgnoreCase) && node.Query is TermNode { IsFuzzy: true })
            context.ValidationResult.AddError($"Fuzzy searches are not allowed on {node.Field}.", node.StartPosition);

        return base.Visit(node, context);
    }
}
```

## Custom Elasticsearch queries

A visitor can supply the exact Elasticsearch query for a node with `SetQuery`. Here a `@tag:` shortcut becomes a `terms` query on an id list that was looked up before building:

```csharp
public class TagVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field != "@tag" || node.Query is not TermNode term)
            return base.Visit(node, context);

        var ids = context.GetValue<IReadOnlyDictionary<string, string[]>>("TagIds")?.GetValueOrDefault(term.UnescapedTerm) ?? [];
        node.SetQuery(new TermsQuery
        {
            Field = "id",
            Terms = new TermsQueryField(ids.Select(FieldValue.String).ToArray())
        });
        return node;
    }
}
```

Fields starting with `@` are never resolved or validated as data fields, which makes them a good choice for shortcuts like this.

### Custom sorts and aggregations

Visitors added with `AddVisitor`, `AddSortVisitor`, or `AddAggregationVisitor` also run on sort and aggregation expressions. `SetSort` supplies the sort for a node, for example a script sort behind a shortcut:

```csharp
public class PopularitySortVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field != "@popular")
            return base.Visit(node, context);

        node.SetSort(new SortOptions
        {
            Script = new ScriptSort
            {
                Type = ScriptSortType.Number,
                Script = new Script { Source = "doc['likes'].value * 2 + doc['views'].value" },
                Order = SortOrder.Desc
            }
        });
        return node;
    }
}

// sort: @popular:desc -created
```

`SetAggregation` supplies the aggregation for a `type:field` node, including types the provider doesn't know. The aggregation is named `{type}_{field}`, wrapped in nested aggregations for nested fields, and gets the node's sub-aggregations:

```csharp
public class RareTermsVisitor : QueryVisitor
{
    protected override QueryNode Visit(FieldQueryNode node, IQueryVisitorContext context)
    {
        if (node.Field == "rare" && node.Query is TermNode field)
            node.SetAggregation(new RareTermsAggregation { Field = field.UnescapedTerm });

        return base.Visit(node, context);
    }
}

// aggregations: rare:tags terms:(status rare:owner)
```

## Async lookups

Visitors can't await. When a transformation needs data from a database or service:

- For field names and include text, use `AsyncFieldResolver` and `IncludeResolver`; the `Async` build methods run them before the pipeline.
- For anything else, load the data before building and put it on the context or in the visitor's constructor, as the `TagVisitor` above reads `TagIds` from the context.

## Testing visitors

```csharp
[Fact]
public void Visit_ShortcutField_ExpandsToStatusClause()
{
    var document = LuceneQuery.Parse("is:open").Document;

    var result = new ShortcutVisitor().Accept(document, new QueryVisitorContext());

    Assert.Equal("(status:open OR status:regressed)", QueryStringBuilder.ToQueryString(result));
}
```
