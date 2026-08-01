namespace ScannerService.Application.Common;

/// <summary>
/// Result pattern for error handling without exceptions.
/// Provides a consistent way to return success/failure with optional value or error.
/// </summary>
public record Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        if (isSuccess && error != null)
        {
            throw new InvalidOperationException("Success result cannot have an error.");
        }

        if (!isSuccess && error == null)
        {
            throw new InvalidOperationException("Failure result must have an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

/// <summary>
/// Result pattern with value.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public record Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        if (isSuccess && error != null)
        {
            throw new InvalidOperationException("Success result cannot have an error.");
        }

        if (!isSuccess && error == null)
        {
            throw new InvalidOperationException("Failure result must have an error.");
        }

        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);

    /// <summary>
    /// Implicit conversion to bool for easy success checking
    /// </summary>
    public static implicit operator bool(Result<T> result) => result.IsSuccess;
}

/// <summary>
/// Extension methods for Result types
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Maps a Result{T} to Result{TResult} by applying a function to the value on success.
    /// </summary>
    public static Result<TResult> Map<T, TResult>(this Result<T> result, Func<T, TResult> func)
    {
        return result.IsSuccess
            ? Result<TResult>.Success(func(result.Value!))
            : Result<TResult>.Failure(result.Error!);
    }

    /// <summary>
    /// Binds a Result{T} to Result{TResult} by applying a function that returns a Result{TResult}.
    /// </summary>
    public static Result<TResult> Bind<T, TResult>(this Result<T> result, Func<T, Result<TResult>> func)
    {
        return result.IsSuccess
            ? func(result.Value!)
            : Result<TResult>.Failure(result.Error!);
    }

    /// <summary>
    /// Executes an action if the result is successful, returning the original result.
    /// </summary>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        if (result.IsSuccess)
        {
            action(result.Value!);
        }
        return result;
    }

    /// <summary>
    /// Executes an action if the result is a failure, returning the original result.
    /// </summary>
    public static Result<T> OnFailure<T>(this Result<T> result, Action<string> action)
    {
        if (result.IsFailure)
        {
            action(result.Error!);
        }
        return result;
    }

    /// <summary>
    /// Returns the value or a default if the result is a failure.
    /// </summary>
    public static T? GetValueOrDefault<T>(this Result<T> result, T? defaultValue = default)
    {
        return result.IsSuccess ? result.Value : defaultValue;
    }

    /// <summary>
    /// Combines multiple results into a single result. Returns failure if any result is a failure.
    /// </summary>
    public static Result Combine(params Result[] results)
    {
        foreach (var result in results)
        {
            if (result.IsFailure)
            {
                return Result.Failure(result.Error!);
            }
        }
        return Result.Success();
    }

    /// <summary>
    /// Converts a nullable value to a Result{T}.
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, string error) where T : class
    {
        return value != null
            ? Result<T>.Success(value)
            : Result<T>.Failure(error);
    }

    /// <summary>
    /// Converts a nullable value to a Result{T}.
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, string error) where T : struct
    {
        return value != null
            ? Result<T>.Success(value.Value)
            : Result<T>.Failure(error);
    }
}
