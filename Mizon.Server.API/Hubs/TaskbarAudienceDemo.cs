using Microsoft.AspNetCore.SignalR;

namespace Mizon.Server.API.Hubs;

public sealed class TaskbarAudienceDemo(IHubContext<MizonHub> hub, ILogger<TaskbarAudienceDemo> logger) : BackgroundService
{
    internal sealed record Sample(OnlineClientCount Online, int Followers);

    private Sample _current = new(new(Random.Shared.Next(18, 66), 1), Random.Shared.Next(1, 13));

    internal Sample Current => Volatile.Read(ref _current);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Random.Shared.Next(2400, 6001), stoppingToken);
            var previous = Current;
            var online = new OnlineClientCount(NextDifferent(18, 65, previous.Online.Count), previous.Online.Revision + 1);
            var sample = new Sample(online, NextDifferent(1, 12, previous.Followers));
            Volatile.Write(ref _current, sample);

            try
            {
                await hub.Clients.All.SendAsync(MizonHub.OnlineClientCountChangedEvent, online, stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Could not broadcast the development audience count.");
            }
        }
    }

    private static int NextDifferent(int minimum, int maximum, int previous)
    {
        var next = Random.Shared.Next(minimum, maximum);
        return next >= previous ? next + 1 : next;
    }
}
