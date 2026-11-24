using Microsoft.AspNetCore.Http;
using NLog;
using System.Net;
using Miningcore.Configuration;
using Miningcore.Extensions;

namespace Miningcore.Api.Middlewares;

public class IPAccessWhitelistMiddleware
{
    public IPAccessWhitelistMiddleware(RequestDelegate next, string[] locations, IPAddress[] whitelist, bool gpdrCompliantLogging)
    {
        this.whitelist = whitelist;
        this.next = next;
        this.locations = locations;
        this.gpdrCompliantLogging = gpdrCompliantLogging;
    }

    private readonly RequestDelegate next;
    private readonly ILogger logger = LogManager.GetCurrentClassLogger();
    private readonly IPAddress[] whitelist;
    private readonly string[] locations;
    private readonly bool gpdrCompliantLogging;

    public async Task Invoke(HttpContext context)
    {
        // ASP.NET Core attribute routing is case-insensitive, so "/API/admin/..." reaches the
        // controller even though a case-sensitive StartsWith here would not match it against
        // the "/api/admin" prefix below - letting a request bypass the whitelist entirely.
        if(locations.Any(x => context.Request.Path.Value.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
        {
            var remoteAddress = context.Connection.RemoteIpAddress;
            if(!whitelist.Any(x => x.Equals(remoteAddress)))
            {
                logger.Info(() => $"Unauthorized request attempt to {context.Request.Path.Value} from {remoteAddress.CensorOrReturn(gpdrCompliantLogging)}");

                context.Response.StatusCode = (int) HttpStatusCode.Forbidden;
                await context.Response.WriteAsync("You are not in my access list. Good Bye.\n");
                return;
            }
        }

        await next.Invoke(context);
    }
}
