using System.Buffers;
using System.Text.Json;

namespace CrystalCode.Bridge.Transport;

/// <summary>Builds one UTF-8 JSON value with an explicit writer.</summary>
internal static class JsonText
{
    public static byte[] Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Writes <paramref name="value"/> as a property when it is not null.</summary>
    public static void Optional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
