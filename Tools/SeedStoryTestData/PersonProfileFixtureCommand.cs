using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Server.Logic.Operations.Persons;

internal static class PersonProfileFixtureCommand
{
    internal static async Task RunAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--assets-directory");
        if (!args.Contains("--apply", StringComparer.Ordinal) || index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Pass --apply and --assets-directory pointing to App_Data/PersonProfileFixtures.");

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
                .UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True").Options;
            await using var database = new MizonDatabaseContext(options);
            var count = await new PersonProfileFixtureOperation(database).ExecuteAsync(args[index + 1], cancellation.Token);
            Console.WriteLine($"Inserted {count} independent artist profiles.");
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
