namespace TelegramPostAggregator.Monitoring.Web.Admin;

public static class AdminClaimTypes
{
    public const string AdminUserId = "admin_user_id";
    public const string Permission = "admin_permission";
}

public static class AdminPermissions
{
    public const string ManageClients = "manage_clients";
    public const string ManageAdminUsers = "manage_admin_users";
}

public static class AdminPolicies
{
    public const string ManageClients = "ManageClients";
    public const string ManageAdminUsers = "ManageAdminUsers";
}
