using System.Text.Json;

namespace CrystalCode.Server.Interactions;

/// <summary>
/// One pending question for the operator. <see cref="Target"/> is the client
/// whose command raised it, or null when any client with the operate scope
/// may answer. <see cref="Resolve"/> parses and validates an answer and
/// completes the engine's wait; the first valid answer wins.
/// </summary>
public sealed class Interaction
{
    private readonly Func<JsonElement, (bool Valid, string Error)> _resolve;
    private readonly Action _cancel;
    private int _done;

    public Interaction(
        string id,
        string kind,
        string? target,
        Action<Utf8JsonWriter> payload,
        Func<JsonElement, (bool Valid, string Error)> resolve,
        Action cancel)
    {
        Id = id;
        Kind = kind;
        Target = target;
        Payload = payload;
        _resolve = resolve;
        _cancel = cancel;
    }

    public string Id { get; }

    public string Kind { get; }

    public string? Target { get; set; }

    public Action<Utf8JsonWriter> Payload { get; }

    public bool IsDone => Volatile.Read(ref _done) == 1;

    /// <summary>
    /// Validates before claiming the interaction, so an invalid answer leaves it open.
    /// Returns false with no error when another answer already won.
    /// </summary>
    public bool TryResolve(JsonElement answer, out string error)
    {
        error = string.Empty;
        if (IsDone)
        {
            return false;
        }

        lock (this)
        {
            if (IsDone)
            {
                return false;
            }

            var (valid, message) = _resolve(answer);
            if (!valid)
            {
                error = message;
                return false;
            }

            Volatile.Write(ref _done, 1);
            return true;
        }
    }

    public bool TryCancel()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1)
        {
            return false;
        }

        _cancel();
        return true;
    }

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("id", Id);
        writer.WriteString("kind", Kind);
        if (Target is not null)
        {
            writer.WriteString("target", Target);
        }

        writer.WriteStartObject("payload");
        Payload(writer);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
