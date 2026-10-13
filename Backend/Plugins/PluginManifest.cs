using System.Text.Json;

namespace Backend.Plugins;

/// <summary>plugin.json (a subset of the spec manifest). Unknown fields are ignored.</summary>
public sealed record PluginManifest(
    string? Id,
    string? DisplayName,
    string? Version,
    string? Main,
    PluginContributes? Contributes)
{
    public const string FileName = "plugin.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <exception cref="JsonException">Not a valid manifest.</exception>
    public static PluginManifest Parse(string json) =>
        JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions)
        ?? throw new JsonException("plugin.json is empty.");
}

public sealed record PluginContributes(IReadOnlyList<PluginCommandManifest>? Commands);

public sealed record PluginCommandManifest(string? Id, string? Title);
