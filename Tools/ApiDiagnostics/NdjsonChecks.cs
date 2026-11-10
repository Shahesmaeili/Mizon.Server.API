using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mizon.Server.API.Endpoints.Search;

internal static class NdjsonChecks
{
    public static async Task RunAsync()
    {
        var rows = new[]
        {
            new NdjsonTestRow("سلام\nnext line \"quoted\" \\ path", 1),
            new NdjsonTestRow("🎧 موسیقی", 2),
            new NdjsonTestRow(string.Empty, 3),
        };

        var normal = new TrackingSource(rows);
        var output = new RecordingPipeWriter();
        await NdjsonResponseWriter.WriteAsync(normal.ReadAsync(), output,
            ApiNdjsonTestJsonContext.Default.NdjsonTestRow, CancellationToken.None);
        var actual = Encoding.UTF8.GetString(output.Written.Span);
        var expected = string.Concat(rows.Select(row =>
            JsonSerializer.Serialize(row, ApiNdjsonTestJsonContext.Default.NdjsonTestRow) + "\n"));
        Require(actual == expected, "source-generated JSON/casing matches the former serializer");
        Require(actual.Count(ch => ch == '\n') == rows.Length, "embedded newlines stay escaped");
        Require(output.FlushCount == rows.Length, "one flush per complete row");
        Require(normal.Disposed && !output.Completed, "source disposed and host pipe left open");
        foreach (var frame in output.Frames)
        {
            Require(frame[^1] == (byte)'\n', "a flush contains a complete NDJSON line");
            using var document = JsonDocument.Parse(frame.AsMemory(0, frame.Length - 1));
            Require(document.RootElement.TryGetProperty("Text", out _), "PascalCase wire contract retained");
        }
        Console.WriteLine("PASS NDJSON Unicode/escaping, exact serialization, one complete line per flush, pipe ownership.");

        var gate = new TaskCompletionSource<FlushResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFlush = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var incremental = new TrackingSource(rows);
        var blockedOutput = new RecordingPipeWriter
        {
            FlushOverride = token =>
            {
                firstFlush.TrySetResult();
                return new ValueTask<FlushResult>(gate.Task.WaitAsync(token));
            }
        };
        var blockedWrite = NdjsonResponseWriter.WriteAsync(incremental.ReadAsync(), blockedOutput,
            ApiNdjsonTestJsonContext.Default.NdjsonTestRow, CancellationToken.None);
        await firstFlush.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(incremental.ReadCount == 1 && !blockedWrite.IsCompleted, "backpressure stops enumeration");
        Require(blockedOutput.Frames.Count == 1 && blockedOutput.Frames[0][^1] == (byte)'\n',
            "first line visible before the producer completes");
        blockedOutput.FlushOverride = null;
        gate.SetResult(new FlushResult(false, false));
        await blockedWrite.WaitAsync(TimeSpan.FromSeconds(5));
        Require(incremental.Disposed && incremental.ReadCount == rows.Length, "source resumes after backpressure");
        Console.WriteLine("PASS NDJSON incremental delivery and backpressure.");

        var completedSource = new TrackingSource(rows);
        var completedOutput = new RecordingPipeWriter
        {
            FlushOverride = _ => ValueTask.FromResult(new FlushResult(false, true))
        };
        await NdjsonResponseWriter.WriteAsync(completedSource.ReadAsync(), completedOutput,
            ApiNdjsonTestJsonContext.Default.NdjsonTestRow, CancellationToken.None);
        Require(completedSource.ReadCount == 1 && completedSource.Disposed && !completedOutput.Completed,
            "reader completion promptly disposes source without completing host pipe");
        Console.WriteLine("PASS NDJSON disconnected/completed reader disposes upstream.");

        var cancelledSource = new TrackingSource(rows);
        var cancelledOutput = new RecordingPipeWriter
        {
            FlushOverride = _ => ValueTask.FromResult(new FlushResult(true, false))
        };
        await ExpectCancellationAsync(() => NdjsonResponseWriter.WriteAsync(cancelledSource.ReadAsync(), cancelledOutput,
            ApiNdjsonTestJsonContext.Default.NdjsonTestRow, CancellationToken.None));
        Require(cancelledSource.ReadCount == 1 && cancelledSource.Disposed && !cancelledOutput.Completed,
            "cancelled flush releases upstream");
        Console.WriteLine("PASS NDJSON cancelled flush releases upstream.");

        using var cancellation = new CancellationTokenSource();
        var cancellationGate = new TaskCompletionSource<FlushResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abortedSource = new TrackingSource(rows);
        var abortedOutput = new RecordingPipeWriter
        {
            FlushOverride = token =>
            {
                entered.TrySetResult();
                return new ValueTask<FlushResult>(cancellationGate.Task.WaitAsync(token));
            }
        };
        var abortedWrite = NdjsonResponseWriter.WriteAsync(abortedSource.ReadAsync(), abortedOutput,
            ApiNdjsonTestJsonContext.Default.NdjsonTestRow, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await ExpectCancellationAsync(() => abortedWrite);
        Require(abortedSource.Disposed && abortedSource.ReadCount == 1 && !abortedOutput.Completed,
            "request cancellation interrupts blocked flush");
        Console.WriteLine("PASS NDJSON request cancellation interrupts backpressure.");

        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        var preCancelledSource = new TrackingSource(rows);
        var preCancelledOutput = new RecordingPipeWriter();
        await ExpectCancellationAsync(() => NdjsonResponseWriter.WriteAsync(preCancelledSource.ReadAsync(), preCancelledOutput,
            ApiNdjsonTestJsonContext.Default.NdjsonTestRow, alreadyCancelled.Token));
        Require(preCancelledOutput.Written.Length == 0 && preCancelledSource.Disposed, "pre-cancelled output is empty");
        Console.WriteLine("PASS NDJSON pre-cancellation.");

        var failingSource = new TrackingSource(rows) { FailAfterFirst = true };
        var failingOutput = new RecordingPipeWriter();
        try
        {
            await NdjsonResponseWriter.WriteAsync(failingSource.ReadAsync(), failingOutput,
                ApiNdjsonTestJsonContext.Default.NdjsonTestRow, CancellationToken.None);
            throw new InvalidOperationException("Source failure was swallowed.");
        }
        catch (SourceFailureException) { }
        Require(failingSource.Disposed && failingOutput.FlushCount == 1 && !failingOutput.Completed,
            "producer errors propagate and retain pipe ownership");
        Console.WriteLine("PASS NDJSON producer error propagation.");

        var empty = new TrackingSource([]);
        var emptyOutput = new RecordingPipeWriter();
        await NdjsonResponseWriter.WriteAsync(empty.ReadAsync(), emptyOutput,
            ApiNdjsonTestJsonContext.Default.NdjsonTestRow, CancellationToken.None);
        Require(empty.Disposed && emptyOutput.FlushCount == 0 && emptyOutput.Written.Length == 0,
            "empty results do not invent a JSON row");
        Console.WriteLine("PASS NDJSON empty result stream.");
    }

    private static async Task ExpectCancellationAsync(Func<Task> action)
    {
        try
        {
            await action().WaitAsync(TimeSpan.FromSeconds(5));
            throw new InvalidOperationException("Expected cancellation.");
        }
        catch (OperationCanceledException) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TrackingSource(NdjsonTestRow[] rows)
    {
        public int ReadCount { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailAfterFirst { get; init; }

        public async IAsyncEnumerable<NdjsonTestRow> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (FailAfterFirst && ReadCount == 1) throw new SourceFailureException();
                    ReadCount++;
                    yield return row;
                    await Task.Yield();
                }
            }
            finally { Disposed = true; }
        }
    }

