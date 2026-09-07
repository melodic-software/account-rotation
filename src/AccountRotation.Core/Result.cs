using System.Diagnostics.CodeAnalysis;

namespace AccountRotation.Core;

/// <summary>
/// An expected outcome: either a value or a typed error. Expected failures are
/// results, not exceptions; exceptions are reserved for defects and I/O faults.
/// </summary>
public readonly record struct Result<TValue, TError>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    private Result(bool isSuccess, TValue? value, TError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The result is a failure; read Error instead.");

    public TError Error => IsSuccess
        ? throw new InvalidOperationException("The result is a success; read Value instead.")
        : _error!;

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The factories are the only construction path; the type arguments are always explicit at the site.")]
    public static Result<TValue, TError> Success(TValue value) => new(true, value, default);

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The factories are the only construction path; the type arguments are always explicit at the site.")]
    public static Result<TValue, TError> Failure(TError error) => new(false, default, error);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(_error!);
    }
}

/// <summary>The value of a result that carries no payload.</summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
