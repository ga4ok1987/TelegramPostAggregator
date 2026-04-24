using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TelegramPostAggregator.Application;
using TelegramPostAggregator.Application.Options;
using TelegramPostAggregator.Infrastructure;
using TelegramPostAggregator.Monitoring.Web.Components;
using TelegramPostAggregator.Monitoring.Web.Configuration;
using TelegramPostAggregator.Monitoring.Web.ViewModels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.Configure<SimpleLoginOptions>(builder.Configuration.GetSection(SimpleLoginOptions.SectionName));
builder.Services.AddMonitoringApplication(builder.Configuration);
builder.Services.AddMonitoringInfrastructure();
builder.Services.AddScoped<DashboardViewModel>();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/auth/login", async (HttpContext httpContext) =>
    {
        var form = await httpContext.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();
        var returnUrl = form["returnUrl"].ToString();

        if (!string.Equals(username, loginOptions.Username, StringComparison.Ordinal) ||
            !string.Equals(password, loginOptions.Password, StringComparison.Ordinal))
        {
            return Results.Redirect("/login?error=1");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, loginOptions.Username),
            new(ClaimTypes.Email, $"{loginOptions.Username}@local.monitoring")
        };
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
    .AllowAnonymous();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
