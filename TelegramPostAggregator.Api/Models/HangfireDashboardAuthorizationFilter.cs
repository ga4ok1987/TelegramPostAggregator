using System.Net;
using System.Text;
using Hangfire.Annotations;
using Hangfire.Dashboard;

namespace TelegramPostAggregator.Api.Models;

public sealed class HangfireDashboardAuthorizationFilter(
    bool allowLocalRequests,
    string? username,
    string? password) : IDashboardAuthorizationFilter
{
    public bool Authorize([NotNull] DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (allowLocalRequests && IsLocalRequest(httpContext))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Challenge(httpContext);
            return false;
        }

        if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            Challenge(httpContext);
            return false;
        }

        var headerValue = authorizationHeader.ToString();
        if (!headerValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            Challenge(httpContext);
            return false;
        }

        try
        {
            var encodedCredentials = headerValue["Basic ".Length..].Trim();
            var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var separatorIndex = decodedCredentials.IndexOf(':');
            if (separatorIndex <= 0)
            {
                Challenge(httpContext);
                return false;
            }

            var suppliedUsername = decodedCredentials[..separatorIndex];
            var suppliedPassword = decodedCredentials[(separatorIndex + 1)..];

            var isAuthorized = string.Equals(suppliedUsername, username, StringComparison.Ordinal)
                && string.Equals(suppliedPassword, password, StringComparison.Ordinal);

            if (!isAuthorized)
            {
                Challenge(httpContext);
            }

            return isAuthorized;
        }
        catch (FormatException)
        {
            Challenge(httpContext);
            return false;
        }
    }

    private static bool IsLocalRequest(HttpContext httpContext)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        var localIp = httpContext.Connection.LocalIpAddress;
        return localIp is not null && remoteIp.Equals(localIp);
    }

    private static void Challenge(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Hangfire Dashboard\"";
    }
}
