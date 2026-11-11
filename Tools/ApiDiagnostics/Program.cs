using System.Text.Json;

if (JsonSerializer.IsReflectionEnabledByDefault)
    throw new InvalidOperationException("Run the checks without the JSON reflection fallback.");

if (args.Length == 2 && args[0] == "--component-benchmark")
{
    await ComponentBenchmarks.RunAsync(args[1]);
    return;
}

var benchmarkIndex = Array.IndexOf(args, "--cache-benchmark");
if (benchmarkIndex >= 0)
{
    if (args.Length < benchmarkIndex + 5)
        throw new ArgumentException("Use --cache-benchmark <HTTPS loopback URL> <API project directory> <output directory> <server PID> [server stdout log].");
    await ResponseCacheBenchmarks.RunAsync(args[benchmarkIndex + 1], args[benchmarkIndex + 2],
        args[benchmarkIndex + 3], int.Parse(args[benchmarkIndex + 4], System.Globalization.CultureInfo.InvariantCulture),
        args.Length > benchmarkIndex + 5 ? args[benchmarkIndex + 5] : null);
    return;
}

await AuthChecks.RunAsync();
await NdjsonChecks.RunAsync();
await DiagnosticsChecks.RunAsync();
await SignalRChecks.RunAsync();
await WindowPresenceChecks.RunAsync();
await ResponseCacheChecks.RunAsync();

var httpIndex = Array.IndexOf(args, "--http");
if (httpIndex >= 0)
{
    var pathIndex = Array.IndexOf(args, "--api-path");
    if (pathIndex < 0 || pathIndex + 1 >= args.Length || httpIndex + 1 >= args.Length)
        throw new ArgumentException("Use --http https://localhost:55128 --api-path <API project directory>.");
    await HttpChecks.RunAsync(args[httpIndex + 1], args[pathIndex + 1]);
}

Console.WriteLine("PASS API diagnostics completed; JSON reflection fallback disabled.");
