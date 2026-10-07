namespace GHOST.Domain.Exceptions;

public sealed class BusinessRuleValidationException : Exception
{
    public BusinessRuleValidationException(string message)
        : base(message)
    {
    }

    public BusinessRuleValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}