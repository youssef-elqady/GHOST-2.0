namespace GHOST.Application.Identity.Bootstrap;

public sealed record AdminBootstrapRequest(
    string UserName,
    string FullName,
    string Password);
