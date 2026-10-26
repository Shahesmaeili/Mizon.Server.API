namespace Mizon.Server.Logic.Operations.Stories;

public sealed class MusicStoriesOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicStoriesRequest, StoriesResponse>
{
    public Task<BaseApiResponse<StoriesResponse>> HandleAsync(BaseApiRequest<MusicStoriesRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
        => new StoriesOperation(mizonDatabaseContext).HandleAsync(new() { Content = new() { MusicPublicId = request.Content!.MusicPublicId } }, requestMetadata, cancellationToken);
}
