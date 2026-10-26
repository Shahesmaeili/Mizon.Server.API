using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: LocalMusicLyricTranscriber <audio-path> [--language fa|auto] [--model small|medium] [--download-model] [--output output.json]");
    return;
}

var inputPath = Path.GetFullPath(args[0]);
if (!File.Exists(inputPath))
    throw new FileNotFoundException("The input audio file was not found.", inputPath);

var language = GetOption("--language") ?? "fa";
var modelName = GetOption("--model") ?? "small";
var outputPath = Path.GetFullPath(GetOption("--output") ?? Path.ChangeExtension(inputPath, ".lyrics.json"));
var downloadModel = args.Contains("--download-model", StringComparer.OrdinalIgnoreCase);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var modelType = modelName.ToLowerInvariant() switch
{
    "base" => GgmlType.Base,
    "small" => GgmlType.Small,
    "medium" => GgmlType.Medium,
    _ => throw new ArgumentException("Model must be base, small, or medium."),
};

var modelDirectory = Path.Combine(AppContext.BaseDirectory, "models");
Directory.CreateDirectory(modelDirectory);
var modelPath = Path.Combine(modelDirectory, $"ggml-{modelName.ToLowerInvariant()}.bin");
var partialModelPath = modelPath + ".download";

if (!File.Exists(modelPath) || new FileInfo(modelPath).Length < 100_000_000)
{
    if (File.Exists(modelPath))
        File.Delete(modelPath);

    if (!downloadModel)
        throw new FileNotFoundException($"Model not found: {modelPath}. Run once with --download-model while online; all later transcription is fully local.");

    Console.WriteLine($"Downloading ggml-{modelName}.bin once…");
    await using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType);
    await using (var modelFile = File.Create(partialModelPath))
        await modelStream.CopyToAsync(modelFile);

    File.Move(partialModelPath, modelPath, overwrite: true);
}

var temporaryWavPath = Path.Combine(Path.GetTempPath(), $"mizon-lyric-{Guid.NewGuid():N}.wav");
try
{
    ConvertToWhisperWav(inputPath, temporaryWavPath);

    using var factory = WhisperFactory.FromPath(modelPath);
    using var processor = factory.CreateBuilder()
        .WithLanguage(language)
        .WithThreads(Math.Min(Environment.ProcessorCount, 16))
        .WithTokenTimestamps()
        .SplitOnWord()
        .WithNoContext()
        .Build();

    var stopwatch = Stopwatch.StartNew();
    var words = new List<TimedWord>();
    await using var wavStream = File.OpenRead(temporaryWavPath);
    await foreach (var segment in processor.ProcessAsync(wavStream))
    {
        foreach (var word in ExtractWords(segment))
            words.Add(word);
    }

    stopwatch.Stop();
    var lines = BuildLines(words);
    var document = new MusicLyricImportDocument
    {
        LanguageCode = language,
        SourceAudioPath = inputPath,
        Model = modelName,
        ProcessingDurationInMilliseconds = (int)stopwatch.ElapsedMilliseconds,
        MusicLyricLines = lines,
    };

    await using var output = File.Create(outputPath);
    await JsonSerializer.SerializeAsync(output, document, jsonOptions);
    Console.WriteLine($"Saved {lines.Count} lines and {words.Count} timed words to: {outputPath}");
    Console.WriteLine($"Processing time: {stopwatch.Elapsed}");
}
finally
{
    if (File.Exists(temporaryWavPath))
        File.Delete(temporaryWavPath);
}

