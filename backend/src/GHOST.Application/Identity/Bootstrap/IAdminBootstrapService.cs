namespace GHOST.Application.Identity.Bootstrap;

public interface IAdminBootstrapService
{
    Task<AdminBootstrapResult> BootstrapAsync(
        AdminBootstrapRequest request,
        CancellationToken cancellationToken = default);
}
