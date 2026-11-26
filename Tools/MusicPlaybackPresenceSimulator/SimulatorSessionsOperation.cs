using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Database.Entities;

/// <summary>Owns the tool's local SQL sessions for accounts seeded by SeedStoryTestData.</summary>
internal sealed class SimulatorSessionsOperation(MizonDatabaseContext database)
{
    private const string DeviceIdentifier = "mizon-development-music-listener";
    private static readonly string[] FixtureUsernames = Enumerable.Range(1, 10)
        .Select(number => $"story_test_{number:00}").ToArray();

    internal async Task<SimulatorSession[]> OpenAsync(int count, CancellationToken cancellationToken)
    {
        var fixtures = await database.ClientUsernames.AsNoTracking()
            .Where(name => name.IsActive && FixtureUsernames.Contains(name.Username))
            .Where(name => database.Stories.Any(story => story.ClientId == name.ClientId &&
                story.Title == "Story test #" + name.Username.Substring(11, 2)))
            .Where(name => !database.ClientPasswords.Any(password => password.ClientId == name.ClientId && password.IsActive))
            .Where(name => !database.ClientEmails.Any(email => email.ClientId == name.ClientId && email.IsActive))
            .Where(name => !database.ClientSessions.Any(session => session.ClientId == name.ClientId &&
                session.DeviceIdentifier != DeviceIdentifier))
            .Where(name => name.Client!.ClientSessionInactivityDays == null || name.Client!.ClientSessionInactivityDays == 0)
            .OrderBy(name => name.Username)
            .Select(name => new { name.ClientId, name.Client!.PublicId })
            .Take(count)
            .ToArrayAsync(cancellationToken);
        if (fixtures.Length == 0)
            throw new InvalidOperationException("No eligible story test fixture accounts exist in the local database.");

        var platform = await database.PlatformVersions.AsNoTracking()
            .Where(version => version.Platform!.Title == "Windows")
            .OrderByDescending(version => version.Id)
            .Select(version => (short?)version.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No Windows platform version exists for development sessions.");

        var fixtureIds = fixtures.Select(fixture => fixture.ClientId).ToArray();
        var active = await database.ClientSessions.AsNoTracking()
            .Where(session => fixtureIds.Contains(session.ClientId) && session.DeviceIdentifier == DeviceIdentifier &&
                session.LogoutDateTime == null)
            .Select(session => new { session.ClientId, session.PublicId })
            .ToDictionaryAsync(session => session.ClientId, session => session.PublicId, cancellationToken);

        var result = new SimulatorSession[fixtures.Length];
        for (var index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            if (!active.TryGetValue(fixture.ClientId, out var sessionId))
            {
                sessionId = Guid.CreateVersion7();
                database.ClientSessions.Add(new ClientSession
                {
                    ClientId = fixture.ClientId, PublicId = sessionId, PlatformVersionId = platform,
                    DeviceIdentifier = DeviceIdentifier, DeviceModel = "Playback simulator",
                    DeviceName = "Mizon development listener", DeviceUsername = "fixture",
                    OsVersion = Environment.OSVersion.Version.ToString(), LastSeenDateTime = DateTimeOffset.UtcNow,
                    // No refresh token is issued to these fixtures.
                    RefreshTokenHash = Convert.ToBase64String(SHA256.HashData(RandomNumberGenerator.GetBytes(64))),
                });
            }
            result[index] = new SimulatorSession(fixture.PublicId, sessionId);
        }
        await database.SaveChangesAsync(cancellationToken);
        return result;
    }

    internal async Task CloseAsync(SimulatorSession[] sessions, CancellationToken cancellationToken)
    {
        var sessionIds = sessions.Select(session => session.SessionPublicId).ToArray();
        var now = DateTimeOffset.UtcNow;
        await database.ClientSessions
            .Where(session => sessionIds.Contains(session.PublicId) && session.DeviceIdentifier == DeviceIdentifier &&
                session.LogoutDateTime == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.LogoutDateTime, now)
                .SetProperty(session => session.UpdateDateTime, now), cancellationToken);
    }
}

internal sealed record SimulatorSession(Guid ClientPublicId, Guid SessionPublicId);
