namespace GHOST.API.Configuration;

public sealed record DatabaseConnectionOptions
{
    public const string SectionName = "ConnectionStrings";

    public string? DefaultConnection { get; init; }
}
