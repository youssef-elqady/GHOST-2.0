
namespace GHOST.Application.Common.Result;

public class Result
{
    public bool IsSuccess { get; }

    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success()
        => new(true, null);

    public static Result Failure(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException(
                "Error message is required.",
                nameof(error));
        }

        return new(false, error);
    }
}