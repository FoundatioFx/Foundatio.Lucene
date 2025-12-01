using Foundatio.LuceneQuery.Visitors;

namespace Foundatio.LuceneQuery.Tests;

public class QueryValidatorTests
{
    [Fact]
    public async Task ValidateQueryAsync_ValidQuery_ReturnsIsValid()
    {
        var result = await QueryValidator.ValidateQueryAsync("title:hello");

        Assert.True(result.IsValid);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public async Task ValidateQueryAsync_InvalidSyntax_ReturnsErrors()
    {
        var result = await QueryValidator.ValidateQueryAsync("title:");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ValidationErrors);
    }

    [Fact]
    public async Task ValidateQueryAsync_AllowedFields_ValidField_ReturnsIsValid()
    {
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("title");
        options.AllowedFields.Add("author");

        var result = await QueryValidator.ValidateQueryAsync("title:hello AND author:john", options);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateQueryAsync_AllowedFields_NonAllowedField_ReturnsError()
    {
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("title");

        var result = await QueryValidator.ValidateQueryAsync("title:hello AND status:active", options);

        Assert.False(result.IsValid);
        Assert.Contains("status", result.Message);
        Assert.Contains("not allowed", result.Message);
    }

    [Fact]
    public async Task ValidateQueryAsync_RestrictedFields_ReturnsError()
    {
        var options = new QueryValidationOptions();
        options.RestrictedFields.Add("password");

        var result = await QueryValidator.ValidateQueryAsync("password:secret", options);

        Assert.False(result.IsValid);
        Assert.Contains("password", result.Message);
        Assert.Contains("restricted", result.Message);
    }

    [Fact]
    public async Task ValidateQueryAsync_AllowLeadingWildcards_False_ReturnsError()
    {
        var options = new QueryValidationOptions
        {
            AllowLeadingWildcards = false
        };

        var result = await QueryValidator.ValidateQueryAsync("title:*hello", options);

        Assert.False(result.IsValid);
        Assert.Contains("wildcard", result.Message);
    }

    [Fact]
    public async Task ValidateQueryAsync_AllowLeadingWildcards_True_ReturnsValid()
    {
        var options = new QueryValidationOptions
        {
            AllowLeadingWildcards = true
        };

        var result = await QueryValidator.ValidateQueryAsync("title:*hello", options);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateQueryAsync_LeadingQuestionMark_WhenNotAllowed_ReturnsError()
    {
        var options = new QueryValidationOptions
        {
            AllowLeadingWildcards = false
        };

        var result = await QueryValidator.ValidateQueryAsync("title:?ello", options);

        Assert.False(result.IsValid);
        Assert.Contains("wildcard", result.Message);
    }

    [Fact]
    public async Task ValidateQueryAsync_MaxNodeDepth_Exceeded_ReturnsError()
    {
        var options = new QueryValidationOptions
        {
            AllowedMaxNodeDepth = 2
        };

        // Three levels of nesting: (outer (middle (inner)))
        var result = await QueryValidator.ValidateQueryAsync("(title:a OR (author:b AND (status:c)))", options);

        Assert.False(result.IsValid);
        Assert.Contains("depth", result.Message);
    }

    [Fact]
    public async Task ValidateQueryAsync_MaxNodeDepth_NotExceeded_ReturnsValid()
    {
        var options = new QueryValidationOptions
        {
            AllowedMaxNodeDepth = 3
        };

        // Two levels of nesting
        var result = await QueryValidator.ValidateQueryAsync("(title:a OR (author:b))", options);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateQueryAsync_RestrictedOperations_ReturnsError()
    {
        var options = new QueryValidationOptions();
        options.RestrictedOperations.Add("regex");

        var result = await QueryValidator.ValidateQueryAsync("title:/pattern/", options);

        Assert.False(result.IsValid);
        Assert.Contains("regex", result.Message);
        Assert.Contains("restricted", result.Message);
    }

    [Fact]
    public async Task ValidateQueryAsync_AllowedOperations_ValidOperation_ReturnsValid()
    {
        var options = new QueryValidationOptions();
        options.AllowedOperations.Add("term");
        options.AllowedOperations.Add("field");

        var result = await QueryValidator.ValidateQueryAsync("title:hello", options);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateQueryAsync_AllowedOperations_NonAllowedOperation_ReturnsError()
    {
        var options = new QueryValidationOptions();
        options.AllowedOperations.Add("term");
        options.AllowedOperations.Add("field");

        var result = await QueryValidator.ValidateQueryAsync("title:/pattern/", options);

        Assert.False(result.IsValid);
        Assert.Contains("regex", result.Message);
        Assert.Contains("not allowed", result.Message);
    }

    [Fact]
    public async Task ValidateQueryAndThrowAsync_InvalidQuery_ThrowsException()
    {
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("title");

        var ex = await Assert.ThrowsAsync<QueryValidationException>(async () =>
            await QueryValidator.ValidateQueryAndThrowAsync("status:active", options));

        Assert.False(ex.Result.IsValid);
        Assert.Contains("status", ex.Message);
    }

    [Fact]
    public async Task ValidateQueryAsync_CollectsReferencedFields()
    {
        var result = await QueryValidator.ValidateQueryAsync("title:hello author:john status:active");

        Assert.True(result.IsValid);
        Assert.Contains("title", result.ReferencedFields);
        Assert.Contains("author", result.ReferencedFields);
        Assert.Contains("status", result.ReferencedFields);
    }

    [Fact]
    public async Task ValidateQueryAsync_ExistsNode_AddsFieldToReferencedFields()
    {
        var result = await QueryValidator.ValidateQueryAsync("_exists_:email");

        Assert.True(result.IsValid);
        Assert.Contains("email", result.ReferencedFields);
        Assert.Contains("exists", result.Operations.Keys);
    }

    [Fact]
    public async Task ValidateQueryAsync_MissingNode_AddsFieldToReferencedFields()
    {
        var result = await QueryValidator.ValidateQueryAsync("_missing_:email");

        Assert.True(result.IsValid);
        Assert.Contains("email", result.ReferencedFields);
        Assert.Contains("missing", result.Operations.Keys);
    }

    [Fact]
    public async Task ValidateQueryAsync_TracksOperations()
    {
        var result = await QueryValidator.ValidateQueryAsync("title:hello title:[a TO z] title:/pattern/ NOT status:active");

        Assert.True(result.IsValid);
        Assert.Contains("term", result.Operations.Keys);
        Assert.Contains("range", result.Operations.Keys);
        Assert.Contains("regex", result.Operations.Keys);
        Assert.Contains("not", result.Operations.Keys);
        Assert.Contains("field", result.Operations.Keys);
    }

    [Fact]
    public async Task Document_ValidateAsync_Extension_Works()
    {
        var document = LuceneQuery.Parse("title:hello").Document;

        var result = await document.ValidateAsync();

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Document_ValidateAsync_WithAllowedFields_Works()
    {
        var document = LuceneQuery.Parse("title:hello status:active").Document;

        var result = await document.ValidateAsync(["title"]);

        Assert.False(result.IsValid);
        Assert.Contains("status", result.Message);
    }

    [Fact]
    public Task Document_ValidateAndThrowAsync_ThrowsOnInvalid()
    {
        var document = LuceneQuery.Parse("title:hello").Document;
        var options = new QueryValidationOptions();
        options.AllowedFields.Add("author");

        return Assert.ThrowsAsync<QueryValidationException>(() =>
            document.ValidateAndThrowAsync(options));
    }

    [Fact]
    public async Task ParseResult_ValidateAsync_Extension_Works()
    {
        var parseResult = LuceneQuery.Parse("title:hello");

        var result = await parseResult.ValidateAsync();

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidationVisitor_RunAsync_WithAllowedFields_Works()
    {
        var document = LuceneQuery.Parse("title:hello author:john").Document;

        var result = await ValidationVisitor.RunAsync(document, ["title", "author"]);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidationResult_ImplicitBoolConversion_Works()
    {
        var validResult = await QueryValidator.ValidateQueryAsync("title:hello");

        // Test implicit bool conversion
        Assert.True(validResult);
    }

    [Fact]
    public async Task ValidateQueryAsync_MultipleErrors_AllReported()
    {
        var options = new QueryValidationOptions
        {
            AllowLeadingWildcards = false
        };
        options.RestrictedFields.Add("password");

        var result = await QueryValidator.ValidateQueryAsync("password:secret *wildcard", options);

        Assert.False(result.IsValid);
        // Should have at least one error
        Assert.NotEmpty(result.ValidationErrors);
    }

    [Fact]
    public void QueryValidationError_ToString_IncludesIndex()
    {
        var error = new QueryValidationError("Test error", 5);

        var str = error.ToString();

        Assert.Contains("[5]", str);
        Assert.Contains("Test error", str);
    }

    [Fact]
    public void QueryValidationError_ToString_NoIndex_JustMessage()
    {
        var error = new QueryValidationError("Test error");

        var str = error.ToString();

        Assert.Equal("Test error", str);
    }

    [Fact]
    public void QueryValidationException_HasResultAndErrors()
    {
        var result = new QueryValidationResult();
        result.ValidationErrors.Add(new QueryValidationError("Error 1"));
        result.ValidationErrors.Add(new QueryValidationError("Error 2"));

        var ex = new QueryValidationException("Test exception", result);

        Assert.Equal(result, ex.Result);
        Assert.Equal(2, ex.Errors.Count);
    }
}
