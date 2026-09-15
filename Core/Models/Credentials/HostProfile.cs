using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Models.Credentials ;

public class HostProfileConverter : JsonConverter<HostProfile>
{
    
    private static readonly JsonSerializerOptions InnerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    public override HostProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        return new HostProfile(
            root.GetProperty("Host").GetString()!,
            Enum.Parse<Protocol>(root.GetProperty("Protocol").GetString()!),
            JsonSerializer.Deserialize<AuthData>(root.GetProperty("Auth").GetRawText(), InnerOptions)!,
            root.TryGetProperty("Port", out var port) ? port.GetInt32() : null
        );
    }

    public override void Write(Utf8JsonWriter writer, HostProfile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Host", value.Host);
        writer.WriteString("Protocol", value.Protocol.ToString());
        writer.WriteNumber("Port", value.EffectivePort);
        writer.WritePropertyName("Auth");
        JsonSerializer.Serialize(writer, value.Auth, InnerOptions);
        writer.WriteEndObject();
    }
}


[JsonConverter(typeof(HostProfileConverter))]
public record HostProfile(
    string Host,
    Protocol Protocol,
    AuthData Auth,
    int? Port = null)
{
    public int EffectivePort => Port ?? DefaultPortFor(Protocol);

    private static int DefaultPortFor(Protocol protocol) => protocol switch
    {
        Protocol.Ftp => 21,
        Protocol.Sftp => 22,
        Protocol.Local => 0, 
        _ => throw new ArgumentOutOfRangeException(nameof(Protocol))
    };
}
