using Microsoft.EntityFrameworkCore;
using Mizon.Database;
using Mizon.Database.Entities;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

const string connectionString = "Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True";
const int waveformFrameDurationInMilliseconds = 50;

var options = AnalysisOptions.Parse(args);
if (options is null)
{
    Console.WriteLine("Usage: MusicFileAnalysis --music-public-id <guid> [--steps quality,waveform] [--force] [--ffprobe path] [--ffmpeg path]");
    Console.WriteLine("   or: MusicFileAnalysis --music-id <int> [--steps quality,waveform] [--force] [--ffprobe path] [--ffmpeg path]");
    return 1;
}

var databaseOptions = new DbContextOptionsBuilder<MizonDatabaseContext>()
    .UseSqlServer(connectionString)
    .Options;

Ffprobe.ExecutablePath = options.FfprobePath ?? Environment.GetEnvironmentVariable("MIZON_FFPROBE_PATH") ?? Ffprobe.FindDefaultExecutablePath();
Ffmpeg.ExecutablePath = options.FfmpegPath ?? Environment.GetEnvironmentVariable("MIZON_FFMPEG_PATH") ?? Ffmpeg.FindDefaultExecutablePath(Ffprobe.ExecutablePath);

await using var database = new MizonDatabaseContext(databaseOptions);
var musicQuery = database.Musics.AsQueryable();
musicQuery = options.MusicId is { } musicId
    ? musicQuery.Where(item => item.Id == musicId)
    : musicQuery.Where(item => item.PublicId == options.MusicPublicId);

var music = await musicQuery
    .Select(item => new
    {
        item.Id,
        item.Title,
        Files = item.MusicFiles.Select(file => new MusicFileInput(file.Id, file.PublicId, file.Media.StoragePath, file.Media.FileName)).ToList(),
    })
    .SingleOrDefaultAsync();

if (music is null)
{
    Console.Error.WriteLine("Music was not found.");
    return 2;
}

Console.WriteLine($"Music: {music.Title} ({music.Id})");
Console.WriteLine($"Files to process: {music.Files.Count}");

foreach (var musicFile in music.Files)
{
    Console.WriteLine();
    Console.WriteLine($"MusicFile {musicFile.Id}: {musicFile.FileName}");

    await using var localFile = await LocalAudioFile.AcquireAsync(musicFile.StoragePath);
    if (localFile is null)
    {
        Console.Error.WriteLine("  Skipped: StoragePath is not an accessible local file or HTTP(S) resource.");
        continue;
    }

    if (options.Steps.Contains(AnalysisStep.Quality))
        await AnalyzeQualityAsync(database, musicFile.Id, localFile.Path, options.Force);

    if (options.Steps.Contains(AnalysisStep.Waveform))
        await AnalyzeWaveformAsync(database, musicFile.Id, localFile.Path, options.Force);

    if (options.Steps.Contains(AnalysisStep.Fingerprint))
        Console.WriteLine("  Fingerprint: not run because MusicFileFingerprint is not active in the database model yet.");

    if (options.Steps.Contains(AnalysisStep.Stems))
        await AnalyzeStemsAsync(database, musicFile, localFile.Path, options.Force);
}

return 0;

