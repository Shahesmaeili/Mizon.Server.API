using System.Diagnostics;

namespace Mizon.Server.API.Middlewares;

public sealed partial class RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
{
    public Task InvokeAsync(HttpContext context) =>
        logger.IsEnabled(LogLevel.Information) ? InvokeLoggedAsync(context) : next(context);

    private async Task InvokeLoggedAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var endpoint = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "/";
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            RequestAborted(logger, context.Request.Protocol, endpoint, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            RequestFailed(logger, exception, context.Request.Protocol, endpoint, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }

        RequestCompleted(logger, context.Request.Protocol, endpoint, context.Response.StatusCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Request {Protocol} {Endpoint} returned {StatusCode} in {ElapsedMilliseconds:F2} ms.")]
    private static partial void RequestCompleted(ILogger logger, string protocol, string endpoint, int statusCode, double elapsedMilliseconds);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
        Message = "Request {Protocol} {Endpoint} was aborted after {ElapsedMilliseconds:F2} ms.")]
    private static partial void RequestAborted(ILogger logger, string protocol, string endpoint, double elapsedMilliseconds);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "Request {Protocol} {Endpoint} failed after {ElapsedMilliseconds:F2} ms.")]
    private static partial void RequestFailed(ILogger logger, Exception exception, string protocol, string endpoint, double elapsedMilliseconds);
}
