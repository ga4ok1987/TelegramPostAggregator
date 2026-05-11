using System.Security.Claims;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TelegramPostAggregator.Application;
using TelegramPostAggregator.Application.Abstractions.Services;
using TelegramPostAggregator.Application.DTOs;
using TelegramPostAggregator.Application.Options;
using TelegramPostAggregator.Infrastructure;
using TelegramPostAggregator.Infrastructure.Persistence;
using TelegramPostAggregator.Monitoring.Web.Admin;
using TelegramPostAggregator.Monitoring.Web.Components;
using TelegramPostAggregator.Monitoring.Web.Configuration;
using TelegramPostAggregator.Monitoring.Web.Services;
using TelegramPostAggregator.Monitoring.Web.ViewModels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminPolicies.ManageClients, policy =>
        policy.RequireClaim(AdminClaimTypes.Permission, AdminPermissions.ManageClients));

    options.AddPolicy(AdminPolicies.ManageAdminUsers, policy =>
        policy.RequireClaim(AdminClaimTypes.Permission, AdminPermissions.ManageAdminUsers));
});
builder.Services.AddDataProtection()
    .SetApplicationName("ChannelsMonitor.Monitoring.Web")
    .PersistKeysToFileSystem(new DirectoryInfo("/var/lib/telegram-post-aggregator/monitoring-web/dataprotection-keys"));
builder.Services.Configure<SimpleLoginOptions>(builder.Configuration.GetSection(SimpleLoginOptions.SectionName));
builder.Services.AddMonitoringApplication(builder.Configuration);
builder.Services.AddMonitoringInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IServerMetricsProvider, ServerMetricsProvider>();
builder.Services.AddScoped<DashboardViewModel>();
builder.Services.AddScoped<MiniAppViewModel>();

var loginOptions = builder.Configuration.GetSection(SimpleLoginOptions.SectionName).Get<SimpleLoginOptions>()
    ?? new SimpleLoginOptions();

builder.Services
    .AddAuthentication(authentication =>
    {
        authentication.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AggregatorDbContext>();
    await dbContext.Database.MigrateAsync();

    var adminAuthService = scope.ServiceProvider.GetRequiredService<IAdminAuthService>();
    await adminAuthService.EnsureBootstrapAdminAsync(loginOptions.Username, loginOptions.Password);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/auth/login", async (HttpContext httpContext, IAdminAuthService adminAuthService) =>
    {
        var form = await httpContext.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();
        var authenticatedUser = await adminAuthService.AuthenticateAsync(username, password, httpContext.RequestAborted);
        if (authenticatedUser is null)
        {
            return Results.Redirect("/login?error=1");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, authenticatedUser.Username),
            new(ClaimTypes.GivenName, authenticatedUser.DisplayName),
            new(ClaimTypes.Email, $"{authenticatedUser.Username}@local.monitoring"),
            new(AdminClaimTypes.AdminUserId, authenticatedUser.AdminUserId.ToString())
        };

        if (authenticatedUser.CanManageClients)
        {
            claims.Add(new Claim(AdminClaimTypes.Permission, AdminPermissions.ManageClients));
        }

        if (authenticatedUser.CanManageAdminUsers)
        {
            claims.Add(new Claim(AdminClaimTypes.Permission, AdminPermissions.ManageAdminUsers));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
            });

        return Results.Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
    })
    .AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    })
    .AllowAnonymous()
    .DisableAntiforgery();

var adminPagePath = Path.Combine(app.Environment.WebRootPath, "admin", "clients.html");
var controlCenterPagePath = Path.Combine(app.Environment.WebRootPath, "admin", "control-center.html");

app.MapGet("/clients", [Authorize(Policy = AdminPolicies.ManageClients)] () => Results.Redirect("/admin/clients"));
app.MapGet("/admin/clients", [Authorize(Policy = AdminPolicies.ManageClients)] () => Results.File(adminPagePath, "text/html; charset=utf-8"));
app.MapGet("/admin/control-center", [Authorize(Policy = AdminPolicies.ManageAdminUsers)] () => Results.File(controlCenterPagePath, "text/html; charset=utf-8"));

var adminApi = app.MapGroup("/api/admin")
    .RequireAuthorization()
    .DisableAntiforgery();