static async Task AnalyzeQualityAsync(MizonDatabaseContext database, int musicFileId, string path, bool force)
{
    var current = await database.MusicFileQualities.SingleOrDefaultAsync(item => item.MusicFileId == musicFileId);
    if (current is not null && !force)
    {
        Console.WriteLine("  Quality: already exists (use --force to calculate again).");
        return;
    }

    var metadata = await AudioMetadata.ProbeAsync(path);
    var channel = await database.MusicFileQualityChannels
        .Where(item => item.ChannelCount == metadata.ChannelCount)
        .Where(item => item.ChannelLayout == metadata.ChannelLayout)
        .SingleOrDefaultAsync()
        ?? await database.MusicFileQualityChannels.SingleAsync(item => item.ChannelCount == metadata.ChannelCount);
    var codec = await database.MusicFileQualityCodecs.SingleAsync(item => item.Title == metadata.Codec);
    var format = await database.MusicFileQualityFormats.SingleAsync(item => item.Title == metadata.Format);
    var bitDepth = metadata.BitDepth is { } bits
        ? await database.MusicFileQualityBitDepths.SingleOrDefaultAsync(item => item.BitDepth == bits)
        : null;
    var sampleFormat = metadata.SampleFormat is null
        ? null
        : await database.MusicFileQualitySampleFormats.SingleOrDefaultAsync(item => item.Title == metadata.SampleFormat);
    var encoder = metadata.Encoder is null
        ? null
        : await database.MusicFileQualityEncoders.SingleOrDefaultAsync(item => item.Title == metadata.Encoder);
    var encoderMode = metadata.EncoderMode is null
        ? null
        : await database.MusicFileQualityEncoderModes.SingleOrDefaultAsync(item => item.Title == metadata.EncoderMode);

    var target = current ?? new MusicFileQuality { MusicFileId = musicFileId };
    target.BitrateKbps = metadata.BitrateKbps;
    target.IsVariableBitRate = metadata.IsVariableBitRate;
    target.SampleRateHz = metadata.SampleRateHz;
    target.ChannelCount = metadata.ChannelCount;
    target.MusicFileQualityChannelId = channel.Id;
    target.MusicFileQualityCodecId = codec.Id;
    target.MusicFileQualityFormatId = format.Id;
    target.MusicFileQualityBitDepthId = bitDepth?.Id;
    target.MusicFileQualitySampleFormatId = sampleFormat?.Id;
    target.MusicFileQualityEncoderId = encoder?.Id;
    target.MusicFileQualityEncoderModeId = encoderMode?.Id;

    if (current is null)
        database.MusicFileQualities.Add(target);

    await database.SaveChangesAsync();
    Console.WriteLine($"  Quality: {metadata.Format}/{metadata.Codec}, {metadata.SampleRateHz}Hz, {metadata.ChannelCount}ch, {metadata.BitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}kbps.");
}

static async Task AnalyzeWaveformAsync(MizonDatabaseContext database, int musicFileId, string path, bool force)
{
    var waveformQuery = database.MusicFileWaveforms.Where(item => item.MusicFileId == musicFileId);
    if (!force && await waveformQuery.AnyAsync())
    {
        Console.WriteLine("  Waveform: already exists (use --force to calculate again).");
        return;
    }

    var metadata = await AudioMetadata.ProbeAsync(path);
    var samplesPerFrame = Math.Max(1, (int)Math.Round(metadata.SampleRateHz * waveformFrameDurationInMilliseconds / 1000d) * metadata.ChannelCount);
    var buffer = new byte[samplesPerFrame * sizeof(float)];
    var frames = new List<MusicFileWaveform>();
    var offsetInMilliseconds = 0;
    var previousRms = 0f;

    {
        await using var reader = Ffmpeg.DecodeToFloat32(path);
        while (true)
        {
            var bytesRead = await ReadFrameAsync(reader.StandardOutput, buffer);
            var sampleCount = bytesRead / sizeof(float);
            if (sampleCount == 0)
                break;

            var minimum = float.MaxValue;
            var maximum = float.MinValue;
            var peak = 0f;
            double sumOfSquares = 0;
            for (var index = 0; index < sampleCount; index++)
            {
                var sample = BitConverter.ToSingle(buffer, index * sizeof(float));
                minimum = Math.Min(minimum, sample);
                maximum = Math.Max(maximum, sample);
                peak = Math.Max(peak, Math.Abs(sample));
                sumOfSquares += sample * sample;
            }

            var rms = (float)Math.Sqrt(sumOfSquares / sampleCount);
            frames.Add(new MusicFileWaveform
            {
                MusicFileId = musicFileId,
                OffsetInMilliseconds = offsetInMilliseconds,
                MinimumAmplitude = ToInt16(minimum),
                MaximumAmplitude = ToInt16(maximum),
                RMS = rms,
                Peak = ToInt16(peak),
                Transient = Math.Abs(rms - previousRms),
                IsClipping = peak >= 0.999f,
            });

            previousRms = rms;
            offsetInMilliseconds += waveformFrameDurationInMilliseconds;
        }
    }

    await waveformQuery.ExecuteDeleteAsync();
    database.MusicFileWaveforms.AddRange(frames);
    await database.SaveChangesAsync();
    Console.WriteLine($"  Waveform: saved {frames.Count} frames at {waveformFrameDurationInMilliseconds}ms resolution.");
}

