using Foundatio.LuceneQuery.Ast;
using Foundatio.LuceneQuery.Visitors;

namespace Foundatio.LuceneQuery.Tests;

/// <summary>
/// Tests for the FieldResolverQueryVisitor.
/// </summary>
public class FieldResolverQueryVisitorTests
{
    private static string ToQueryString(QueryDocument document)
    {
        return QueryStringBuilder.ToQueryString(document);
    }

    #region Simple Field Resolution

    [Fact]
    public async Task CanResolveSimpleField()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "actualField" }
        };

        var result = LuceneQuery.Parse("alias:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap, context);

        var query = ToQueryString(result.Document);
        Assert.Equal("actualField:value", query);
    }

    [Fact]
    public async Task CanResolveCaseInsensitiveField()
    {
        var fieldMap = new FieldMap
        {
            { "ALIAS", "actualField" }
        };

        var result = LuceneQuery.Parse("alias:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap, context);

        var query = ToQueryString(result.Document);
        Assert.Equal("actualField:value", query);
    }

    [Fact]
    public async Task CanResolveMultipleFields()
    {
        var fieldMap = new FieldMap
        {
            { "field1", "resolved1" },
            { "field2", "resolved2" }
        };

        var result = LuceneQuery.Parse("field1:value1 AND field2:value2");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("resolved1:value1 AND resolved2:value2", query);
    }

    [Fact]
    public async Task UnresolvedFieldsAreTracked()
    {
        var fieldMap = new FieldMap
        {
            { "known", "resolved" }
        };

        var result = LuceneQuery.Parse("known:value1 unknown:value2");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        // Use ToFieldResolver instead of ToHierarchicalFieldResolver to track unresolved fields
        context.SetFieldResolver(fieldMap.ToFieldResolver());
        await new FieldResolverQueryVisitor().RunAsync(result.Document, context);

        var validationResult = context.GetValidationResult();
        Assert.Contains("unknown", validationResult.UnresolvedFields);
        Assert.DoesNotContain("known", validationResult.UnresolvedFields);
    }

    [Fact]
    public async Task FieldNotInMapRemainsUnchangedWithHierarchicalResolver()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "actualField" }
        };

        var result = LuceneQuery.Parse("other:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        // Hierarchical resolver returns original field if no match
        Assert.Equal("other:value", query);
    }

    #endregion

    #region Hierarchical Field Resolution

    [Fact]
    public async Task CanResolveNestedField()
    {
        var fieldMap = new FieldMap
        {
            { "data", "resolved" }
        };

        var result = LuceneQuery.Parse("data.subfield:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("resolved.subfield:value", query);
    }

    [Fact]
    public async Task CanResolveDeeplyNestedField()
    {
        var fieldMap = new FieldMap
        {
            { "root", "mappedRoot" }
        };

        var result = LuceneQuery.Parse("root.level1.level2.level3:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("mappedRoot.level1.level2.level3:value", query);
    }

    [Fact]
    public async Task CanResolveMiddleOfPath()
    {
        var fieldMap = new FieldMap
        {
            { "data.nested", "resolved.path" }
        };

        var result = LuceneQuery.Parse("data.nested.field:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("resolved.path.field:value", query);
    }

    [Fact]
    public async Task ExactMatchTakesPrecedenceOverHierarchical()
    {
        var fieldMap = new FieldMap
        {
            { "data", "wrong" },
            { "data.subfield", "exactMatch" }
        };

        var result = LuceneQuery.Parse("data.subfield:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("exactMatch:value", query);
    }

    [Fact]
    public async Task HierarchicalResolverWithPrefix()
    {
        var fieldMap = new FieldMap
        {
            { "field", "mappedField" }
        };

        var resolver = fieldMap.ToHierarchicalFieldResolver("prefix.");

        var result = LuceneQuery.Parse("field:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        context.SetFieldResolver(resolver);
        await new FieldResolverQueryVisitor().RunAsync(result.Document, context);

        var query = ToQueryString(result.Document);
        Assert.Equal("prefix.mappedField:value", query);
    }

    #endregion

    #region Exists and Missing Nodes

    [Fact]
    public async Task CanResolveExistsNode()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "actualField" }
        };

        var result = LuceneQuery.Parse("_exists_:alias");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("_exists_:actualField", query);
    }

    [Fact]
    public async Task CanResolveMissingNode()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "actualField" }
        };

        var result = LuceneQuery.Parse("_missing_:alias");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("_missing_:actualField", query);
    }

    [Fact]
    public async Task CanResolveExistsWithWildcardSyntax()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "actualField" }
        };

        var result = LuceneQuery.Parse("alias:*");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("actualField:*", query);
    }

    #endregion

    #region Range Queries

    [Fact]
    public async Task CanResolveRangeField()
    {
        var fieldMap = new FieldMap
        {
            { "date", "timestamp" }
        };

        var result = LuceneQuery.Parse("date:[2020-01-01 TO 2020-12-31]");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("timestamp:[2020-01-01 TO 2020-12-31]", query);
    }

    [Fact]
    public async Task CanResolveShortFormRangeField()
    {
        var fieldMap = new FieldMap
        {
            { "age", "person.age" }
        };

        var result = LuceneQuery.Parse("age:>18");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("person.age:>18", query);
    }

    #endregion

    #region Phrase and Boolean Queries

    [Fact]
    public async Task CanResolveFieldWithPhrase()
    {
        var fieldMap = new FieldMap
        {
            { "title", "document.title" }
        };

        var result = LuceneQuery.Parse("title:\"hello world\"");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("document.title:\"hello world\"", query);
    }

    [Fact]
    public async Task CanResolveFieldsInBooleanQuery()
    {
        var fieldMap = new FieldMap
        {
            { "name", "person.name" },
            { "age", "person.age" }
        };

        var result = LuceneQuery.Parse("name:John AND age:25");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("person.name:John AND person.age:25", query);
    }

    [Fact]
    public async Task CanResolveFieldsInNestedGroups()
    {
        var fieldMap = new FieldMap
        {
            { "field1", "resolved1" },
            { "field2", "resolved2" }
        };

        var result = LuceneQuery.Parse("(field1:value1 OR field2:value2)");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("(resolved1:value1 OR resolved2:value2)", query);
    }

    #endregion

    #region Custom Resolvers

    [Fact]
    public async Task CanUseSynchronousResolver()
    {
        var result = LuceneQuery.Parse("alias:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, field => 
            field == "alias" ? "resolvedField" : null);

        var query = ToQueryString(result.Document);
        Assert.Equal("resolvedField:value", query);
    }

    [Fact]
    public async Task CanUseAsyncResolver()
    {
        QueryFieldResolver resolver = (field, ctx) =>
        {
            if (field == "alias")
                return Task.FromResult<string?>("resolvedField");
            return Task.FromResult<string?>(null);
        };

        var result = LuceneQuery.Parse("alias:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, resolver);

        var query = ToQueryString(result.Document);
        Assert.Equal("resolvedField:value", query);
    }

    [Fact]
    public async Task ContextResolverTakesPrecedenceOverGlobalResolver()
    {
        QueryFieldResolver globalResolver = (field, ctx) =>
            Task.FromResult<string?>("global." + field);

        QueryFieldResolver contextResolver = (field, ctx) =>
            field == "special" ? Task.FromResult<string?>("context.special") : Task.FromResult<string?>(null);

        var result = LuceneQuery.Parse("special:value other:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        context.SetFieldResolver(contextResolver);
        var visitor = new FieldResolverQueryVisitor(globalResolver);
        await visitor.RunAsync(result.Document, context);

        var query = ToQueryString(result.Document);
        Assert.Equal("context.special:value global.other:value", query);
    }

    #endregion

    #region Original Field Tracking

    [Fact]
    public async Task OriginalFieldIsTracked()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "actualField" }
        };

        var result = LuceneQuery.Parse("alias:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        await FieldResolverQueryVisitor.RunAsync(result.Document!, fieldMap, context);

        // Find the FieldQueryNode
        var fieldNode = FindFirstFieldQueryNode(result.Document!.Query!);
        Assert.NotNull(fieldNode);
        Assert.Equal("actualField", fieldNode.Field);
        
        var originalField = fieldNode.GetOriginalField(context);
        Assert.Equal("alias", originalField);
    }

    [Fact]
    public async Task OriginalFieldNotSetWhenNoChange()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "actualField" }
        };

        var result = LuceneQuery.Parse("other:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        await FieldResolverQueryVisitor.RunAsync(result.Document!, fieldMap, context);

        // Find the FieldQueryNode
        var fieldNode = FindFirstFieldQueryNode(result.Document!.Query!);
        Assert.NotNull(fieldNode);
        Assert.Equal("other", fieldNode.Field); // Unchanged due to hierarchical resolver
        
        var originalField = fieldNode.GetOriginalField(context);
        Assert.Null(originalField); // Not set because field didn't change
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task ResolverExceptionAddsValidationError()
    {
        QueryFieldResolver badResolver = (field, ctx) =>
            throw new InvalidOperationException("Test error");

        var result = LuceneQuery.Parse("field:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        context.SetFieldResolver(badResolver);
        await new FieldResolverQueryVisitor().RunAsync(result.Document, context);

        var validationResult = context.GetValidationResult();
        Assert.False(validationResult.IsValid);
        Assert.Single(validationResult.ValidationErrors);
        Assert.Contains("Test error", validationResult.ValidationErrors.First().Message);
    }

    [Fact]
    public async Task NoResolverDoesNothing()
    {
        var result = LuceneQuery.Parse("field:value");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        // No resolver set
        await new FieldResolverQueryVisitor().RunAsync(result.Document, context);

        var query = ToQueryString(result.Document);
        Assert.Equal("field:value", query);
        Assert.True(context.GetValidationResult().IsValid);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public async Task CanResolveComplexQuery()
    {
        var fieldMap = new FieldMap
        {
            { "user", "account.user" },
            { "created", "metadata.timestamp" },
            { "status", "workflow.status" }
        };

        var result = LuceneQuery.Parse("(user:john OR user:jane) AND created:[2020-01-01 TO 2020-12-31] AND status:active");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("(account.user:john OR account.user:jane) AND metadata.timestamp:[2020-01-01 TO 2020-12-31] AND workflow.status:active", query);
    }

    [Fact]
    public async Task CanResolveNotQueries()
    {
        var fieldMap = new FieldMap
        {
            { "field", "resolved" }
        };

        var result = LuceneQuery.Parse("NOT field:value");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("NOT resolved:value", query);
    }

    [Fact]
    public async Task CanResolveBoostAndFuzzy()
    {
        var fieldMap = new FieldMap
        {
            { "title", "document.title" }
        };

        // Use ~1 instead of ~2 since ~2 is the default and won't be output
        var result = LuceneQuery.Parse("title:search~1^2");
        Assert.True(result.IsSuccess);

        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap);

        var query = ToQueryString(result.Document);
        Assert.Equal("document.title:search~1^2", query);
    }

    [Fact]
    public async Task CanCombineWithIncludeVisitor()
    {
        var fieldMap = new FieldMap
        {
            { "alias", "resolved" }
        };

        var includes = new Dictionary<string, string>
        {
            { "myfilter", "alias:value" }
        };

        var result = LuceneQuery.Parse("@include:myfilter");
        Assert.True(result.IsSuccess);

        var context = new QueryVisitorContext();
        
        // First run include visitor
        context.SetIncludeResolver(name => Task.FromResult(includes.GetValueOrDefault(name)));
        await new IncludeVisitor().RunAsync(result.Document, context);

        // Then run field resolver
        await FieldResolverQueryVisitor.RunAsync(result.Document, fieldMap, context);

        var query = ToQueryString(result.Document);
        Assert.Equal("(resolved:value)", query);
    }

    #endregion

    #region Helpers

    private static FieldQueryNode? FindFirstFieldQueryNode(QueryNode node)
    {
        return node switch
        {
            FieldQueryNode fieldNode => fieldNode,
            BooleanQueryNode boolNode => boolNode.Clauses
                .Select(c => FindFirstFieldQueryNode(c.Query!))
                .FirstOrDefault(n => n != null),
            GroupNode groupNode when groupNode.Query != null => FindFirstFieldQueryNode(groupNode.Query),
            NotNode notNode when notNode.Query != null => FindFirstFieldQueryNode(notNode.Query),
            _ => null
        };
    }

    #endregion
}
