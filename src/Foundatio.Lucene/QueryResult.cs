namespace Foundatio.Lucene;

/// <summary>
/// Represents the result of a query operation that may succeed or fail.
/// </summary>
/// <typeparam name="T">The type of the value on success.</typeparam>
public readonly struct QueryResult<T>
{
    private readonly T? _value;
    private readonly QueryException? _error;

    private QueryResult(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private QueryResult(QueryException error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The result value. Throws if the operation failed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing Value on a failed result.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value on a failed result. Error: {_error?.Message}");

    /// <summary>
    /// The error that occurred. Null if the operation succeeded.
    /// </summary>
    public QueryException? Error => _error;

    /// <summary>
    /// The error code if the operation failed.
    /// </summary>
    public QueryErrorCode? ErrorCode => _error?.ErrorCode;

    /// <summary>
    /// The error message if the operation failed.
    /// </summary>
    public string? ErrorMessage => _error?.Message;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static QueryResult<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static QueryResult<T> Failure(QueryException error) => new(error);

    /// <summary>
    /// Creates a failed result with a message and error code.
    /// </summary>
    public static QueryResult<T> Failure(string message, QueryErrorCode errorCode = QueryErrorCode.Unknown)
        => new(new QueryException(message, errorCode));

    /// <summary>
    /// Tries to get the value, returning false if the operation failed.
    /// </summary>
    public bool TryGetValue(out T? value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>
    /// Returns the value if successful, or the default value if failed.
    /// </summary>
    public T? GetValueOrDefault() => _value;

    /// <summary>
    /// Returns the value if successful, or the specified default value if failed.
    /// </summary>
    public T GetValueOrDefault(T defaultValue) => IsSuccess ? _value! : defaultValue;

    /// <summary>
    /// Executes the action if the operation succeeded.
    /// </summary>
    public QueryResult<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
            action(_value!);
        return this;
    }

    /// <summary>
    /// Executes the action if the operation failed.
    /// </summary>
    public QueryResult<T> OnFailure(Action<QueryException> action)
    {
        if (IsFailure)
            action(_error!);
        return this;
    }

    /// <summary>
    /// Maps the success value to a new type.
    /// </summary>
    public QueryResult<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess
            ? QueryResult<TNew>.Success(mapper(_value!))
            : QueryResult<TNew>.Failure(_error!);
    }

    /// <summary>
    /// Throws the exception if the operation failed.
    /// </summary>
    /// <exception cref="QueryException">Thrown when the operation failed.</exception>
    public T GetValueOrThrow()
    {
        if (IsFailure)
            throw _error!;
        return _value!;
    }

    /// <summary>
    /// Implicit conversion to bool based on IsSuccess.
    /// </summary>
    public static implicit operator bool(QueryResult<T> result) => result.IsSuccess;

    /// <summary>
    /// Deconstructs the result into success status and value/error.
    /// </summary>
    public void Deconstruct(out bool isSuccess, out T? value, out QueryException? error)
    {
        isSuccess = IsSuccess;
        value = _value;
        error = _error;
    }
}

/// <summary>
/// Static helper methods for QueryResult.
/// </summary>
public static class QueryResult
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static QueryResult<T> Success<T>(T value) => QueryResult<T>.Success(value);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static QueryResult<T> Failure<T>(QueryException error) => QueryResult<T>.Failure(error);

    /// <summary>
    /// Creates a failed result with a message and error code.
    /// </summary>
    public static QueryResult<T> Failure<T>(string message, QueryErrorCode errorCode = QueryErrorCode.Unknown)
        => QueryResult<T>.Failure(message, errorCode);

    /// <summary>
    /// Wraps an operation that may throw into a QueryResult.
    /// </summary>
    public static QueryResult<T> Try<T>(Func<T> operation)
    {
        try
        {
            return QueryResult<T>.Success(operation());
        }
        catch (QueryException ex)
        {
            return QueryResult<T>.Failure(ex);
        }
        catch (Exception ex)
        {
            return QueryResult<T>.Failure(new QueryException(ex.Message, QueryErrorCode.Unknown, ex));
        }
    }

    /// <summary>
    /// Wraps an async operation that may throw into a QueryResult.
    /// </summary>
    public static async Task<QueryResult<T>> TryAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return QueryResult<T>.Success(await operation().ConfigureAwait(false));
        }
        catch (QueryException ex)
        {
            return QueryResult<T>.Failure(ex);
        }
        catch (Exception ex)
        {
            return QueryResult<T>.Failure(new QueryException(ex.Message, QueryErrorCode.Unknown, ex));
        }
    }
}
