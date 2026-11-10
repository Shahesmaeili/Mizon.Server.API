using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

if (args.Length < 4 || !Path.IsPathFullyQualified(args[0]) || !File.Exists(args[0])
    || !string.Equals(Path.GetFileName(args[0]), "Mizon.Windows.Mcp.exe", StringComparison.OrdinalIgnoreCase)
    || args[1] != "--client-profile" || !Guid.TryParse(args[2], out var profile) || profile == Guid.Empty)
{
    Console.Error.WriteLine("Usage: McpProbe <absolute installed Mizon.Windows.Mcp.exe> --client-profile <configured profile GUID> getconnection|getauth|openlogin|openwelcome [...]");
    return 2;
}

var calls = new List<CallToolRequestParams>();
foreach (var operation in args.Skip(3))
{
    CallToolRequestParams? call = operation switch
    {
        "getconnection" => new() { Name = "mizon_get_connection" },
        "getauth" => new() { Name = "mizon_get_auth_state" },
        "openlogin" => new() { Name = "mizon_open_auth_page", Arguments = PageArgument("login") },
        "openwelcome" => new() { Name = "mizon_open_auth_page", Arguments = PageArgument("welcome") },
        _ => null
    };
    if (call is null)
    {
        Console.Error.WriteLine("Unsupported operation. Only authentication state and the two declared pages are available.");
        return 2;
    }
    calls.Add(call);
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; timeout.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
    {
        Command = args[0],
        Arguments = ["--client-profile", profile.ToString("N")],
        Name = "Mizon runtime benchmark probe"
    }), new McpClientOptions
    {
        ClientInfo = new() { Name = "Mizon.RuntimeUpgradeBenchmarks", Title = "Mizon runtime benchmark probe", Version = "1.0.0" }
    }, cancellationToken: timeout.Token);

    foreach (var call in calls)
    {
        var result = await client.CallToolAsync(call, cancellationToken: timeout.Token);
        Console.WriteLine(call.Name);
        if (result.StructuredContent is not JsonElement content)
        {
            Console.Error.WriteLine("Mizon did not return the required structured result. No further operation was executed.");
            return 1;
        }

        Console.WriteLine(content.GetRawText());
        // An SDK transport success is not a business success. Fail closed for
        // pending/denied/unavailable or malformed results, even if IsError is false.
        if (result.IsError == true || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            return 1;
    }
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("MCP probe was cancelled or timed out. Check current app state before repeating a page change.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"MCP transport failed ({exception.GetType().Name}). No operation was automatically retried.");
    return 3;
}
finally
{
    Console.CancelKeyPress -= cancel;
}

static Dictionary<string, JsonElement> PageArgument(string page)
{
    using var document = JsonDocument.Parse(page == "login" ? "\"login\"" : "\"welcome\"");
    return new() { ["page"] = document.RootElement.Clone() };
}
