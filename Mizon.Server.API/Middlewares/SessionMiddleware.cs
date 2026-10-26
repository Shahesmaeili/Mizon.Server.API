using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Database.Entities;

namespace Mizon.Server.API.Middlewares;

//public sealed class SessionMiddleware
//{
//    private readonly RequestDelegate _next;

//    public SessionMiddleware(RequestDelegate next)
//    {
//        _next = next;
//    }

//    public async Task InvokeAsync(HttpContext context, MizonDatabaseContext mizonDatabaseContext)
//    {
//        if (context.User.Identity?.IsAuthenticated != true)
//        {
//            await _next(context);
//            return;
//        }

//        var sessionPublicIdStr = context.User.FindFirst("sid")?.Value;

//        if (!Guid.TryParse(sessionPublicIdStr, out var sessionPublicId))
//        {
//            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
//            return;
//        }

//        var clientSession = await mizonDatabaseContext
//            .Set<ClientSession>()
//            .Where(c => c.PublicId == sessionPublicId)
//            .FirstOrDefaultAsync();

//        if (clientSession is null)
//        {
//            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
//            return;
//        }

//        var ctx = (ClientRequestContext?)context.Items["ClientRequestContext"];

//        if (ctx is null)
//        {
//            context.Response.StatusCode = StatusCodes.Status400BadRequest;
//            return;
//        }

//        // Device validation
//        if (!string.Equals(session.DeviceIdentifier, ctx.DeviceId, StringComparison.Ordinal))
//        {
//            context.Response.StatusCode = StatusCodes.Status403Forbidden;
//            return;
//        }

//        bool sessionUpdated = false;

//        // AppVersion update
//        if (!string.Equals(session.AppVersion, ctx.AppVersion, StringComparison.Ordinal))
//        {
//            session.AppVersion = ctx.AppVersion;
//            sessionUpdated = true;
//        }

//        // OsVersion update
//        if (!string.Equals(session.OsVersion, ctx.OsVersion, StringComparison.Ordinal))
//        {
//            session.OsVersion = ctx.OsVersion;
//            sessionUpdated = true;
//        }

//        // PlatformVersion update
//        if (session.PlatformVersionId != ctx.PlatformVersionId)
//        {
//            session.PlatformVersionId = ctx.PlatformVersionId;
//            sessionUpdated = true;
//        }


//        // IP handling
//        var ip = context.Connection.RemoteIpAddress?.ToString();

//        if (!string.IsNullOrWhiteSpace(ip))
//        {
//            bool exists = session.ClientSessionIps
//                .Any(x => x.IpAddress == ip);

//            if (!exists)
//            {
//                session.ClientSessionIps.Add(new ClientSessionIp
//                {
//                    IpAddress = ip,
//                    CreateDateTime = DateTimeOffset.UtcNow
//                });

//                sessionUpdated = true;
//            }
//        }

//        if (sessionUpdated)
//        {
//            await db.SaveChangesAsync();
//        }

//        context.Items["ClientSession"] = session;

//        await _next(context);
//    }
//}
