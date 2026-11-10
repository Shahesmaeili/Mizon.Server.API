using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Mizon.Server.API.Middlewares;

internal static class DiagnosticsChecks
{
    public static async Task RunAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/not-an-endpoint";
        var logger = new RecordingLogger();
        var middleware = new RequestDiagnosticsMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, logger);
        await middleware.InvokeAsync(context);
        Require(context.Response.StatusCode == 404 && logger.Events.SequenceEqual([1000]), "unmatched route");

        logger.Enabled = false;
        var pending = new TaskCompletionSource();
        middleware = new RequestDiagnosticsMiddleware(_ => pending.Task, logger);
        Require(ReferenceEquals(middleware.InvokeAsync(context), pending.Task), "disabled logging fast path");
        pending.SetResult();

        logger.Enabled = true;
        var error = new InvalidOperationException("fixture");
        middleware = new RequestDiagnosticsMiddleware(_ => Task.FromException(error), logger);
        try { await middleware.InvokeAsync(context); throw new Exception("Failure was swallowed."); }
        catch (InvalidOperationException actual) when (ReferenceEquals(actual, error)) { }
        Require(logger.Events[^1] == 1002, "failure logged without swallowing");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        context.RequestAborted = cancellation.Token;
        middleware = new RequestDiagnosticsMiddleware(_ => Task.FromCanceled(cancellation.Token), logger);
        try { await middleware.InvokeAsync(context); throw new Exception("Cancellation was swallowed."); }
        catch (OperationCanceledException) { }
        Require(logger.Events[^1] == 1001, "request cancellation remains cancellation");
        Console.WriteLine("PASS request diagnostics: unknown route, no-log fast path, failure/cancellation propagation.");
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException("Request diagnostics: " + check);
    }

    private sealed class RecordingLogger : ILogger<RequestDiagnosticsMiddleware>
    {
        public bool Enabled { get; set; } = true;
        public List<int> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add(eventId.Id);
    }
}
