namespace Mizon.Server.Logic.Operations.Musics;

internal static class MusicPlaylistWriteLock
{
    // Serialize only this client's mutations. Transaction disposal also releases
    // the lock on rollback/cancellation, without holding any process-wide state.
    public static Task AcquireAsync(MizonDatabaseContext context, int clientId, CancellationToken cancellationToken)
    {
        var resource = $"Mizon:MusicPlaylists:{clientId}";
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {resource}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'The playlist is being updated. Please retry.', 1;
            """, cancellationToken);
    }
}