static async Task<int> ReadFrameAsync(Stream stream, byte[] buffer)
{
    var totalRead = 0;
    while (totalRead < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(totalRead));
        if (read == 0)
            break;
        totalRead += read;
    }

    return totalRead;
}

static short ToInt16(float sample) => (short)Math.Clamp((int)Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);

static async Task AnalyzeStemsAsync(MizonDatabaseContext database, MusicFileInput musicFile, string inputPath, bool force)
{
    var vocalsType = await database.MusicFileStemTypes.SingleAsync(item => item.Title == "Vocals");
    var instrumentalType = await database.MusicFileStemTypes.SingleAsync(item => item.Title == "Instrumental");
    var existingCount = await database.MusicFileStems.Where(item => item.MusicFileId == musicFile.Id).CountAsync();
    if (existingCount > 0 && !force)
    {
        Console.WriteLine("  Stems: already exist (use --force to generate again).");
        return;
    }

    var root = Path.Combine(AppContext.BaseDirectory, "GeneratedStems", musicFile.PublicId.ToString("N"));
    Directory.CreateDirectory(root);
    await Demucs.RunAsync(inputPath, root);
    var resultDirectory = Path.Combine(root, "htdemucs", Path.GetFileNameWithoutExtension(inputPath));
    await UpsertStemAsync(database, musicFile.Id, vocalsType.Id, Path.Combine(resultDirectory, "vocals.flac"));
    await UpsertStemAsync(database, musicFile.Id, instrumentalType.Id, Path.Combine(resultDirectory, "no_vocals.flac"));
    Console.WriteLine("  Stems: Vocals and Instrumental were stored.");
}

static async Task UpsertStemAsync(MizonDatabaseContext database, int musicFileId, byte stemTypeId, string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException("Demucs did not create the expected stem.", path);
    var source = await database.MusicFiles.Where(item => item.Id == musicFileId).Select(item => new { item.Media.MediaKeyId, item.Media.MediaEncryptionTypeId, Encryption = item.Media.MediaEncryptionType.Title, item.Media.MediaStorageTypeId }).SingleAsync();
    var stem = await database.MusicFileStems.Include(item => item.Media).SingleOrDefaultAsync(item => item.MusicFileId == musicFileId && item.MusicFileStemTypeId == stemTypeId);
    var media = stem?.Media ?? new Media { MediaKeyId = source.MediaKeyId, MediaEncryptionTypeId = source.MediaEncryptionTypeId, MediaStorageTypeId = source.MediaStorageTypeId };
    var info = new FileInfo(path);
    media.StoragePath = new Uri(path).AbsoluteUri;
    media.FileName = info.Name;
    media.FileSize = info.Length;
    media.TotalChunks = 1;
    media.ChunkSize = checked((int)Math.Min(info.Length, int.MaxValue));
    media.EncryptionIV = RandomNumberGenerator.GetBytes(16);
    await using var file = File.OpenRead(path);
    media.FileHash = Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
    if (stem is null) database.MusicFileStems.Add(new MusicFileStem { MusicFileId = musicFileId, MusicFileStemTypeId = stemTypeId, Media = media });
    await database.SaveChangesAsync();
}

internal sealed record MusicFileInput(int Id, Guid PublicId, string StoragePath, string FileName);

internal enum AnalysisStep { Quality, Waveform, Fingerprint, Stems }

