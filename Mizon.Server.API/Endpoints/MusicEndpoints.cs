using Mizon.Server.API.Endpoints.Musics;

namespace Mizon.Server.API.Endpoints;

internal sealed class MusicEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        new MusicContributors().Map(app);
        new Music().Map(app);
        new MusicFileWaveform().Map(app);
        new MusicFile().Map(app);
        new MusicStemMix().Map(app);
        new MusicLyric().Map(app);
        new MusicLyricLine().Map(app);
        new MusicReaction().Map(app); 
        new MusicView().Map(app);
        new MusicReactions().Map(app);
        new MusicReactionTypes().Map(app);
        new MusicGenreTypes().Map(app);
        new MusicAlbumMusics().Map(app);
        new MusicContributorMusics().Map(app);
        new MusicComments().Map(app);
        new MusicCommentCreate().Map(app);
        new MusicPlaylists().Map(app);
        new MusicPlaylistMembership().Map(app);
        new MusicPlaylistCreate().Map(app);
    }
}
