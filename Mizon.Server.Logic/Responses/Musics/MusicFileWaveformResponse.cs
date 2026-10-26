namespace Mizon.Server.Logic.Responses.Musics;

public sealed class MusicFileWaveformResponse : IApiResponse
{
    public List<WaveformResponse> Waveforms { get; set; } = [];
    public MusicLightingProfileResponse LightingProfile { get; set; } = new();
}

/// <summary>Lighting intensity comes from persisted waveform frames; color comes from the cover.</summary>
public sealed class MusicLightingProfileResponse
{
    public string PrimaryColor { get; set; } = "#00B4FF";
}


public sealed class WaveformResponse
{
    public int OffsetInMilliseconds { get; set; }
    public short MinimumAmplitude { get; set; }
    public short MaximumAmplitude { get; set; }
    public float RMS { get; set; }
    public short Peak { get; set; }
    public float Transient { get; set; }
    public bool IsClipping { get; set; }
}