internal sealed class AnalysisOptions
{
    public int? MusicId { get; init; }
    public Guid? MusicPublicId { get; init; }
    public bool Force { get; init; }
    public string? FfprobePath { get; init; }
    public string? FfmpegPath { get; init; }
    public HashSet<AnalysisStep> Steps { get; init; } = [AnalysisStep.Quality, AnalysisStep.Waveform];

    public static AnalysisOptions? Parse(string[] arguments)
    {
        var musicId = GetOption(arguments, "--music-id");
        var musicPublicId = GetOption(arguments, "--music-public-id");
        if (!int.TryParse(musicId, out var parsedMusicId) && !Guid.TryParse(musicPublicId, out _))
            return null;

        var requestedSteps = GetOption(arguments, "--steps");
        var steps = new HashSet<AnalysisStep>();
        foreach (var value in (requestedSteps ?? "quality,waveform").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<AnalysisStep>(value, true, out var step))
                steps.Add(step);
            else
                throw new ArgumentException($"Unknown analysis step '{value}'.");
        }

        return new AnalysisOptions
        {
            MusicId = int.TryParse(musicId, out parsedMusicId) ? parsedMusicId : null,
            MusicPublicId = Guid.TryParse(musicPublicId, out var parsedPublicId) ? parsedPublicId : null,
            Force = arguments.Contains("--force", StringComparer.OrdinalIgnoreCase),
            FfprobePath = GetOption(arguments, "--ffprobe"),
            FfmpegPath = GetOption(arguments, "--ffmpeg"),
            Steps = steps,
        };
    }

    private static string? GetOption(string[] arguments, string name)
    {
        var index = Array.FindIndex(arguments, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}

internal sealed class LocalAudioFile : IAsyncDisposable
{
    private readonly string? _temporaryDirectory;
    public string Path { get; }

    private LocalAudioFile(string path, string? temporaryDirectory)
    {
        Path = path;
        _temporaryDirectory = temporaryDirectory;
    }

    public static async Task<LocalAudioFile?> AcquireAsync(string storagePath)
    {
        if (Uri.TryCreate(storagePath, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
                return File.Exists(uri.LocalPath) ? new LocalAudioFile(uri.LocalPath, null) : null;

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                var temporaryDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Mizon", "MusicFileAnalysis", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);
                var destination = System.IO.Path.Combine(temporaryDirectory, System.IO.Path.GetFileName(uri.LocalPath));
                using var client = new HttpClient();
                await using var source = await client.GetStreamAsync(uri);
                await using var output = File.Create(destination);
                await source.CopyToAsync(output);
                return new LocalAudioFile(destination, temporaryDirectory);
            }
        }

        return File.Exists(storagePath) ? new LocalAudioFile(storagePath, null) : null;
    }

    public ValueTask DisposeAsync()
    {
        if (_temporaryDirectory is not null && Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
        return ValueTask.CompletedTask;
    }
}

internal sealed record AudioMetadata(int? BitrateKbps, int SampleRateHz, byte ChannelCount, string? ChannelLayout, byte? BitDepth, string? SampleFormat, string Codec, string Format, bool IsVariableBitRate, string? Encoder, string? EncoderMode)
{
    public static async Task<AudioMetadata> ProbeAsync(string path)
    {
        var result = await Ffprobe.RunAsync(path);
        var stream = result.Streams.SingleOrDefault(item => item.CodecType == "audio")
            ?? throw new InvalidOperationException("ffprobe did not return an audio stream.");
        var format = ResolveFormat(result.Format.FormatName, path);
        var codec = ResolveCodec(format, stream.CodecName);
        var bitDepth = stream.BitsPerRawSample ?? stream.BitsPerSample;
        var sampleFormat = ResolveSampleFormat(stream.SampleFormat);
        var isVbr = string.Equals(stream.BitRateMode, "VBR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.Format.BitRateMode, "VBR", StringComparison.OrdinalIgnoreCase)
            || HasVariableBitRateMarker(path, codec);
        var bitRate = IsPlausibleBitRate(stream.BitRate) ? stream.BitRate : result.Format.BitRate;
        return new AudioMetadata(
            IsPlausibleBitRate(bitRate) ? (int?)Math.Round(bitRate!.Value / 1000d) : null,
            stream.SampleRate,
            checked((byte)stream.Channels),
            NormalizeChannelLayout(stream.ChannelLayout),
            bitDepth,
            sampleFormat,
            codec,
            format,
            isVbr,
            ResolveEncoder(stream.Encoder),
            codec is "FLAC" or "PCM" or "ALAC" ? "Lossless" : isVbr ? "VBR" : null);
    }

    private static string ResolveCodec(string format, string description)
    {
        var source = $"{format} {description}".ToUpperInvariant();
        if (source.Contains("FLAC")) return "FLAC";
        if (source.Contains("OPUS")) return "Opus";
        if (source.Contains("VORBIS")) return "Vorbis";
        if (source.Contains("ALAC")) return "ALAC";
        if (source.Contains("AAC") || format == "MP4") return "AAC";
        if (source.Contains("MPEG") || format == "MP3") return "MP3";
        if (source.Contains("PCM") || format == "WAV") return "PCM";
        throw new NotSupportedException($"No seeded codec matches '{description}' in {format}.");
    }

    private static string ResolveFormat(string? formatName, string path)
    {
        var format = (formatName ?? System.IO.Path.GetExtension(path).TrimStart('.')).ToUpperInvariant();
        if (format.Contains("MATROSKA")) return "Matroska";
        if (format.Contains("WEBM")) return "WebM";
        if (format.Contains("MOV") || format.Contains("MP4") || format.Contains("M4A")) return "MP4";
        if (format.Contains("OGG")) return "Ogg";
        if (format.Contains("WAV")) return "WAV";
        if (format.Contains("FLAC")) return "FLAC";
        if (format.Contains("MP3")) return "MP3";
        throw new NotSupportedException($"No seeded format matches '{formatName}'.");
    }

    private static string? ResolveSampleFormat(string? sampleFormat) => sampleFormat?.ToLowerInvariant() switch
    {
        "u8" => "Unsigned 8-bit",
        "s16" => "Signed 16-bit",
        "s24" => "Signed 24-bit",
        "s32" => "Signed 32-bit",
        "flt" or "fltp" => "Float 32-bit",
        "dbl" or "dblp" => "Float 64-bit",
        _ => null,
    };

    private static bool IsPlausibleBitRate(long? bitRate) => bitRate is >= 8_000 and <= 100_000_000;

    private static string? NormalizeChannelLayout(string? channelLayout) => channelLayout?.Replace("(side)", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveEncoder(string? description)
    {
        if (description?.Contains("LAME", StringComparison.OrdinalIgnoreCase) == true) return "LAME";
        if (description?.Contains("libopus", StringComparison.OrdinalIgnoreCase) == true) return "libopus";
        return null;
    }

    private static bool HasVariableBitRateMarker(string path, string codec)
    {
        if (codec != "MP3") return false;
        var bytes = File.ReadAllBytes(path).AsSpan(0, (int)Math.Min(new FileInfo(path).Length, 8 * 1024));
        return bytes.IndexOf("Xing"u8) >= 0 || bytes.IndexOf("VBRI"u8) >= 0;
    }
}

internal static class Ffprobe
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string ExecutablePath { get; set; } = "ffprobe";

    public static string FindDefaultExecutablePath()
    {
        var outputLocalTools = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        var projectLocalTools = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ffmpeg"));
        var localMizonTools = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mizon",
            "Tools",
            "FFmpeg");
        var outputFfprobe = FindExecutable(outputLocalTools, "ffprobe.exe");
        var projectFfprobe = FindExecutable(projectLocalTools, "ffprobe.exe");
        var localFfprobe = FindExecutable(localMizonTools, "ffprobe.exe");
        return outputFfprobe ?? projectFfprobe ?? localFfprobe ?? "ffprobe";
    }

    private static string? FindExecutable(string directory, string fileName) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, fileName, SearchOption.AllDirectories).FirstOrDefault()
        : null;

    public static async Task<FfprobeResult> RunAsync(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start ffprobe.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed: {errorTask.Result.Trim()}");

        return JsonSerializer.Deserialize<FfprobeResult>(outputTask.Result, SerializerOptions)
            ?? throw new InvalidOperationException("ffprobe returned no readable JSON metadata.");
    }
}

internal static class Ffmpeg
{
    public static string ExecutablePath { get; set; } = "ffmpeg";

    public static string FindDefaultExecutablePath(string ffprobeExecutablePath)
    {
        if (!string.Equals(ffprobeExecutablePath, "ffprobe", StringComparison.OrdinalIgnoreCase))
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffprobeExecutablePath)!, "ffmpeg.exe");
            if (File.Exists(sibling))
                return sibling;
        }

        return "ffmpeg";
    }

    public static FfmpegDecodedAudio DecodeToFloat32(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a:0");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("f32le");
        startInfo.ArgumentList.Add("-acodec");
        startInfo.ArgumentList.Add("pcm_f32le");
        startInfo.ArgumentList.Add("pipe:1");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start ffmpeg.");
        return new FfmpegDecodedAudio(process);
    }
}

