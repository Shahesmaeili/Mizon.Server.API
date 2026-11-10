using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Mizon.Server.API.Endpoints.Search;

internal static class NdjsonResponseWriter
{
    public static async Task WriteAsync<T>(
        IAsyncEnumerable<T> source,
        PipeWriter output,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(output);
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(writer, item, jsonTypeInfo);
            writer.Flush();
            output.GetSpan(1)[0] = (byte)'\n';
            output.Advance(1);

            // Make each complete row visible immediately while preserving the
            // response pipe's backpressure. Do not buffer the whole search.
            var flush = await output.FlushAsync(cancellationToken);
            if (flush.IsCanceled)
                throw new OperationCanceledException(cancellationToken);
            if (flush.IsCompleted)
                break;

            writer.Reset(output);
        }

        // The host owns the response pipe. Disposing this writer releases only
        // its serialization state; completing the pipe belongs to ASP.NET Core.
    }
}
