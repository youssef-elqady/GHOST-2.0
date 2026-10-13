
namespace GHOST.Application.Common.Result;

public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, T? value, string? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public static Result<T> Success(T value)
        => new(true, value, null);

    public static new Result<T> Failure(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException(
                "Error message is required.",
                nameof(error));
        }

        return new(false, default, error);
    }
}