string? GetOption(string name)
{
    var index = Array.FindIndex(args, item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void ConvertToWhisperWav(string inputPath, string outputPath)
{
    using var source = new MediaFoundationReader(inputPath);
    var targetFormat = new WaveFormat(16_000, 16, 1);
    using var resampler = new MediaFoundationResampler(source, targetFormat)
    {
        ResamplerQuality = 60,
    };
    WaveFileWriter.CreateWaveFile(outputPath, resampler);
}

static IEnumerable<TimedWord> ExtractWords(object segment)
{
    var start = ReadTime(segment, "Start");
    var end = ReadTime(segment, "End");
    var text = ReadString(segment, "Text");
    var tokens = segment.GetType().GetProperty("Tokens", BindingFlags.Instance | BindingFlags.Public)?.GetValue(segment) as IEnumerable;

    // SplitOnWord makes each returned segment a word for current Whisper.net runtimes.
    // Tokens are still inspected first when the native runtime exposes token timestamps.
    if (tokens is not null)
    {
        var timedTokens = tokens.Cast<object>()
            .Select(token => new TimedWord(ReadString(token, "Text"), ReadTime(token, "Start", start), ReadTime(token, "End", end)))
            .Where(token => !string.IsNullOrWhiteSpace(token.Text) && token.EndMilliseconds > token.StartMilliseconds)
            .ToList();

        if (timedTokens.Count > 0)
            return MergeTokensIntoWords(timedTokens);
    }

    return string.IsNullOrWhiteSpace(text) || end <= start ? [] : [new(text.Trim(), start, end)];
}

static IEnumerable<TimedWord> MergeTokensIntoWords(IReadOnlyList<TimedWord> tokens)
{
    var words = new List<TimedWord>();
    foreach (var token in tokens)
    {
        var text = token.Text.Trim();
        if (text.Length == 0)
            continue;

        // Whisper's multilingual tokens begin a new word with whitespace. Persian
        // does too, so this keeps Persian words intact while preserving their timing.
        if (words.Count == 0 || char.IsWhiteSpace(token.Text[0]))
        {
            words.Add(token with { Text = text });
            continue;
        }

        var previous = words[^1];
        words[^1] = previous with { Text = previous.Text + text, EndMilliseconds = token.EndMilliseconds };
    }

    return words;
}

static List<MusicLyricLineImport> BuildLines(IReadOnlyList<TimedWord> words)
{
    var lines = new List<MusicLyricLineImport>();
    var current = new List<TimedWord>();

    foreach (var word in words.OrderBy(item => item.StartMilliseconds))
    {
        var previous = current.LastOrDefault();
        var hasLongGap = previous is not null && word.StartMilliseconds - previous.EndMilliseconds > 1_100;
        var endsSentence = previous is not null && EndsSentence(previous.Text);

        if (current.Count >= 10 || hasLongGap || endsSentence)
        {
            AddLine(lines, current);
            current.Clear();
        }

        current.Add(word);
    }

    AddLine(lines, current);
    return lines;
}

static void AddLine(List<MusicLyricLineImport> lines, IReadOnlyList<TimedWord> words)
{
    if (words.Count == 0)
        return;

    lines.Add(new()
    {
        LineOrder = lines.Count,
        StartTimeInMilliseconds = words[0].StartMilliseconds,
        EndTimeInMilliseconds = words[^1].EndMilliseconds,
        Text = string.Join(' ', words.Select(word => word.Text)),
        MusicLyricLineWords = words.Select((word, index) => new MusicLyricLineWordImport
        {
            WordOrder = index,
            StartTimeInMilliseconds = word.StartMilliseconds,
            EndTimeInMilliseconds = word.EndMilliseconds,
            Text = word.Text,
        }).ToList(),
    });
}

static bool EndsSentence(string text) =>
    text.EndsWith('.') ||
    text.EndsWith('!') ||
    text.EndsWith('؟') ||
    text.EndsWith('?') ||
    text.EndsWith('…');

static int ReadTime(object value, string propertyName, int fallback = 0)
{
    var property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
    return property switch
    {
        TimeSpan time => (int)time.TotalMilliseconds,
        int milliseconds => milliseconds,
        long milliseconds => (int)milliseconds,
        float seconds => (int)(seconds * 1_000),
        double seconds => (int)(seconds * 1_000),
        _ => fallback,
    };
}

static string ReadString(object value, string propertyName) =>
    value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value)?.ToString() ?? string.Empty;

file sealed record TimedWord(string Text, int StartMilliseconds, int EndMilliseconds);

file sealed class MusicLyricImportDocument
{
    public string LanguageCode { get; init; } = null!;
    public string SourceAudioPath { get; init; } = null!;
    public string Model { get; init; } = null!;
    public int ProcessingDurationInMilliseconds { get; init; }
    public List<MusicLyricLineImport> MusicLyricLines { get; init; } = [];
}

file sealed class MusicLyricLineImport
{
    public int StartTimeInMilliseconds { get; init; }
    public int EndTimeInMilliseconds { get; init; }
    public int LineOrder { get; init; }
    public string Text { get; init; } = null!;
    public List<MusicLyricLineWordImport> MusicLyricLineWords { get; init; } = [];
}

file sealed class MusicLyricLineWordImport
{
    public int StartTimeInMilliseconds { get; init; }
    public int EndTimeInMilliseconds { get; init; }
    public int WordOrder { get; init; }
    public string Text { get; init; } = null!;
}
