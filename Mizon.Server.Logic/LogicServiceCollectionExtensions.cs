using Microsoft.Extensions.DependencyInjection;
using Mizon.Server.Logic.Operations.Medias;
using Mizon.Server.Logic.Requests.Medias;
using Mizon.Server.Logic.Responses.Medias;
using System.Diagnostics.CodeAnalysis;

namespace Mizon.Server.Logic;

public static class LogicServiceCollectionExtensions
{
    public static IServiceCollection AddMizonLogic(this IServiceCollection services)
    {
        services.AddApiOperation<LoginOperation, LoginRequest, LoginResponse>();
        services.AddApiOperation<MeOperation, MeRequest, MeResponse>();
        services.AddApiOperation<LogoutOperation, LogoutRequest, LogoutResponse>();
        services.AddApiOperation<MusicOperation, MusicRequest, MusicResponse>();
        services.AddApiOperation<FindClientIdOperation, FindClientIdRequest, FindClientIdResponse>();
        services.AddApiOperation<MusicFileWaveformOperation, MusicFileWaveformRequest, MusicFileWaveformResponse>();
        services.AddApiOperation<MusicFileOperation, MusicFileRequest, MusicFileResponse>();
        services.AddApiOperation<MusicStemMixOperation, MusicStemMixRequest, MusicStemMixResponse>();
        services.AddApiOperation<MusicLyricOperation, MusicLyricRequest, MusicLyricResponse>();
        services.AddApiOperation<MusicLyricLineOperation, MusicLyricLineRequest, MusicLyricLineResponse>();
        services.AddApiOperation<MusicReactionOperation, MusicReactionRequest, MusicReactionResponse>();
        services.AddApiOperation<MusicViewOperation, MusicViewRequest, MusicViewResponse>();
        services.AddApiOperation<MusicReactionsOperation, MusicReactionsRequest, MusicReactionsResponse>();
        services.AddApiOperation<MusicReactionTypesOperation, MusicReactionTypesRequest, MusicReactionTypesResponse>();
        services.AddApiOperation<MusicGenreTypesOperation, MusicGenreTypesRequest, MusicGenreTypesResponse>();
        services.AddApiOperation<MusicAlbumMusicsOperation, MusicAlbumMusicsRequest, MusicTabMusicsResponse>();
        services.AddApiOperation<MusicContributorMusicsOperation, MusicContributorMusicsRequest, MusicTabMusicsResponse>();
        services.AddApiOperation<MusicCommentsOperation, MusicCommentsRequest, MusicCommentsResponse>();
        services.AddApiOperation<MusicCommentCreateOperation, MusicCommentCreateRequest, MusicCommentCreateResponse>();
        services.AddApiOperation<MusicContributorOperation, MusicContributorRequest, MusicContributorsResponse>();
        services.AddApiOperation<MusicPlaylistsOperation, MusicPlaylistsRequest, MusicPlaylistsResponse>();
        services.AddApiOperation<MusicPlaylistMembershipOperation, MusicPlaylistMembershipRequest, MusicPlaylistMembershipResponse>();
        services.AddApiOperation<MusicPlaylistCreateOperation, MusicPlaylistCreateRequest, MusicPlaylistCreateResponse>();
        services.AddApiOperation<MediaOperation, MediaRequest, MediaResponse>();
        services.AddApiOperation<StoriesOperation, StoriesRequest, StoriesResponse>();
        services.AddApiOperation<MusicStoriesOperation, MusicStoriesRequest, StoriesResponse>();
        services.AddApiOperation<StoryDetailsOperation, StoryRequest, StoryResponse>();
        services.AddApiOperation<StoryViewOperation, StoryRequest, StoryResponse>();
        services.AddApiOperation<StoryLikeOperation, StoryLikeRequest, StoryResponse>();
        services.AddApiOperation<StoryCreateOperation, StoryCreateRequest, StoryResponse>();
        services.AddScoped<GlobalSearchOperation>();



        return services;
    }


    public static IServiceCollection AddApiOperation<
       [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TOperation,
       TRequest,
       TResponse>(this IServiceCollection services)
       where TOperation : class, IApiOperation<TRequest, TResponse>
       where TRequest : IApiRequest
       where TResponse : IApiResponse
    {
        services.AddScoped<TOperation>();
        return services;
    }
}
