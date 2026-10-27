using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CrystalCode.Bridge.Transport;

/// <summary>
/// Newline-delimited JSON-RPC 2.0 over two streams. One reader dispatches
/// incoming messages in order; each request runs as its own task so a long
/// <c>session/prompt</c> does not block <c>session/cancel</c> or permission
/// answers. One writer drains a queue, so a line is never interleaved with
/// another and notifications keep the order in which they were sent.
/// </summary>
internal sealed class JsonRpcConnection : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _incoming = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentBag<Task> _running = [];
    private IJsonRpcHandler? _handler;
    private Task? _writer;
    private long _nextId;
    private int _closed;

    public JsonRpcConnection(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        _input = input;
        _output = output;
    }

    /// <summary>
    /// Reads until the input ends or <paramref name="cancellationToken"/> is
    /// cancelled, then asks the handler to shut down and answers every request
    /// still running with <c>-32800</c>.
    /// </summary>
    public async Task RunAsync(IJsonRpcHandler handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _writer = WriteLoopAsync();
        try
        {
            await ReadLoopAsync(cancellationToken);
        }
        finally
        {
            await handler.ShutdownAsync();
            foreach (var source in _incoming.Values)
            {
                source.Cancel();
            }

            foreach (var pending in _pending.Values)
            {
                pending.TrySetCanceled();
            }

            await Task.WhenAll(_running.ToArray()).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Interlocked.Exchange(ref _closed, 1);
            _outgoing.Writer.TryComplete();
            await _writer;
        }
    }

    /// <summary>Queues one notification.</summary>
    public void Notify(string method, Action<Utf8JsonWriter> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(parameters);
        Send(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            parameters(writer);
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// Sends a request to the client and waits for its result. Cancelling
    /// sends <c>$/cancel_request</c> and stops waiting; an error response
    /// throws <see cref="JsonRpcException"/>.
    /// </summary>
    public async Task<JsonElement> RequestAsync(
        string method,
        Action<Utf8JsonWriter> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        if (Volatile.Read(ref _closed) == 1)
        {
            _pending.TryRemove(id, out _);
            throw new OperationCanceledException("The connection is closed.");
        }

        Send(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            parameters(writer);
            writer.WriteEndObject();
        });
        await using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var cancelled))
            {
                Notify("$/cancel_request", writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("requestId", id);
                    writer.WriteEndObject();
                });
                cancelled.TrySetCanceled(cancellationToken);
            }
        });
        return await completion.Task;
    }

    private void Send(Action<Utf8JsonWriter> write)
    {
        var bytes = JsonText.Write(write);
        if (!_outgoing.Writer.TryWrite(bytes))
        {
            // The connection already closed; the peer is gone.
            return;
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var line in _outgoing.Reader.ReadAllAsync())
            {
                await _output.WriteAsync(line);
                _output.WriteByte((byte)'\n');
                await _output.FlushAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The client closed its end. Reading stops on its own.
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(_input, Utf8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            if (line.Length > 0 && !string.IsNullOrWhiteSpace(line))
            {
                Dispatch(line);
            }
        }
    }

    private void Dispatch(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            SendError(null, new JsonRpcException(JsonRpcErrorCode.ParseError, "The message is not valid JSON."));
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                SendError(null, new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "Batches are not supported."));
                return;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                SendError(null, new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "The message must be a JSON-RPC object."));
                return;
            }

            var hasId = root.TryGetProperty("id", out var idElement);
            JsonElement? id = hasId ? idElement.Clone() : null;
            if (!root.TryGetProperty("method", out var methodElement))
            {
                if (hasId)
                {
                    Resolve(idElement, root);
                }

                return;
            }

            if (methodElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(methodElement.GetString()))
            {
                SendError(id, new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "method must be a string."));
                return;
            }

            var method = methodElement.GetString()!;
            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : JsonDocument.Parse("{}").RootElement.Clone();
            if (!hasId)
            {
                HandleNotification(method, parameters);
                return;
            }

            HandleRequest(id!.Value, method, parameters);
        }
    }

    private void HandleNotification(string method, JsonElement parameters)
    {
        if (method == "$/cancel_request")
        {
            if (parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("requestId", out var requestId)
                && _incoming.TryGetValue(requestId.GetRawText(), out var source))
            {
                source.Cancel();
            }

            return;
        }

        _running.Add(RunNotificationAsync(method, parameters));
    }

    private async Task RunNotificationAsync(string method, JsonElement parameters)
    {
        try
        {
            await _handler!.HandleNotificationAsync(method, parameters);
        }
        catch (Exception exception) when (exception is JsonRpcException or OperationCanceledException)
        {
            // A notification has no response to carry a failure.
        }
    }

    private void HandleRequest(JsonElement id, string method, JsonElement parameters)
    {
        var key = id.GetRawText();
        var source = new CancellationTokenSource();
        if (!_incoming.TryAdd(key, source))
        {
            source.Dispose();
            SendError(id, new JsonRpcException(JsonRpcErrorCode.InvalidRequest, "The request id is already in use."));
            return;
        }

        _running.Add(RunRequestAsync(id, key, method, parameters, source));
    }

    private async Task RunRequestAsync(
        JsonElement id,
        string key,
        string method,
        JsonElement parameters,
        CancellationTokenSource source)
    {
        // Yield first, so the reader goes on to the next line while this runs.
        await Task.Yield();
        try
        {
            var reply = await _handler!.HandleRequestAsync(method, parameters, source.Token);
            Send(writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                writer.WritePropertyName("id");
                id.WriteTo(writer);
                writer.WritePropertyName("result");
                reply.Write(writer);
                writer.WriteEndObject();
            });
            reply.AfterSend?.Invoke();
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            SendError(id, new JsonRpcException(JsonRpcErrorCode.RequestCancelled, "The request was cancelled."));
        }
        catch (JsonRpcException exception)
        {
            SendError(id, exception);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or OperationCanceledException
            or FormatException)
        {
            SendError(id, JsonRpcException.Refused("internal", exception.Message));
        }
        finally
        {
            _incoming.TryRemove(key, out _);
            source.Dispose();
        }
    }

    private void Resolve(JsonElement id, JsonElement response)
    {
        if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number)
            || !_pending.TryRemove(number, out var completion))
        {
            return;
        }

        if (response.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var value)
                ? value
                : JsonRpcErrorCode.InternalError;
            var message = error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;
            completion.TrySetException(new JsonRpcException(code, message));
            return;
        }

        completion.TrySetResult(response.TryGetProperty("result", out var result)
            ? result.Clone()
            : JsonDocument.Parse("null").RootElement.Clone());
    }

    private void SendError(JsonElement? id, JsonRpcException exception)
    {
        Send(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            if (id is { } value)
            {
                value.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteStartObject("error");
            writer.WriteNumber("code", exception.Code);
            writer.WriteString("message", exception.Message);
            if (exception.Kind is not null || exception.Data is not null)
            {
                writer.WriteStartObject("data");
                writer.WriteStartObject("crystal");
                JsonText.Optional(writer, "kind", exception.Kind);
                exception.Data?.Invoke(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    public async ValueTask DisposeAsync()
    {
        _outgoing.Writer.TryComplete();
        if (_writer is not null)
        {
            await _writer.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
