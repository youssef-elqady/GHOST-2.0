namespace GHOST.API.Security;

public static class AuthorizationPolicies
{
    /// <summary>Admin with a normal access token. Same rule as the fallback policy.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Admin or Staff with a normal access token (operational endpoints).</summary>
    public const string StaffOrAdmin = "StaffOrAdmin";

    public const string AdminRole = "Admin";
    public const string StaffRole = "Staff";
}
