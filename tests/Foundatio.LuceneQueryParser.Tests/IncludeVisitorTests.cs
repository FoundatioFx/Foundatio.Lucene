using Foundatio.LuceneQueryParser.Ast;
using Foundatio.LuceneQueryParser.Visitors;

namespace Foundatio.LuceneQueryParser.Tests;

public class IncludeVisitorTests
{
    private readonly Dictionary<string, string> _includes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["simple"] = "status:active",
        ["complex"] = "status:active AND type:user",
        ["nested"] = "@include:simple AND category:test",
        ["recursive1"] = "@include:recursive2",
        ["recursive2"] = "@include:recursive1",
        ["self"] = "@include:self",
        ["empty"] = "",
        ["whitespace"] = "   ",
        ["with-boost"] = "status:active^2",
        ["with-group"] = "(status:active OR status:pending)",
        ["nested2"] = "@include:nested",
        ["invalid"] = "\"unclosed phrase",
        ["term-only"] = "active"
    };

    private Task<string?> TestResolver(string name)
    {
        return Task.FromResult(_includes.TryGetValue(name, out var value) ? value : null);
    }

    private static string ToQueryString(QueryDocument document)
    {
        return new QueryStringBuilder().Visit(document);
    }

    #region Basic Include Expansion

    [Fact]
    public async Task ExpandIncludesAsync_SimpleInclude_ReturnsExpandedQuery()
    {
        // Arrange
        var query = "@include:simple";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_ComplexInclude_ReturnsExpandedQuery()
    {
        // Arrange
        var query = "@include:complex";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active AND type:user)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_IncludeWithOtherTerms_ReturnsExpandedQuery()
    {
        // Arrange
        var query = "@include:simple AND name:test";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active) AND name:test", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_MultipleIncludes_ReturnsExpandedQuery()
    {
        // Arrange
        var query = "@include:simple OR @include:complex";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active) OR (status:active AND type:user)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_NoIncludes_ReturnsOriginalQuery()
    {
        // Arrange
        var query = "status:active AND name:test";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("status:active AND name:test", output);
    }

    #endregion

    #region Nested Includes

    [Fact]
    public async Task ExpandIncludesAsync_NestedInclude_ReturnsFullyExpandedQuery()
    {
        // Arrange
        var query = "@include:nested";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("((status:active) AND category:test)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_DeeplyNestedInclude_ReturnsFullyExpandedQuery()
    {
        // Arrange
        var query = "@include:nested2";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(((status:active) AND category:test))", output);
    }

    #endregion

    #region Recursive Include Detection

    [Fact]
    public async Task ExpandIncludesAsync_SelfRecursiveInclude_DetectsRecursion()
    {
        // Arrange
        var query = "@include:self";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.ValidationErrors, e => e.Message.Contains("Circular"));
    }

    [Fact]
    public async Task ExpandIncludesAsync_MutuallyRecursiveIncludes_DetectsRecursion()
    {
        // Arrange
        var query = "@include:recursive1";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.ValidationErrors, e => e.Message.Contains("Circular"));
    }

    #endregion

    #region Unresolved Includes

    [Fact]
    public async Task ExpandIncludesAsync_UnresolvedInclude_TracksInResult()
    {
        // Arrange
        var query = "@include:nonexistent";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.Contains("nonexistent", validationResult.UnresolvedIncludes);
    }

    [Fact]
    public async Task ExpandIncludesAsync_EmptyInclude_TracksAsUnresolved()
    {
        // Arrange
        var query = "@include:empty";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.Contains("empty", validationResult.UnresolvedIncludes);
    }

    [Fact]
    public async Task ExpandIncludesAsync_WhitespaceInclude_TracksAsUnresolved()
    {
        // Arrange
        var query = "@include:whitespace";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.Contains("whitespace", validationResult.UnresolvedIncludes);
    }

    #endregion

    #region Referenced Includes Tracking

    [Fact]
    public async Task ExpandIncludesAsync_TracksReferencedIncludes()
    {
        // Arrange
        var query = "@include:simple AND @include:complex";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.Contains("simple", validationResult.ReferencedIncludes);
        Assert.Contains("complex", validationResult.ReferencedIncludes);
    }

    [Fact]
    public async Task ExpandIncludesAsync_NestedInclude_TracksAllIncludes()
    {
        // Arrange
        var query = "@include:nested";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.Contains("nested", validationResult.ReferencedIncludes);
        Assert.Contains("simple", validationResult.ReferencedIncludes);
    }

    #endregion

    #region Include Resolver Errors

    [Fact]
    public async Task ExpandIncludesAsync_ResolverThrows_AddsValidationError()
    {
        // Arrange
        var query = "@include:error";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        Task<string?> errorResolver(string name)
        {
            throw new InvalidOperationException("Test error");
        }

        // Act
        await parseResult.Document.ExpandIncludesAsync(errorResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.ValidationErrors, e => e.Message.Contains("Error"));
    }

    [Fact]
    public async Task ExpandIncludesAsync_InvalidIncludeQuery_AddsValidationError()
    {
        // Arrange
        var query = "@include:badquery";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        Task<string?> badResolver(string name)
        {
            return Task.FromResult<string?>("(((("); // Unclosed parentheses
        }

        // Act
        await parseResult.Document.ExpandIncludesAsync(badResolver, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.ValidationErrors, e => e.Message.Contains("Invalid"));
    }

    #endregion

    #region No Resolver

    [Fact]
    public async Task ExpandIncludesAsync_NoResolver_TracksAllAsUnresolved()
    {
        // Arrange
        var query = "@include:simple";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        // Act
        var visitor = new IncludeVisitor();
        await visitor.RunAsync(parseResult.Document, context);

        // Assert
        var validationResult = context.GetValidationResult();
        Assert.Contains("simple", validationResult.UnresolvedIncludes);
    }

    #endregion

    #region Skip Include Function

    [Fact]
    public async Task ExpandIncludesAsync_WithSkipFunction_SkipsSpecifiedIncludes()
    {
        // Arrange
        var query = "@include:simple AND @include:complex";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        bool shouldSkip(FieldQueryNode node, IQueryVisitorContext ctx)
        {
            var name = (node.Query as TermNode)?.Term;
            return name == "simple";
        }

        context.SetIncludeResolver(TestResolver);
        context.SetShouldSkipIncludeFunc(shouldSkip);

        // Act
        await parseResult.Document.ExpandIncludesAsync(context);

        // Assert
        var output = ToQueryString(parseResult.Document);
        Assert.Contains("@include:simple", output);
        Assert.Contains("(status:active AND type:user)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_SkipFunctionFromContext_SkipsSpecifiedIncludes()
    {
        // Arrange
        var query = "@include:simple";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();

        bool shouldSkip(FieldQueryNode node, IQueryVisitorContext ctx) => true;

        context.SetShouldSkipIncludeFunc(shouldSkip);

        // Act
        await parseResult.Document.ExpandIncludesAsync(TestResolver, context);

        // Assert
        var output = ToQueryString(parseResult.Document);
        Assert.Equal("@include:simple", output);
    }

    #endregion

    #region Context-Based Resolver

    [Fact]
    public async Task ExpandIncludesAsync_ResolverFromContext_Works()
    {
        // Arrange
        var query = "@include:simple";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);
        var context = new QueryVisitorContext();
        context.SetIncludeResolver(TestResolver);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(context);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active)", output);
    }

    #endregion

    #region Include with Boost and Other Modifiers

    [Fact]
    public async Task ExpandIncludesAsync_IncludeWithBoost_ReturnsExpandedQuery()
    {
        // Arrange
        var query = "@include:with-boost";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active^2)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_IncludeWithGroup_ReturnsExpandedQuery()
    {
        // Arrange
        var query = "@include:with-group";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("((status:active OR status:pending))", output);
    }

    #endregion

    #region Include with Phrase Value

    [Fact]
    public async Task ExpandIncludesAsync_IncludeWithPhraseValue_Works()
    {
        // Arrange
        var includes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["my include"] = "status:active"
        };

        Task<string?> resolver(string name) =>
            Task.FromResult(includes.TryGetValue(name, out var value) ? value : null);

        var query = "@include:\"my include\"";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(resolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active)", output);
    }

    #endregion

    #region Integration with ChainedVisitor

    [Fact]
    public async Task IncludeVisitor_WithChainedVisitor_WorksCorrectly()
    {
        // Arrange
        var query = "@include:simple AND name:Test";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        var context = new QueryVisitorContext();
        context.SetIncludeResolver(TestResolver);

        var chainedVisitor = new ChainedQueryVisitor()
            .AddVisitor(new IncludeVisitor(), 0)
            .AddVisitor(new LowercaseFieldVisitor(), 1);

        // Act
        var result = await chainedVisitor.RunAsync(parseResult.Document, context);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active) AND name:test", output);
    }

    // Helper visitor for chained test
    private class LowercaseFieldVisitor : QueryNodeVisitor
    {
        public override async Task<QueryNode> VisitAsync(FieldQueryNode node, IQueryVisitorContext context)
        {
            await base.VisitAsync(node, context).ConfigureAwait(false);

            if (node.Query is TermNode term && term.Term is not null)
                term.Term = term.Term.ToLowerInvariant();

            return node;
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExpandIncludesAsync_IncludeWithTermOnlyValue_Works()
    {
        // Arrange
        var query = "@include:term-only";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(active)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_CaseInsensitiveIncludeName_Works()
    {
        // Arrange
        var query = "@include:SIMPLE";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_CaseInsensitiveFieldName_Works()
    {
        // Arrange  
        var query = "@INCLUDE:simple";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:active)", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_InGroup_Works()
    {
        // Arrange
        var query = "(status:pending OR @include:simple)";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("(status:pending OR (status:active))", output);
    }

    [Fact]
    public async Task ExpandIncludesAsync_WithNot_Works()
    {
        // Arrange
        var query = "NOT @include:simple";
        var parseResult = LuceneQuery.Parse(query);
        Assert.True(parseResult.IsSuccess);

        // Act
        var result = await parseResult.Document.ExpandIncludesAsync(TestResolver);

        // Assert
        var output = ToQueryString(result);
        Assert.Equal("NOT (status:active)", output);
    }

    #endregion
}
