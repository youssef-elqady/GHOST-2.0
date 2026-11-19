using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GHOST.API.Authentication;

public static class JwtAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddGhostJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
            {
                var jwtOptions = jwtOptionsAccessor.Value;

                bearerOptions.MapInboundClaims = false;
                bearerOptions.SaveToken = false;
                bearerOptions.IncludeErrorDetails = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey!)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = GhostClaimTypes.Name,
                    RoleClaimType = GhostClaimTypes.Role
                };
            });

        services.AddAuthorization(options =>
        {
            // Temporary-password tokens may access only the dedicated password-change flow.
            var normalUserPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    !context.User.HasClaim(GhostClaimTypes.MustChangePassword, "true"))
                .RequireClaim(GhostClaimTypes.BusinessAccess, "true")
                .Build();

            options.DefaultPolicy = normalUserPolicy;
            options.FallbackPolicy = normalUserPolicy;
            options.AddPolicy("PasswordChangeOnly", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(GhostClaimTypes.MustChangePassword, "true"));
        });

        return services;
    }
}