internal static class Demucs
{
    private const string PythonPath = @"C:\Users\Ali\AppData\Local\Programs\Python\Python312\python.exe";

    public static async Task RunAsync(string inputPath, string outputPath)
    {
        var startInfo = new ProcessStartInfo { FileName = PythonPath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        var ffmpegDirectory = Path.GetDirectoryName(Ffmpeg.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
            startInfo.Environment["PATH"] = $"{ffmpegDirectory};{startInfo.Environment["PATH"]}";
        foreach (var argument in new[] { "-m", "demucs", "--two-stems", "vocals", "--flac", "--device", "cpu", "--out", outputPath, "-n", "htdemucs", inputPath }) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start Demucs.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
        if (process.ExitCode != 0) throw new InvalidOperationException($"Demucs failed: {stderr.Result.Trim()}");
    }
}

internal sealed class FfmpegDecodedAudio : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _errorTask;

    public FfmpegDecodedAudio(Process process)
    {
        _process = process;
        _errorTask = process.StandardError.ReadToEndAsync();
    }

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public async ValueTask DisposeAsync()
    {
        await _process.WaitForExitAsync();
        if (_process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed: {(await _errorTask).Trim()}");
        _process.Dispose();
    }
}

internal sealed class FfprobeResult
{
    public List<FfprobeStream> Streams { get; init; } = [];
    public FfprobeFormat Format { get; init; } = new();
}

internal sealed class FfprobeStream
{
    [JsonPropertyName("codec_type")]
    public string? CodecType { get; init; }
    [JsonPropertyName("codec_name")]
    public string? CodecName { get; init; }
    [JsonPropertyName("sample_rate")]
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    [JsonPropertyName("channel_layout")]
    public string? ChannelLayout { get; init; }
    [JsonPropertyName("bits_per_sample")]
    public byte? BitsPerSample { get; init; }
    [JsonPropertyName("bits_per_raw_sample")]
    public byte? BitsPerRawSample { get; init; }
    [JsonPropertyName("sample_fmt")]
    public string? SampleFormat { get; init; }
    [JsonPropertyName("bit_rate")]
    public long? BitRate { get; init; }
    [JsonPropertyName("bit_rate_mode")]
    public string? BitRateMode { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
    public string? Encoder => Tags is not null && Tags.TryGetValue("encoder", out var encoder) ? encoder : null;
}

internal sealed class FfprobeFormat
{
    [JsonPropertyName("format_name")]
    public string? FormatName { get; init; }
    [JsonPropertyName("bit_rate")]
    public long? BitRate { get; init; }
    [JsonPropertyName("bit_rate_mode")]
    public string? BitRateMode { get; init; }
}
