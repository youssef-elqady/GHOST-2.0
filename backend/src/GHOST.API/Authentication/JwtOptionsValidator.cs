using System.Text;
using Microsoft.Extensions.Options;

namespace GHOST.API.Authentication;

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add($"{JwtOptions.SectionName}:Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add($"{JwtOptions.SectionName}:Audience is required.");

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add($"{JwtOptions.SectionName}:SigningKey is required.");
        }
        else if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            failures.Add($"{JwtOptions.SectionName}:SigningKey must contain at least 32 UTF-8 bytes.");
        }

        if (options.AccessTokenLifetimeMinutes is < 1 or > 30)
        {
            failures.Add(
                $"{JwtOptions.SectionName}:AccessTokenLifetimeMinutes must be between 1 and 30.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
