namespace GameOfLife.Client;

/// <summary>
/// A dependency-free outcome monad: either a success carrying a <typeparamref name="T"/> or a
/// failure carrying a <typeparamref name="TError"/>. Used across the seam so callers handle the
/// meaningful failure cases as values (via <see cref="Match{R}"/>) rather than catching exceptions.
/// </summary>
public readonly struct Result<T, TError>
{
    private readonly T _value;
    private readonly TError _error;

    private Result(bool isSuccess, T value, TError error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>True when this is a success.</summary>
    public bool IsSuccess { get; }

    /// <summary>True when this is a failure.</summary>
    public bool IsError => !IsSuccess;

    /// <summary>Wraps a success value.</summary>
    public static Result<T, TError> Ok(T value) => new(true, value, default!);

    /// <summary>Wraps a failure value.</summary>
    public static Result<T, TError> Err(TError error) => new(false, default!, error);

    /// <summary>The success value. Throws if this is a failure — guard with <see cref="IsSuccess"/>.</summary>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Result is a failure; no value is present.");

    /// <summary>The failure value. Throws if this is a success — guard with <see cref="IsError"/>.</summary>
    public TError Error => IsSuccess
        ? throw new InvalidOperationException("Result is a success; no error is present.")
        : _error;

    /// <summary>Folds both cases into a single value.</summary>
    public R Match<R>(Func<T, R> ok, Func<TError, R> err) => IsSuccess ? ok(_value) : err(_error);

    /// <summary>Runs the matching side effect for whichever case this is.</summary>
    public void Match(Action<T> ok, Action<TError> err)
    {
        if (IsSuccess) ok(_value);
        else err(_error);
    }

    /// <summary>Transforms the success value, leaving a failure untouched.</summary>
    public Result<R, TError> Map<R>(Func<T, R> map) =>
        IsSuccess ? Result<R, TError>.Ok(map(_value)) : Result<R, TError>.Err(_error);

    /// <summary>Chains another <see cref="Result{R, TError}"/>-producing step, short-circuiting on failure.</summary>
    public Result<R, TError> Bind<R>(Func<T, Result<R, TError>> bind) =>
        IsSuccess ? bind(_value) : Result<R, TError>.Err(_error);
}