    private sealed class RecordingPipeWriter : PipeWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        private int _lastFlushed;
        public ReadOnlyMemory<byte> Written => _buffer.WrittenMemory;
        public List<byte[]> Frames { get; } = [];
        public int FlushCount { get; private set; }
        public bool Completed { get; private set; }
        public Func<CancellationToken, ValueTask<FlushResult>>? FlushOverride { get; set; }
        public override bool CanGetUnflushedBytes => true;
        public override long UnflushedBytes => _buffer.WrittenCount - _lastFlushed;
        public override void Advance(int bytes) => _buffer.Advance(bytes);
        public override Memory<byte> GetMemory(int sizeHint = 0) => _buffer.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint = 0) => _buffer.GetSpan(sizeHint);
        public override void CancelPendingFlush() { }
        public override void Complete(Exception? exception = null) => Completed = true;
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FlushCount++;
            Frames.Add(_buffer.WrittenMemory[_lastFlushed..].ToArray());
            _lastFlushed = _buffer.WrittenCount;
            return FlushOverride?.Invoke(cancellationToken)
                ?? ValueTask.FromResult(new FlushResult(false, false));
        }
    }

    private sealed class SourceFailureException : Exception;
}

internal sealed record NdjsonTestRow(string Text, int Number);

[JsonSerializable(typeof(NdjsonTestRow))]
internal partial class ApiNdjsonTestJsonContext : JsonSerializerContext;

