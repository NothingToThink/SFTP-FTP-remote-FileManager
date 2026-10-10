using System.Text;

namespace VolcanoBoys.Agent;

/// <summary>A file the model wants to replace: the whole new content.</summary>
public sealed record FileEdit(string Path, string Content);

/// <summary>A file or an empty folder the model wants to delete.</summary>
public sealed record FileDelete(string Path);

/// <param name="Text">What the model said outside the blocks.</param>
/// <param name="Edits">Valid edits, in order of appearance.</param>
/// <param name="Deletes">Valid deletions, in order of appearance. Edits and deletions together are at most <see cref="ResponseParser.MaxActions"/>.</param>
/// <param name="Warnings">What was ignored and why; goes to the summary for the user.</param>
public sealed record ParsedResponse(
    string Text, IReadOnlyList<FileEdit> Edits, IReadOnlyList<FileDelete> Deletes, IReadOnlyList<string> Warnings);

/// <summary>
/// Splits the answer of the model into text, file edits and deletions. Knows nothing about the SDK and the network.
/// An edit is <c>&lt;&lt;&lt;FILE /path</c> on its own line, the new content, and <c>&gt;&gt;&gt;FILE</c> on its own line.
/// A deletion is one line <c>&lt;&lt;&lt;DELETE /path&gt;&gt;&gt;</c>.
/// </summary>
public static class ResponseParser
{
    public const int MaxActions = 5;
    public const string BlockStart = "<<<FILE";
    public const string BlockEnd = ">>>FILE";
    public const string DeleteStart = "<<<DELETE";
    public const string DeleteEnd = ">>>";

    public static ParsedResponse Parse(string response)
    {
        var lines = response.Replace("\r\n", "\n").Split('\n');
        var text = new List<string>();
        var edits = new List<FileEdit>();
        var deletes = new List<FileDelete>();
        var warnings = new List<string>();
        var skippedByLimit = 0;
        var actions = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var header = lines[i].TrimEnd();
            if (IsMarker(header, DeleteStart) || header.StartsWith(DeleteStart + DeleteEnd, StringComparison.Ordinal))
            {
                var deletePath = ParseDeletePath(header);
                if (deletePath is null)
                    warnings.Add($"строка «{Shorten(header)}» не заканчивается на {DeleteEnd}, удаление проигнорировано");
                else if (PathProblem(deletePath) is { } deleteProblem)
                    warnings.Add($"блок DELETE «{Shorten(deletePath)}» проигнорирован: {deleteProblem}");
                else if (actions >= MaxActions)
                    skippedByLimit++;
                else
                {
                    actions++;
                    deletes.Add(new FileDelete(deletePath));
                }

                continue;
            }

            if (!IsMarker(header, BlockStart))
            {
                text.Add(lines[i]);
                continue;
            }

            var path = header[BlockStart.Length..].Trim();
            var content = new List<string>();
            var closed = false;
            // Everything up to the first closing line is content, including a line that looks like a header.
            for (i++; i < lines.Length; i++)
            {
                if (lines[i].TrimEnd() == BlockEnd)
                {
                    closed = true;
                    break;
                }

                content.Add(lines[i]);
            }

            if (!closed)
            {
                warnings.Add($"блок «{Shorten(path)}» не закрыт ({BlockEnd}), правка проигнорирована");
                break;
            }

            if (PathProblem(path) is { } problem)
                warnings.Add($"блок «{Shorten(path)}» проигнорирован: {problem}");
            else if (actions >= MaxActions)
                skippedByLimit++;
            else
            {
                actions++;
                edits.Add(new FileEdit(path, JoinContent(content)));
            }
        }

        if (skippedByLimit > 0)
            warnings.Add($"лишних действий: {skippedByLimit} (за один ответ принимается не больше {MaxActions} блоков FILE и DELETE вместе)");

        // The same path in both kinds of blocks is a contradiction: neither is done.
        var contradictions = edits.Select(e => e.Path).Intersect(deletes.Select(d => d.Path)).ToHashSet();
        foreach (var conflict in contradictions)
            warnings.Add($"путь «{Shorten(conflict)}» и в FILE, и в DELETE: оба блока проигнорированы");
        if (contradictions.Count > 0)
        {
            edits.RemoveAll(e => contradictions.Contains(e.Path));
            deletes.RemoveAll(d => contradictions.Contains(d.Path));
        }

        return new ParsedResponse(string.Join("\n", text).Trim(), edits, deletes, warnings);
    }

    private static bool IsMarker(string line, string marker) =>
        line.StartsWith(marker, StringComparison.Ordinal)
        && (line.Length == marker.Length || char.IsWhiteSpace(line[marker.Length]));

    /// <returns>The path between the marker and <see cref="DeleteEnd"/>, or null if the line is not closed.</returns>
    private static string? ParseDeletePath(string line)
    {
        var rest = line[DeleteStart.Length..];
        return rest.EndsWith(DeleteEnd, StringComparison.Ordinal) ? rest[..^DeleteEnd.Length].Trim() : null;
    }

    // Every content line ends with a line break, so "a\nb\n" is two lines and an empty block is an empty file.
    private static string JoinContent(List<string> content)
    {
        var builder = new StringBuilder();
        foreach (var line in content)
            builder.Append(line).Append('\n');
        return builder.ToString();
    }

    private static string? PathProblem(string path)
    {
        if (path.Length == 0)
            return "путь пустой";
        if (path[0] != '/')
            return "путь не абсолютный";
        if (path.Any(char.IsControl) || path.Contains('\\'))
            return "в пути недопустимые символы";
        if (path.Split('/').Any(s => s is "." or ".."))
            return "в пути есть «.» или «..»";
        if (path.TrimEnd('/').Length == 0 || path.EndsWith('/'))
            return "путь указывает на папку";
        return null;
    }

    // The path comes from a model and ends up in a dialog: no control characters, no very long lines.
    private static string Shorten(string path)
    {
        var clean = new string(path.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return clean.Length <= 80 ? clean : clean[..79] + "…";
    }
}
