namespace Mizon.Server.Logic.Responses.Clients;

public sealed class ClientMoodResponse : IApiResponse
{
    public ClientMoodItemResponse? CurrentMood { get; set; }
}

