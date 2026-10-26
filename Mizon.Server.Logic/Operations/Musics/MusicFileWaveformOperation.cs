namespace Mizon.Server.Logic.Operations.Musics;

public sealed class MusicFileWaveformOperation(MizonDatabaseContext mizonDatabaseContext)
    : IApiOperation<MusicFileWaveformRequest, MusicFileWaveformResponse>
{
    public async Task<BaseApiResponse<MusicFileWaveformResponse>> HandleAsync(BaseApiRequest<MusicFileWaveformRequest> request, RequestMetadata? requestMetadata, CancellationToken cancellationToken)
    {
        var musicFile = await mizonDatabaseContext.MusicFiles
              .AsNoTracking()
              .Where(m => m.PublicId == request.Content!.MusicFilePublicId)
              .Select(m => new
              {
                  m.Id,
                  m.MusicId,
              })
              .FirstOrDefaultAsync(cancellationToken);

        if (musicFile is null)
        {
            return new()
            {
                Error = new()
                {
                    Code = OperationErrorCodes.Musics.MusicFileNotFound
                }
            };
        }


        var waveformResponse = await mizonDatabaseContext.MusicFileWaveforms
           .AsNoTracking()
           .Where(m => m.MusicFileId == musicFile.Id)
           .OrderBy(m => m.OffsetInMilliseconds)
           .Select(m => new WaveformResponse
           {
               RMS = m.RMS,
               IsClipping = m.IsClipping,
               MaximumAmplitude = m.MaximumAmplitude,
               MinimumAmplitude = m.MinimumAmplitude,
               OffsetInMilliseconds = m.OffsetInMilliseconds,
               Peak = m.Peak,
               Transient = m.Transient,
           })
           .ToListAsync(cancellationToken);

        var primaryAccentColor = await mizonDatabaseContext.MusicCovers
            .AsNoTracking()
            .Where(m => m.MusicId == musicFile.MusicId)
            .Select(m => m.PrimaryAccentColor)
            .FirstOrDefaultAsync(cancellationToken) ?? "#00B4FF";

        return new()
        {
            Content = new()
            {
                Waveforms = waveformResponse,
                LightingProfile = CreateLightingProfile(primaryAccentColor),
            }
        };
    }

    private static MusicLightingProfileResponse CreateLightingProfile(string primaryColor)
    {
        var normalizedPrimaryColor = NormalizeColor(primaryColor);
        return new()
        {
            PrimaryColor = normalizedPrimaryColor,
        };
    }

    private static string NormalizeColor(string color)
    {
        var value = color.Trim();
        if (value.Length == 9 && value[0] == '#')
            value = $"#{value[3..]}";

        return value.Length == 7 && value[0] == '#' && value[1..].All(Uri.IsHexDigit)
            ? value.ToUpperInvariant()
            : "#00B4FF";
    }

}
