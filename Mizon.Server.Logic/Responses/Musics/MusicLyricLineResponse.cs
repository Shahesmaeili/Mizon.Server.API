namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicLyricLineResponse : IApiResponse
{
    public List<MusicLyricLineItemResponse> MusicLyricLines { get; set; } = [];
}

public sealed class MusicLyricLineItemResponse
{
    public int StartTimeInMilliseconds { get; set; }
    public int EndTimeInMilliseconds { get; set; }
    public int LineOrder { get; set; }
    public string Text { get; set; } = null!;

    public List<MusicLyricLineWordResponse> MusicLyricLineWords { get; set; } = [];
}


public sealed class MusicLyricLineWordResponse
{
    public int StartTimeInMilliseconds { get; set; }
    public int EndTimeInMilliseconds { get; set; }
    public int WordOrder { get; set; }
    public string Text { get; set; } = null!;
}
