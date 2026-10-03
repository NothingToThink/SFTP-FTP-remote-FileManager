using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileManagerClient.Models;

public enum Protocol
{
    Local,
    Ftp,
    Sftp,
}

public abstract record AuthData;

public record PasswordAuth(string Username, string Password) : AuthData;

public record KeyAuth(string Username, string KeyPath, string? Passphrase = null) : AuthData;

public record AnonymousAuth : AuthData;

/// <summary>
/// Повторяет серверный HostProfileConverter из Core: поля Host/Protocol/Port/Auth — PascalCase
/// (регистр важен при отправке на сервер), Port пишется всегда, Auth различается по "$type".
/// </summary>
public sealed class HostProfileJsonConverter : JsonConverter<HostProfile>
{
    public override HostProfile? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var host = root.GetProperty("Host").GetString()!;
        var protocol = Enum.Parse<Protocol>(root.GetProperty("Protocol").GetString()!, ignoreCase: false);
        int? port = root.TryGetProperty("Port", out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32()
            : null;
        return new HostProfile(host, protocol, ReadAuth(root.GetProperty("Auth")), port);
    }

    private static AuthData ReadAuth(JsonElement el)
    {
        var type = el.GetProperty("$type").GetString();
        return type switch
        {
            "password" => new PasswordAuth(
                el.GetProperty("Username").GetString()!,
                el.GetProperty("Password").GetString()!),
            "key" => new KeyAuth(
                el.GetProperty("Username").GetString()!,
                el.GetProperty("KeyPath").GetString()!,
                el.TryGetProperty("Passphrase", out var pp) ? pp.GetString() : null),
            "anonymous" => new AnonymousAuth(),
            _ => throw new JsonException($"Неизвестный тип авторизации '{type}'"),
        };
    }

    public override void Write(Utf8JsonWriter writer, HostProfile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Host", value.Host);
        writer.WriteString("Protocol", value.Protocol.ToString());
        writer.WriteNumber("Port", value.EffectivePort);
        writer.WritePropertyName("Auth");
        WriteAuth(writer, value.Auth);
        writer.WriteEndObject();
    }

    private static void WriteAuth(Utf8JsonWriter writer, AuthData auth)
    {
        writer.WriteStartObject();
        switch (auth)
        {
            case PasswordAuth(var user, var password):
                writer.WriteString("$type", "password");
                writer.WriteString("Username", user);
                writer.WriteString("Password", password);
                break;
            case KeyAuth(var user, var keyPath, var passphrase):
                writer.WriteString("$type", "key");
                writer.WriteString("Username", user);
                writer.WriteString("KeyPath", keyPath);
                if (passphrase is not null)
                    writer.WriteString("Passphrase", passphrase);
                break;
            case AnonymousAuth:
                writer.WriteString("$type", "anonymous");
                break;
            default:
                throw new JsonException($"Неизвестный тип авторизации '{auth.GetType().Name}'");
        }
        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(HostProfileJsonConverter))]
public record HostProfile(string Host, Protocol Protocol, AuthData Auth, int? Port = null)
{
    public int EffectivePort => Port ?? DefaultPortFor(Protocol);

    private static int DefaultPortFor(Protocol protocol) => protocol switch
    {
        Protocol.Ftp => 21,
        Protocol.Sftp => 22,
        Protocol.Local => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };
}

public record SavedProfile(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("hostProfile")] HostProfile HostProfile)
{
    public static SavedProfile Create(string name, HostProfile hostProfile)
        => new(Guid.NewGuid(), name, hostProfile);
}

/// <summary>
/// Ответы сервер отдаёт в camelCase (дефолт ASP.NET Core), поэтому у полей явные имена.
/// </summary>
public class FileItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("lastModified")] public DateTime LastModified { get; set; }
    [JsonPropertyName("isDirectory")] public bool IsDirectory { get; set; }
    [JsonPropertyName("fullPath")] public string FullPath { get; set; } = string.Empty;
    [JsonPropertyName("permissions")] public string Permissions { get; set; } = string.Empty;

    [JsonIgnore] public string SizeDisplay => IsDirectory
        ? string.Empty
        : HumanSize(Size);

    private static string HumanSize(long size)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double value = size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}

public static class ClientJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
