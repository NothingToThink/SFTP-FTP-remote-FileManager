using System.Globalization;
using System.Text;
using Backend.Ui;
using Core.Implementations.Protocol;
using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Core.Ssh;
using FileManager.Plugins;

namespace Backend.Plugins;

/// <summary>
/// The single place where the user is asked before a plugin changes files. The host asks, not the
/// plugin: the plugin may be driven by a model that reads files, and a file can tell it to delete things.
/// </summary>
public interface IWriteConfirmation
{
    /// <summary>
    /// Asks the client of the running command whether <paramref name="pluginName"/> may do
    /// <paramref name="action"/> with <paramref name="path"/> (and <paramref name="destination"/>, if any).
    /// Returns only when the user allowed it.
    /// </summary>
    /// <exception cref="PermissionDeniedException">The user declined or closed the dialog.</exception>
    /// <exception cref="UiUnavailableException">There is no client to ask.</exception>
    Task ConfirmAsync(string pluginName, Guid connectionId, string action, string path, string? destination,
        CancellationToken ct);
}

public sealed class WriteConfirmation(IUiBridge bridge, IConnectionManager connections, ILogger<WriteConfirmation> logger)
    : IWriteConfirmation
{
    public const string Allow = "Разрешить";
    public const string Deny = "Отклонить";

    private const int MaxNameLength = 60;
    private const int MaxPathLength = 200;

    public async Task ConfirmAsync(string pluginName, Guid connectionId, string action, string path,
        string? destination, CancellationToken ct)
    {
        var sessionId = UiSessionScope.SessionId
                        ?? throw new UiUnavailableException("No UI client: the action was not started by a command.");
        var where = DescribeConnection(connectionId);

        var target = Clean(path, MaxPathLength);
        if (destination is not null)
            target += " в " + Clean(destination, MaxPathLength);
        var message = $"{Clean(pluginName, MaxNameLength)} хочет {action} {target} на {where}. Разрешить?";

        var answer = await bridge.ShowMessageAsync(sessionId, MessageSeverity.Warning, message, [Allow, Deny], ct);

        // Only the exact "Разрешить" is a yes: Deny, a closed window and anything unexpected are a no.
        var allowed = answer == Allow;
        logger.LogInformation("Plugin '{Plugin}' asked to {Action} on {Where}: {Answer}.",
            pluginName, action, where, allowed ? "allowed" : "denied");
        if (!allowed)
            throw new PermissionDeniedException($"The user did not allow the plugin to {action}.");
    }

    /// <summary>
    /// What the user knows the connection by. Core does not keep the profile name next to a connection,
    /// so this is the host where the connection exposes it; never the user name or any credentials.
    /// </summary>
    private string DescribeConnection(Guid connectionId)
    {
        if (!connections.TryGetConnection(connectionId, out var connection) || connection is null)
            return $"соединении {connectionId}";

        return DescribeHost(connection) is { } host ? Clean(host, MaxNameLength) : $"соединении {connectionId}";
    }

    private static string? DescribeHost(Connection connection) => connection switch
    {
        ISshSessionProvider { Session.Host: { Length: > 0 } host } => host,
        LocalConnection => "локальном диске",
        _ => null,
    };

    /// <summary>
    /// The text comes from the plugin or a model and is shown to the user as a question: control and
    /// invisible formatting characters (new lines, bidi overrides) could fake a different dialog, and a very
    /// long text could push the real question out of view. The middle of a long text is cut, so both the
    /// start and the end stay visible.
    /// </summary>
    private static string Clean(string text, int maxLength)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var category = char.GetUnicodeCategory(c);
            builder.Append(category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.OtherNotAssigned
                ? '�'
                : c);
        }

        var clean = builder.ToString();
        if (clean.Length <= maxLength)
            return clean;

        var head = maxLength / 2 - 1;
        var tail = maxLength - head - 1;
        return clean[..head] + "…" + clean[^tail..];
    }
}
