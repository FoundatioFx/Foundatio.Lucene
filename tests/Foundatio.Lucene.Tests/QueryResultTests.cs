namespace Foundatio.Lucene.Tests;

public class QueryResultTests
{
    [Fact]
    public void Success_Value_ReportsSuccess()
    {
        // Act
        var result = QueryResult.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.True(result);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.True(result.TryGetValue(out int value));
        Assert.Equal(42, value);
        Assert.Equal(42, result.GetValueOrThrow());
        Assert.Equal(42, result.GetValueOrDefault(7));
    }

    [Fact]
    public void Failure_MessageAndCode_ReportsFailure()
    {
        // Act
        var result = QueryResult.Failure<int>("bad query", QueryErrorCode.InvalidRange);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.False(result);
        Assert.Equal(QueryErrorCode.InvalidRange, result.ErrorCode);
        Assert.Equal("bad query", result.ErrorMessage);
        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("bad query", exception.Message);
        Assert.False(result.TryGetValue(out _));
        Assert.Equal(0, result.GetValueOrDefault());
        Assert.Equal(7, result.GetValueOrDefault(7));
    }

    [Fact]
    public void Failure_DefaultCode_IsUnknown()
    {
        Assert.Equal(QueryErrorCode.Unknown, QueryResult<string>.Failure("x").ErrorCode);
    }

    [Fact]
    public void GetValueOrThrow_Failure_RethrowsOriginalException()
    {
        var error = new QueryBuildException("cannot build", QueryErrorCode.UnsupportedQueryType);
        var result = QueryResult.Failure<string>(error);

        var thrown = Assert.Throws<QueryBuildException>(() => result.GetValueOrThrow());

        Assert.Same(error, thrown);
    }

    [Fact]
    public void Map_Success_TransformsValue()
    {
        var result = QueryResult.Success(21).Map(v => v * 2);

        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Map_Failure_PreservesErrorWithoutCallingMapper()
    {
        var error = new QueryException("boom");
        bool called = false;

        var result = QueryResult.Failure<int>(error).Map(v =>
        {
            called = true;
            return v.ToString();
        });

        Assert.False(called);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void OnSuccessAndOnFailure_CallOnlyMatchingCallback()
    {
        // Arrange
        var calls = new List<string>();

        // Act
        QueryResult.Success(1).OnSuccess(v => calls.Add($"success {v}")).OnFailure(e => calls.Add("failure"));
        QueryResult.Failure<int>("err").OnSuccess(_ => calls.Add("success")).OnFailure(e => calls.Add($"failure {e.Message}"));

        // Assert
        Assert.Equal(["success 1", "failure err"], calls);
    }

    [Fact]
    public void Try_QueryException_ReturnsFailureWithSameException()
    {
        var error = new QueryParseException("parse failed");

        var result = QueryResult.Try<int>(() => throw error);

        Assert.Same(error, result.Error);
        Assert.Equal(QueryErrorCode.ParseError, result.ErrorCode);
    }

    [Fact]
    public void Try_OtherException_WrapsInQueryException()
    {
        var error = new InvalidOperationException("oops");

        var result = QueryResult.Try<int>(() => throw error);

        Assert.Equal(QueryErrorCode.Unknown, result.ErrorCode);
        Assert.Equal("oops", result.ErrorMessage);
        Assert.Same(error, result.Error!.InnerException);
    }

    [Fact]
    public void Try_Success_ReturnsValue()
    {
        Assert.Equal("ok", QueryResult.Try(() => "ok").Value);
    }

    [Fact]
    public async Task TryAsync_Operations_CaptureResultsAndExceptions()
    {
        var success = await QueryResult.TryAsync(async () =>
        {
            await Task.Yield();
            return 5;
        });
        var validationFailure = await QueryResult.TryAsync<int>(async () =>
        {
            await Task.Yield();
            throw new QueryValidationException("invalid");
        });
        var otherFailure = await QueryResult.TryAsync<int>(async () =>
        {
            await Task.Yield();
            throw new FormatException("format");
        });

        Assert.Equal(5, success.Value);
        Assert.IsType<QueryValidationException>(validationFailure.Error);
        Assert.IsType<FormatException>(otherFailure.Error!.InnerException);
    }

    [Fact]
    public void Deconstruct_Result_YieldsStatusValueAndError()
    {
        var (isSuccess, value, error) = QueryResult.Success("v");
        var (failed, failedValue, failedError) = QueryResult.Failure<string>("e");

        Assert.True(isSuccess);
        Assert.Equal("v", value);
        Assert.Null(error);
        Assert.False(failed);
        Assert.Null(failedValue);
        Assert.Equal("e", failedError!.Message);
    }

    [Fact]
    public void QueryException_ToString_IncludesCodePositionAndField()
    {
        var exception = new QueryException("bad", QueryErrorCode.InvalidFieldName) { Position = 4, FieldName = "name" };

        Assert.Equal("[InvalidFieldName] bad at position 4 (field: name)", exception.ToString());
        Assert.Equal("[Unknown] plain", new QueryException("plain").ToString());
    }

    [Fact]
    public void GetDocumentOrThrow_ParseErrors_ThrowsQueryParseExceptionWithErrors()
    {
        var parsed = LuceneQuery.Parse("a:(b");

        var exception = Assert.Throws<QueryParseException>(() => parsed.GetDocumentOrThrow());

        Assert.Equal(parsed.Errors, exception.Errors);
        Assert.StartsWith("Failed to parse query:", exception.Message);
    }

    [Fact]
    public void GetDocumentOrThrow_Success_ReturnsDocument()
    {
        var parsed = LuceneQuery.Parse("a:b");

        Assert.Same(parsed.Document, parsed.GetDocumentOrThrow());
    }

    [Fact]
    public void QueryValidationException_WithoutResult_HasEmptyResult()
    {
        var exception = new QueryValidationException("invalid", QueryErrorCode.FieldRestricted);

        Assert.Equal(QueryErrorCode.FieldRestricted, exception.ErrorCode);
        Assert.True(exception.Result.IsValid);
        Assert.Empty(exception.Errors);
    }
}
