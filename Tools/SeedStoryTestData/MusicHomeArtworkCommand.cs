using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Server.Logic.Operations.Musics;

internal static class MusicHomeArtworkCommand
{
    public static async Task RunAsync(string[] args)
    {
        var directoryIndex = Array.IndexOf(args, "--artwork-directory");
        if (directoryIndex < 0 || directoryIndex + 1 >= args.Length)
            throw new ArgumentException("Pass --artwork-directory with the canonical server App_Data/MusicHomeArtwork directory.");
        var directory = Path.GetFullPath(args[directoryIndex + 1]);
        var apply = args.Contains("--apply", StringComparer.Ordinal);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            // Same local integrated-security database as the API; credentials are never accepted or logged.
            var options = new DbContextOptionsBuilder<MizonDatabaseContext>()
                .UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True").Options;
            await using var database = new MizonDatabaseContext(options);
            var operation = new MusicHomeArtworkInitializeOperation(database);
            var plan = await operation.GetPlanAsync(directory, cancellation.Token);
            Console.WriteLine($"Artwork plan: {plan.Items.Count(x => !x.IsFeature)} stories, {plan.Items.Count(x => x.IsFeature)} featured slides, {plan.Files.Count} dedicated media files.");
            foreach (var item in plan.Items)
                Console.WriteLine($"{(item.IsFeature ? "FEATURE" : "STORY")} {item.PublicId} | previous content={item.PreviousContentMediaId}, preview={item.PreviousPreviewMediaId} | {item.Content.Name} | {item.Preview.Name}");
            if (!apply)
            {
                Console.WriteLine("Read-only plan. Pass --apply to replace only these known development fixtures.");
                return;
            }
            var result = await operation.ExecuteAsync(directory, cancellation.Token);
            Console.WriteLine($"Applied: stories={result.StoriesChanged}, features={result.FeaturesChanged}, new media={result.MediaInserted}.");
            await operation.VerifyAsync(directory, cancellation.Token);
            var verified = await operation.GetPlanAsync(directory, cancellation.Token);
            Console.WriteLine($"Readback: {verified.Items.Count} fixtures resolve to the licensed artwork catalog.");
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
