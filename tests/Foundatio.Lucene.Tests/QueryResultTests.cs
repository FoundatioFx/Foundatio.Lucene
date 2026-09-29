namespace Foundatio.Lucene.Tests;

public class QueryResultTests
{
    [Fact]
    public void Success_ExposesValue_AndReportsSuccess()
    {
        var result = QueryResult<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
        Assert.True(result); // implicit bool
    }

    [Fact]
    public void Failure_ExposesError_AndThrowsOnValue()
    {
        var result = QueryResult<int>.Failure(new QueryParseException("bad", QueryErrorCode.UnexpectedToken));

        Assert.True(result.IsFailure);
        Assert.Equal(QueryErrorCode.UnexpectedToken, result.ErrorCode);
        Assert.Equal("bad", result.ErrorMessage);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.False(result); // implicit bool
    }

    [Fact]
    public void GetValueOrThrow_RethrowsOriginalException()
    {
        var ex = new QueryBuildException("nope", QueryErrorCode.UnsupportedQueryType);
        var result = QueryResult<string>.Failure(ex);

        var thrown = Assert.Throws<QueryBuildException>(() => result.GetValueOrThrow());
        Assert.Same(ex, thrown);
    }

    [Fact]
    public void GetValueOrDefault_ReturnsFallbackOnFailure()
    {
        var result = QueryResult<string>.Failure("oops");

        Assert.Equal("fallback", result.GetValueOrDefault("fallback"));
    }

    [Fact]
    public void Map_TransformsSuccess_AndPreservesFailure()
    {
        Assert.Equal(6, QueryResult<int>.Success(3).Map(v => v * 2).Value);

        var failed = QueryResult<int>.Failure("err").Map(v => v * 2);
        Assert.True(failed.IsFailure);
    }

    [Fact]
    public void Try_CapturesQueryExceptionAsFailure()
    {
        var result = QueryResult.Try<int>(() => throw new QueryParseException("boom"));

        Assert.True(result.IsFailure);
        Assert.Equal(QueryErrorCode.ParseError, result.ErrorCode);
    }

    [Fact]
    public void Deconstruct_YieldsStatusValueAndError()
    {
        var (isSuccess, value, error) = QueryResult<int>.Success(7);

        Assert.True(isSuccess);
        Assert.Equal(7, value);
        Assert.Null(error);
    }
}
