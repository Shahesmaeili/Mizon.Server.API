using System.Collections.Frozen;
using Mizon.Server.Logic.Operations.Medias;

namespace Mizon.Server.API.Caching;

public enum ResponseCacheDomain
{
    MusicContent, MusicCounters, MusicReactions, MusicComments, Playlists,
    Stories, Profiles, Following, Relationships, Taxonomies, Identity
}

public sealed record ResponseCacheRule(Type OperationType, TimeSpan Duration, bool Shared, ResponseCacheDomain[] Domains);

public static class ResponseCacheCatalog
{
    // Explicit opt-in: GET is not proof that an operation has no side effects,
    // and this API also has five read-only POST routes.
    private static readonly FrozenDictionary<string, ResponseCacheRule> Reads = new Dictionary<string, ResponseCacheRule>(StringComparer.OrdinalIgnoreCase)
    {
        ["GET /Clients/Me"] = User<MeOperation>(15, ResponseCacheDomain.Profiles),
        ["GET /Clients/ClientProfile"] = User<ClientProfileOperation>(30, ResponseCacheDomain.Profiles, ResponseCacheDomain.Relationships),
        ["GET /Clients/ClientMoodTypes"] = Shared<ClientMoodTypesOperation>(1800, ResponseCacheDomain.Taxonomies),
        ["POST /Clients/FollowingActivities"] = User<FollowingActivitiesOperation>(5, ResponseCacheDomain.Following, ResponseCacheDomain.MusicContent, ResponseCacheDomain.Profiles, ResponseCacheDomain.Relationships),
        ["GET /Musics/Music"] = User<MusicOperation>(10, ResponseCacheDomain.MusicContent, ResponseCacheDomain.MusicCounters),
        ["GET /Musics/MusicContributors"] = Shared<MusicContributorOperation>(300, ResponseCacheDomain.MusicContent, ResponseCacheDomain.Profiles),
        ["GET /Musics/MusicFile"] = Shared<MusicFileOperation>(60, ResponseCacheDomain.MusicContent),
        ["GET /Musics/MusicStemMix"] = Shared<MusicStemMixOperation>(300, ResponseCacheDomain.MusicContent),
        ["GET /Musics/MusicFileWaveform"] = Shared<MusicFileWaveformOperation>(3600, ResponseCacheDomain.MusicContent),
        ["GET /Musics/MusicLyric"] = Shared<MusicLyricOperation>(3600, ResponseCacheDomain.MusicContent),
        ["GET /Musics/MusicLyricLine"] = Shared<MusicLyricLineOperation>(3600, ResponseCacheDomain.MusicContent),
        ["GET /Musics/MusicReactions"] = User<MusicReactionsOperation>(5, ResponseCacheDomain.MusicReactions, ResponseCacheDomain.MusicContent),
        ["GET /Musics/MusicReactionTypes"] = Shared<MusicReactionTypesOperation>(3600, ResponseCacheDomain.Taxonomies),
        ["GET /Musics/MusicGenreTypes"] = Shared<MusicGenreTypesOperation>(300, ResponseCacheDomain.MusicContent, ResponseCacheDomain.Taxonomies),
        ["GET /Musics/MusicAlbumMusics"] = User<MusicAlbumMusicsOperation>(30, ResponseCacheDomain.MusicContent, ResponseCacheDomain.MusicCounters, ResponseCacheDomain.Profiles),
        ["GET /Musics/MusicContributorMusics"] = User<MusicContributorMusicsOperation>(30, ResponseCacheDomain.MusicContent, ResponseCacheDomain.MusicCounters, ResponseCacheDomain.Profiles),
        ["GET /Musics/MusicComments"] = User<MusicCommentsOperation>(10, ResponseCacheDomain.MusicComments, ResponseCacheDomain.Profiles, ResponseCacheDomain.MusicContent),
        ["POST /Musics/MusicPlaylists"] = User<MusicPlaylistsOperation>(10, ResponseCacheDomain.Playlists, ResponseCacheDomain.MusicContent),
        ["POST /Musics/MusicLibrary"] = User<MusicLibraryOperation>(10, ResponseCacheDomain.Playlists, ResponseCacheDomain.MusicContent),
        ["POST /Musics/MusicPlaylistDetails"] = User<MusicPlaylistDetailsOperation>(10, ResponseCacheDomain.Playlists, ResponseCacheDomain.MusicContent, ResponseCacheDomain.MusicCounters, ResponseCacheDomain.Profiles, ResponseCacheDomain.Relationships),
        ["POST /Musics/MusicFeatures"] = User<MusicFeaturesOperation>(10, ResponseCacheDomain.MusicContent, ResponseCacheDomain.MusicCounters, ResponseCacheDomain.Profiles),
        ["GET /Stories/Stories"] = User<StoriesOperation>(5, ResponseCacheDomain.Stories, ResponseCacheDomain.Profiles, ResponseCacheDomain.Relationships),
        ["GET /Stories/MusicStories"] = User<MusicStoriesOperation>(5, ResponseCacheDomain.Stories, ResponseCacheDomain.Profiles, ResponseCacheDomain.Relationships),
        ["GET /Stories/StoryDetails"] = User<StoryDetailsOperation>(3, ResponseCacheDomain.Stories, ResponseCacheDomain.Profiles, ResponseCacheDomain.Relationships, ResponseCacheDomain.MusicContent),
        ["GET /Medias/Media"] = Shared<MediaOperation>(60, ResponseCacheDomain.MusicContent),
        // /Search/Global stays streamed and uncached.
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<Type, ResponseCacheDomain[]> Writes = new Dictionary<Type, ResponseCacheDomain[]>
    {
        // New accounts/sessions have new identity keys. Public login attempts
        // must not be able to flush other users' existing personal responses.
        [typeof(LoginOperation)] = [],
        [typeof(RegisterOperation)] = [],
        [typeof(LogoutOperation)] = [ResponseCacheDomain.Identity],
        [typeof(ClientMoodOperation)] = [ResponseCacheDomain.Profiles],
        [typeof(MusicLikeOperation)] = [ResponseCacheDomain.MusicCounters, ResponseCacheDomain.Following],
        [typeof(MusicViewOperation)] = [ResponseCacheDomain.MusicCounters],
        [typeof(MusicPlayOperation)] = [ResponseCacheDomain.Following],
        [typeof(MusicReactionOperation)] = [ResponseCacheDomain.MusicReactions, ResponseCacheDomain.Following],
        [typeof(MusicCommentCreateOperation)] = [ResponseCacheDomain.MusicComments, ResponseCacheDomain.Following],
        [typeof(MusicPlaylistCreateOperation)] = [ResponseCacheDomain.Playlists, ResponseCacheDomain.Following],
        [typeof(MusicPlaylistMembershipOperation)] = [ResponseCacheDomain.Playlists, ResponseCacheDomain.Following],
        [typeof(StoryCreateOperation)] = [ResponseCacheDomain.Stories, ResponseCacheDomain.Following],
        [typeof(StoryViewOperation)] = [ResponseCacheDomain.Stories, ResponseCacheDomain.Following],
        [typeof(StoryLikeOperation)] = [ResponseCacheDomain.Stories],
    }.ToFrozenDictionary();

    public static ResponseCacheRule? GetRule(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint route
            ? Reads.GetValueOrDefault($"{context.Request.Method} {route.RoutePattern.RawText}") : null;

    public static ResponseCacheDomain[]? GetInvalidatedDomains(Type operation) => Writes.GetValueOrDefault(operation);

    private static ResponseCacheRule User<T>(int seconds, params ResponseCacheDomain[] domains) =>
        new(typeof(T), TimeSpan.FromSeconds(seconds), false, [.. domains, ResponseCacheDomain.Identity]);
    private static ResponseCacheRule Shared<T>(int seconds, params ResponseCacheDomain[] domains) =>
        new(typeof(T), TimeSpan.FromSeconds(seconds), true, domains);
}
