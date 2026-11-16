using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Server.Logic.Operations.Persons;

internal static class FeaturedPersonsCommand
{
    internal static async Task RunAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--model-directory");
        if (!args.Contains("--apply", StringComparer.Ordinal) || index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Pass --apply and --model-directory pointing to Assets/Discovery/Models.");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
                .UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True").Options;
            await using var database = new MizonDatabaseContext(options);
            var created = await new FeaturedPersonsInitializeOperation(database).ExecuteAsync(args[index + 1], cancellation.Token);
            Console.WriteLine($"Inserted {created} featured person entries.");
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
