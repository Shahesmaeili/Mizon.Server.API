using System.Diagnostics;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: LocalMusicStemSeparator <audio-path> [--python python.exe] [--model htdemucs] [--device cuda|cpu] [--output directory]");
    Console.WriteLine("Prerequisite (once): python -m pip install -U demucs");
    return;
}

var inputPath = Path.GetFullPath(args[0]);
if (!File.Exists(inputPath))
    throw new FileNotFoundException("Input music was not found.", inputPath);

var pythonExecutable = GetOption("--python") ?? "python";
var model = GetOption("--model") ?? "htdemucs";
var device = GetOption("--device") ?? "cuda";
var outputDirectory = Path.GetFullPath(GetOption("--output") ?? Path.Combine(Path.GetDirectoryName(inputPath)!, "Separated"));
Directory.CreateDirectory(outputDirectory);

var startInfo = new ProcessStartInfo
{
    FileName = pythonExecutable,
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
};

startInfo.ArgumentList.Add("-m");
startInfo.ArgumentList.Add("demucs");
startInfo.ArgumentList.Add("--two-stems=vocals");
startInfo.ArgumentList.Add("--device");
startInfo.ArgumentList.Add(device);
startInfo.ArgumentList.Add("--out");
startInfo.ArgumentList.Add(outputDirectory);
startInfo.ArgumentList.Add("-n");
startInfo.ArgumentList.Add(model);
startInfo.ArgumentList.Add(inputPath);

using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start Python/Demucs.");
var outputTask = ForwardAsync(process.StandardOutput, Console.Out);
var errorTask = ForwardAsync(process.StandardError, Console.Error);
await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());

if (process.ExitCode != 0)
    throw new InvalidOperationException("Demucs did not complete. Install a local Python runtime and Demucs first, then retry.");

var stemDirectory = Path.Combine(outputDirectory, model, Path.GetFileNameWithoutExtension(inputPath));
var vocalsPath = Path.Combine(stemDirectory, "vocals.wav");
var instrumentalPath = Path.Combine(stemDirectory, "no_vocals.wav");

if (!File.Exists(vocalsPath) || !File.Exists(instrumentalPath))
    throw new FileNotFoundException("Demucs completed but expected vocal stems were not found.", stemDirectory);

Console.WriteLine();
Console.WriteLine($"Vocals: {vocalsPath}");
Console.WriteLine($"Instrumental: {instrumentalPath}");

string? GetOption(string option)
{
    var index = Array.FindIndex(args, item => string.Equals(item, option, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static async Task ForwardAsync(StreamReader reader, TextWriter writer)
{
    while (await reader.ReadLineAsync() is { } line)
        await writer.WriteLineAsync(line);
}
