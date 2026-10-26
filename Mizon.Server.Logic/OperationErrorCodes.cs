namespace Mizon.Server.Logic;

internal static class OperationErrorCodes
{
    internal static class Clients
    {
        public const int ClientSessionNotFound = 20000;
    }


    internal static class Musics
    {
        public const int MusicNotFound = 10000;
        public const int MusicNotActive = 10001;
        public const int MusicFileNotFound = 10002;
        public const int MusicLyricNotFound = 10003;
        public const int MusicReactionTypeNotFound = 10004;
        public const int MusicPlaylistNotFound = 10005;
        public const int InvalidMusicPlaylistTitle = 10006;

    }

    internal static class Medias
    {
        public const int MediaNotFound = 30000;

    }

}