adminApi.MapGet("/session", (ClaimsPrincipal user) =>
{
    var adminUserId = user.FindFirstValue(AdminClaimTypes.AdminUserId);
    return Results.Ok(new
    {
        adminUserId,
        username = user.Identity?.Name ?? string.Empty,
        displayName = user.FindFirstValue(ClaimTypes.GivenName) ?? user.Identity?.Name ?? string.Empty,
        canManageClients = user.HasClaim(AdminClaimTypes.Permission, AdminPermissions.ManageClients),
        canManageAdminUsers = user.HasClaim(AdminClaimTypes.Permission, AdminPermissions.ManageAdminUsers)
    });
});

var clientAdminApi = adminApi.MapGroup(string.Empty)
    .RequireAuthorization(new AuthorizeAttribute { Policy = AdminPolicies.ManageClients });

clientAdminApi.MapGet("/clients", async (
        IClientAdminService service,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
    {
        var normalizedPage = Math.Max(page ?? 1, 1);
        var normalizedPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
        var clients = await service.ListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(search))
        {
            clients = clients
                .Where(client =>
                    client.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    client.TelegramUsername.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    client.TelegramUserId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var totalCount = clients.Count;
        var items = clients
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToArray();

        return Results.Ok(new AdminPagedResultDto<AdminClientDto>(items, normalizedPage, normalizedPageSize, totalCount));
    });

clientAdminApi.MapGet("/clients/{userId:guid}", async (Guid userId, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var detail = await service.GetAsync(userId, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

clientAdminApi.MapPatch("/clients/{userId:guid}", async (Guid userId, AdminClientUpdateRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.SetBlockedAsync(userId, request.IsBlockedBot, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapPatch("/clients/{userId:guid}/subscription-allowance", async (Guid userId, AdminClientSubscriptionAllowanceRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.SetExtraSubscriptionSlotsAsync(userId, request.ExtraSubscriptionSlots, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapPatch("/clients/{userId:guid}/managed-channel-allowance", async (Guid userId, AdminClientManagedChannelAllowanceRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.SetExtraManagedChannelSlotsAsync(userId, request.ExtraManagedChannelSlots, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapGet("/clients/{userId:guid}/bot-subscriptions", async (Guid userId, int? page, int? pageSize, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var result = await service.GetBotSubscriptionsPageAsync(userId, page ?? 1, Math.Clamp(pageSize ?? 10, 1, 100), cancellationToken);
    return Results.Ok(result);
});

clientAdminApi.MapPost("/clients/{userId:guid}/bot-subscriptions", async (Guid userId, AdminCreateSubscriptionRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var created = await service.CreateBotSubscriptionAsync(userId, request.ChannelReference, cancellationToken);
    return created ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapPatch("/bot-subscriptions/{subscriptionId:guid}", async (Guid subscriptionId, AdminSetActiveRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.SetBotSubscriptionActiveAsync(subscriptionId, request.IsActive, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapDelete("/bot-subscriptions/{subscriptionId:guid}", async (Guid subscriptionId, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var deleted = await service.DeleteBotSubscriptionAsync(subscriptionId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapPatch("/managed-channels/{managedChannelId:guid}", async (Guid managedChannelId, AdminSetActiveRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.SetManagedChannelActiveAsync(managedChannelId, request.IsActive, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapDelete("/managed-channels/{managedChannelId:guid}", async (Guid managedChannelId, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var deleted = await service.DeleteManagedChannelAsync(managedChannelId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapGet("/managed-channels/{managedChannelId:guid}/subscriptions", async (Guid managedChannelId, int? page, int? pageSize, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var result = await service.GetManagedChannelSubscriptionsPageAsync(managedChannelId, page ?? 1, Math.Clamp(pageSize ?? 10, 1, 100), cancellationToken);
    return Results.Ok(result);
});

clientAdminApi.MapPost("/managed-channels/{managedChannelId:guid}/subscriptions", async (Guid managedChannelId, AdminCreateSubscriptionRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var created = await service.CreateManagedChannelSubscriptionAsync(managedChannelId, request.ChannelReference, cancellationToken);
    return created ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapPatch("/managed-channel-subscriptions/{subscriptionId:guid}", async (Guid subscriptionId, AdminSetActiveRequest request, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.SetManagedChannelSubscriptionActiveAsync(subscriptionId, request.IsActive, cancellationToken);
    return updated ? Results.NoContent() : Results.NotFound();
});

clientAdminApi.MapDelete("/managed-channel-subscriptions/{subscriptionId:guid}", async (Guid subscriptionId, IClientAdminService service, CancellationToken cancellationToken) =>
{
    var deleted = await service.DeleteManagedChannelSubscriptionAsync(subscriptionId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

var controlCenterApi = adminApi.MapGroup("/admin-users")
    .RequireAuthorization(new AuthorizeAttribute { Policy = AdminPolicies.ManageAdminUsers });

var billingAdminApi = adminApi.MapGroup("/billing")
    .RequireAuthorization(new AuthorizeAttribute { Policy = AdminPolicies.ManageAdminUsers });

billingAdminApi.MapGet("/settings", async (IBillingAdminService service, CancellationToken cancellationToken) =>
{
    var settings = await service.GetSettingsAsync(cancellationToken);
    return Results.Ok(settings);
});

billingAdminApi.MapPatch("/plans/{planId:guid}", async (Guid planId, AdminPlanUpdateRequest request, IBillingAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.UpdatePlanAsync(
        planId,
        request.DisplayName,
        request.ChannelLimit,
        request.ManagedChannelLimit,
        request.PriceStars,
        request.DurationDays,
        request.IsEnabled,
        request.SortOrder,
        cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

billingAdminApi.MapPatch("/donations/{donationId:guid}", async (Guid donationId, AdminDonationUpdateRequest request, IBillingAdminService service, CancellationToken cancellationToken) =>
{
    var updated = await service.UpdateDonationAsync(
        donationId,
        request.DisplayName,
        request.StarsAmount,
        request.IsEnabled,
        request.SortOrder,
        cancellationToken);

    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

controlCenterApi.MapGet(string.Empty, async (IAdminUserService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var currentAdminUserId = GetCurrentAdminUserId(user);
    var users = await service.ListAsync(currentAdminUserId, cancellationToken);
    return Results.Ok(users);
});

controlCenterApi.MapGet("/{adminUserId:guid}", async (Guid adminUserId, IAdminUserService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var currentAdminUserId = GetCurrentAdminUserId(user);
    var detail = await service.GetAsync(adminUserId, currentAdminUserId, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

controlCenterApi.MapPost(string.Empty, async (AdminUserCreateRequest request, IAdminUserService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var currentAdminUserId = GetCurrentAdminUserId(user);
    var result = await service.CreateAsync(
        new AdminUserCreateDto(request.Username, request.DisplayName, request.Password, request.IsActive, request.CanManageClients, request.CanManageAdminUsers),
        currentAdminUserId,
        cancellationToken);

    return result.Success
        ? Results.Ok(result.User)
        : Results.BadRequest(new { error = result.ErrorMessage });
});

controlCenterApi.MapPatch("/{adminUserId:guid}", async (Guid adminUserId, AdminUserUpdateRequest request, IAdminUserService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var currentAdminUserId = GetCurrentAdminUserId(user);
    var result = await service.UpdateAsync(
        adminUserId,
        new AdminUserUpdateDto(request.Username, request.DisplayName, request.IsActive, request.CanManageClients, request.CanManageAdminUsers),
        currentAdminUserId,
        cancellationToken);

    return result.Success
        ? Results.Ok(result.User)
        : Results.BadRequest(new { error = result.ErrorMessage });
});

controlCenterApi.MapPatch("/{adminUserId:guid}/password", async (Guid adminUserId, AdminUserPasswordRequest request, IAdminUserService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var currentAdminUserId = GetCurrentAdminUserId(user);
    var result = await service.SetPasswordAsync(adminUserId, request.Password, currentAdminUserId, cancellationToken);
    return result.Success
        ? Results.NoContent()
        : Results.BadRequest(new { error = result.ErrorMessage });
});

controlCenterApi.MapDelete("/{adminUserId:guid}", async (Guid adminUserId, IAdminUserService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var currentAdminUserId = GetCurrentAdminUserId(user);
    var result = await service.DeleteAsync(adminUserId, currentAdminUserId, cancellationToken);
    return result.Success
        ? Results.NoContent()
        : Results.BadRequest(new { error = result.ErrorMessage });
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static Guid GetCurrentAdminUserId(ClaimsPrincipal user)
{
    var rawValue = user.FindFirstValue(AdminClaimTypes.AdminUserId);
    return Guid.TryParse(rawValue, out var adminUserId)
        ? adminUserId
        : throw new InvalidOperationException("Authenticated admin user id claim is missing.");
}
