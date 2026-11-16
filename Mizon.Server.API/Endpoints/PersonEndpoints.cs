using Mizon.Server.Logic.Operations.Persons;
using Mizon.Server.Logic.Requests.Persons;
using Mizon.Server.Logic.Responses.Persons;

namespace Mizon.Server.API.Endpoints;

internal static class PersonEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/Persons").RequireAuthorization();
        group.MapGet("PersonProfile", ProfileAsync);
        group.MapGet("PersonMusics", MusicsAsync);
        group.MapPost("PersonFollow", FollowAsync);
    }

    private static Task<BaseApiResponse<PersonProfileResponse>> ProfileAsync([AsParameters] PersonProfileRequest request,
        IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<PersonProfileOperation, PersonProfileRequest, PersonProfileResponse>(new() { Content = request }, metadata, token);

    private static Task<BaseApiResponse<PersonMusicsResponse>> MusicsAsync([AsParameters] PersonMusicsRequest request,
        IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<PersonMusicsOperation, PersonMusicsRequest, PersonMusicsResponse>(new() { Content = request }, metadata, token);

    private static Task<BaseApiResponse<PersonFollowResponse>> FollowAsync(BaseApiRequest<PersonFollowRequest> request,
        IApiOperationExecutor executor, RequestMetadata metadata, CancellationToken token) =>
        executor.ExecuteAsync<PersonFollowOperation, PersonFollowRequest, PersonFollowResponse>(request, metadata, token);
}
