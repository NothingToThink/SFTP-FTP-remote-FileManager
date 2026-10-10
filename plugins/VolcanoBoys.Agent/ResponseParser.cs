using System.Text;

namespace VolcanoBoys.Agent;

/// <summary>A file the model wants to replace: the whole new content.</summary>
public sealed record FileEdit(string Path, string Content);

/// <param name="Text">What the model said outside the file blocks.</param>
/// <param name="Edits">Valid edits, at most <see cref="ResponseParser.MaxEdits"/>.</param>
/// <param name="Warnings">What was ignored and why; goes to the summary for the user.</param>
public sealed record ParsedResponse(string Text, IReadOnlyList<FileEdit> Edits, IReadOnlyList<string> Warnings);

/// <summary>
/// Splits the answer of the model into text and file edits. Knows nothing about the SDK and the network.
/// A block is <c>&lt;&lt;&lt;FILE /path</c> on its own line, the new content, and <c>&gt;&gt;&gt;FILE</c> on its own line.
/// </summary>
public static class ResponseParser
{
    public const int MaxEdits = 5;
    public const string BlockStart = "<<<FILE";
    public const string BlockEnd = ">>>FILE";

    public static ParsedResponse Parse(string response)
    {
        var lines = response.Replace("\r\n", "\n").Split('\n');
        var text = new List<string>();
        var edits = new List<FileEdit>();
        var warnings = new List<string>();
        var skippedByLimit = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var header = lines[i].TrimEnd();
            if (!IsBlockStart(header))
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
            else if (edits.Count >= MaxEdits)
                skippedByLimit++;
            else
                edits.Add(new FileEdit(path, JoinContent(content)));
        }

        if (skippedByLimit > 0)
            warnings.Add($"лишних правок: {skippedByLimit} (за один ответ принимается не больше {MaxEdits})");

        return new ParsedResponse(string.Join("\n", text).Trim(), edits, warnings);
    }

    private static bool IsBlockStart(string line) =>
        line.StartsWith(BlockStart, StringComparison.Ordinal)
        && (line.Length == BlockStart.Length || char.IsWhiteSpace(line[BlockStart.Length]));

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
