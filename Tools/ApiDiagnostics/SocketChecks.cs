using System.Net.WebSockets;
using System.Text.Json;

internal static class SocketChecks
{
    public static async Task RunAsync(Uri baseUri, string token, Guid clientPublicId, Guid musicPublicId)
    {
        await using var first = await Peer.ConnectAsync(baseUri, token);
        await using var second = await Peer.ConnectAsync(baseUri, token);
        await first.InvokeAsync("RegisterDevice", "diagnostics");
        await second.InvokeAsync("RegisterDevice", "diagnostics");
        await first.InvokeAsync("JoinEntity", "Music", musicPublicId);
        await second.InvokeAsync("JoinEntity", "music", musicPublicId);
        await second.WaitEventAsync("EntityViewersCountUpdated", args => args[2].GetInt32() == 2);

        await first.InvokeAsync("UpdateMusicPlayback", musicPublicId, 123000L, true);
        await second.WaitEventAsync("MusicListenerUpdated", args =>
            args[1].GetProperty("positionMilliseconds").GetInt64() == 123000 && args[1].GetProperty("isPlaying").GetBoolean());
        await second.InvokeAsync("UpdateMusicPlayback", musicPublicId, 209000L, false);
        await first.InvokeAsync("LeaveEntity");

        await using (var third = await Peer.ConnectAsync(baseUri, token))
        {
            await third.InvokeAsync("JoinEntity", "Music", musicPublicId);
            var snapshot = await third.WaitEventAsync("MusicListenersSnapshot", _ => true);
            var listeners = snapshot.GetProperty("arguments")[1];
            Require(listeners.GetArrayLength() == 1, "same user represented once");
            var listener = listeners[0];
            Require(listener.GetProperty("clientPublicId").GetGuid() == clientPublicId, "identity serialized");
            Require(listener.GetProperty("positionMilliseconds").GetInt64() == 209000 &&
                !listener.GetProperty("isPlaying").GetBoolean(), "newest connection's paused listener survives older leave");
            Require(listener.GetProperty("updatedAtUtc").TryGetDateTimeOffset(out _), "timestamp metadata");

            await second.InvokeAsync("LeaveEntity");
            await third.WaitEventAsync("MusicListenerRemoved", args => args[1].GetGuid() == clientPublicId);
            await third.InvokeAsync("UpdateMusicPlayback", musicPublicId, -1L, true);
            await third.WaitEventAsync("MusicListenerUpdated", args => args[1].GetProperty("positionMilliseconds").GetInt64() == 0);
            await third.InvokeAsync("UnregisterDevice");
            await third.InvokeAsync("LeaveEntity");
        }

        await second.InvokeAsync("UnregisterDevice");
        await second.WaitEventAsync("UnregistrationConfirmed", args => args[0].GetString() == clientPublicId.ToString());
        await first.InvokeAsync("JoinEntity", "Music", musicPublicId);
        var freshSnapshot = await first.WaitEventAsync("MusicListenersSnapshot", args => args[1].GetArrayLength() == 0);
        await first.InvokeAsync("LeaveEntity");
        Console.WriteLine("PASS actual SignalR WebSockets: handshake, registration, groups, JSON primitives/listeners, play/pause/seek, multiple connections, cleanup.");
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException("WebSocket check: " + check);
    }

    private sealed class Peer : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly MemoryStream _pending = new();
        private readonly Queue<JsonElement> _messages = new();
        private readonly List<JsonElement> _events = [];
        private readonly byte[] _receive = new byte[8192];
        private int _invocation;

        public static async Task<Peer> ConnectAsync(Uri baseUri, string token)
        {
            var peer = new Peer();
            try
            {
                // The runner accepts loopback URLs only; this permits the local development certificate.
                Require(baseUri.IsLoopback, "diagnostics host must be loopback");
                peer._socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                peer._socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var uri = new UriBuilder(baseUri) { Scheme = "wss", Path = "/mizonHub" }.Uri;
                await peer._socket.ConnectAsync(uri, timeout.Token);
                await peer._socket.SendAsync("{\"protocol\":\"json\",\"version\":1}\u001e"u8.ToArray(),
                    WebSocketMessageType.Text, true, timeout.Token);
                var handshake = await peer.ReadAsync(timeout.Token);
                Require(!handshake.TryGetProperty("error", out _), "handshake");
                return peer;
            }
            catch { await peer.DisposeAsync(); throw; }
        }

        public async Task InvokeAsync(string method, params object[] arguments)
        {
            var id = (++_invocation).ToString();
            using var data = new MemoryStream();
            using (var writer = new Utf8JsonWriter(data))
            {
                writer.WriteStartObject(); writer.WriteNumber("type", 1);
                writer.WriteString("invocationId", id); writer.WriteString("target", method);
                writer.WriteStartArray("arguments");
                foreach (var argument in arguments)
                {
                    switch (argument)
                    {
                        case string value: writer.WriteStringValue(value); break;
                        case Guid value: writer.WriteStringValue(value); break;
                        case long value: writer.WriteNumberValue(value); break;
                        case bool value: writer.WriteBooleanValue(value); break;
                        default: throw new ArgumentException("Unsupported test argument.");
                    }
                }
                writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
            }
            data.WriteByte(0x1e);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _socket.SendAsync(data.GetBuffer().AsMemory(0, (int)data.Length), WebSocketMessageType.Text, true, timeout.Token);
            while (true)
            {
                var message = await ReadAsync(timeout.Token);
                var type = message.GetProperty("type").GetInt32();
                if (type == 1) _events.Add(message);
                if (type == 3 && message.GetProperty("invocationId").GetString() == id)
                {
                    Require(!message.TryGetProperty("error", out var error), method + " failed: " + error);
                    return;
                }
                Require(type != 7, "hub connection closed");
            }
        }

        public async Task<JsonElement> WaitEventAsync(string name, Func<JsonElement, bool> matches)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                for (var i = 0; i < _events.Count; i++)
                {
                    var item = _events[i];
                    if (item.GetProperty("target").GetString() != name || !matches(item.GetProperty("arguments"))) continue;
                    _events.RemoveAt(i);
                    return item;
                }
                var message = await ReadAsync(timeout.Token);
                var type = message.GetProperty("type").GetInt32();
                if (type == 1) _events.Add(message);
                Require(type != 7, "hub closed while awaiting " + name);
            }
        }

        private async Task<JsonElement> ReadAsync(CancellationToken cancellationToken)
        {
            while (_messages.Count == 0)
            {
                var received = await _socket.ReceiveAsync(_receive.AsMemory(), cancellationToken);
                Require(received.MessageType == WebSocketMessageType.Text, "expected text frame");
                _pending.Write(_receive, 0, received.Count);
                var buffer = _pending.GetBuffer();
                var end = (int)_pending.Length;
                var start = 0;
                for (var i = 0; i < end; i++)
                {
                    if (buffer[i] != 0x1e) continue;
                    using var document = JsonDocument.Parse(buffer.AsMemory(start, i - start));
                    _messages.Enqueue(document.RootElement.Clone());
                    start = i + 1;
                }
                if (start > 0)
                {
                    Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                    _pending.SetLength(end - start);
                    _pending.Position = _pending.Length;
                }
            }
            return _messages.Dequeue();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "diagnostics finished", timeout.Token);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
            finally { _socket.Dispose(); _pending.Dispose(); }
        }
    }
}
