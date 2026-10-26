using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace Mizon.Server.API.Middlewares;

public sealed class ClientContextMiddleware
{
    private readonly RequestDelegate _next;
    public static ConcurrentDictionary<Guid, int> Clients = new();
    public ClientContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context,
        IApiOperationExecutor apiOperationExecutor,
        RequestMetadata requestMetadata)
    {

        var sid = context.User?.FindFirstValue("sid");
        var sub = context.User?.FindFirstValue("sub");

        requestMetadata.SessionPublicId = Guid.TryParse(sid, out Guid sessionPublicId) ? sessionPublicId : null;
        requestMetadata.ClientPublicId = Guid.TryParse(sub, out Guid clientPublicId) ? clientPublicId : null;

        if (requestMetadata.ClientPublicId is not null)
        {
            if (!Clients.ContainsKey(requestMetadata.ClientPublicId.Value))
            {
                var baseApiResponse = await apiOperationExecutor.ExecuteAsync<FindClientIdOperation, FindClientIdRequest, FindClientIdResponse>(new() { Content = new() { ClientPublicId = requestMetadata.ClientPublicId.Value } }, null, CancellationToken.None);

                if (baseApiResponse.IsSuccess && baseApiResponse.Content is not null)
                {
                    Clients.TryAdd(requestMetadata.ClientPublicId.Value, baseApiResponse.Content.ClientId);
                }
                else
                {

                }
            }

            if (Clients.TryGetValue(requestMetadata.ClientPublicId.Value, out int clientId))
                requestMetadata.ClientId = clientId;
        }

        if (context.Request.Headers.TryGetValue("Mizon-Device-Identifier", out StringValues deviceIdentifier))
            requestMetadata.DeviceIdentifier = deviceIdentifier;


        if (context.Request.Headers.TryGetValue("Mizon-Device-Model", out StringValues deviceModel))
            requestMetadata.DeviceModel = deviceModel;


        if (context.Request.Headers.TryGetValue("Mizon-Device-Name", out StringValues deviceName))
            requestMetadata.DeviceName = deviceName;


        if (context.Request.Headers.TryGetValue("Mizon-Device-Username", out StringValues deviceUsername))
            requestMetadata.DeviceUsername = deviceUsername;


        if (context.Request.Headers.TryGetValue("Mizon-Platform-Version", out StringValues platformVersion))
            if (Guid.TryParse(platformVersion, out Guid p))
                requestMetadata.PlatformVersion = p;


        if (context.Request.Headers.TryGetValue("Mizon-OS-Version", out StringValues osVersion))
            if (Version.TryParse(osVersion, out Version? v))
                requestMetadata.OsVersion = v;


        await _next(context);
    }
}
