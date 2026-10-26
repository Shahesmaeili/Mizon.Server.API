using Mizon.Server.Logic.Responses.Medias;
using Mizon.Server.API.Hubs;
using System.Text.Json.Serialization;

namespace Mizon.Server.API.JsonSerializerContexts;


[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(GlobalSearchItemResponse))]
[JsonSerializable(typeof(MusicListenerPresenceResponse))]
[JsonSerializable(typeof(List<MusicListenerPresenceResponse>))]
[JsonSerializable(typeof(BaseApiRequest<LoginRequest>))]
[JsonSerializable(typeof(BaseApiResponse<LoginResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MeRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MeResponse>))]

[JsonSerializable(typeof(BaseApiRequest<LogoutRequest>))]
[JsonSerializable(typeof(BaseApiResponse<LogoutResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicResponse>))]


[JsonSerializable(typeof(BaseApiRequest<MusicFileWaveformRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicFileWaveformResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicFileRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicFileResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicStemMixRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicStemMixResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicLyricRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicLyricResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicLyricLineRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicLyricLineResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicReactionRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicReactionResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MusicPlaylistsRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicPlaylistsResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MusicPlaylistMembershipRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicPlaylistMembershipResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MusicPlaylistCreateRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicPlaylistCreateResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MusicViewRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicViewResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicReactionsRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicReactionsResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MusicCommentsRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicCommentsResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MusicCommentCreateRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicCommentCreateResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicReactionTypesRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicReactionTypesResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicGenreTypesRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicGenreTypesResponse>))]

[JsonSerializable(typeof(BaseApiRequest<MusicAlbumMusicsRequest>))]
[JsonSerializable(typeof(BaseApiResponse<MusicTabMusicsResponse>))]
[JsonSerializable(typeof(BaseApiRequest<MusicContributorMusicsRequest>))]

[JsonSerializable(typeof(BaseApiResponse<MusicContributorsResponse>))]

[JsonSerializable(typeof(BaseApiResponse<MediaResponse>))]

[JsonSerializable(typeof(BaseApiRequest<StoryRequest>))]
[JsonSerializable(typeof(BaseApiRequest<MusicStoriesRequest>))]
[JsonSerializable(typeof(BaseApiResponse<StoryResponse>))]
[JsonSerializable(typeof(BaseApiRequest<StoryLikeRequest>))]
[JsonSerializable(typeof(BaseApiRequest<StoryCreateRequest>))]
[JsonSerializable(typeof(BaseApiResponse<StoriesResponse>))]


internal partial class MizonJsonContexts : JsonSerializerContext
{
}
