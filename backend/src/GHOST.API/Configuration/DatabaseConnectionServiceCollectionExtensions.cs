using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GHOST.API.Configuration;

public static class DatabaseConnectionServiceCollectionExtensions
{
    public static IServiceCollection AddGhostDatabaseConnection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<
            IValidateOptions<DatabaseConnectionOptions>,
            DatabaseConnectionOptionsValidator>();

        services
            .AddOptions<DatabaseConnectionOptions>()
            .Bind(configuration.GetSection(DatabaseConnectionOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}
