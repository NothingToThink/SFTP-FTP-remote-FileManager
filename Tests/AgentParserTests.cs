using VolcanoBoys.Agent;

namespace Tests;

public class AgentParserTests
{
    [Fact]
    public void Plain_Answer_Has_No_Edits()
    {
        var parsed = ResponseParser.Parse("Просто ответ.\nВторая строка.");

        Assert.Equal("Просто ответ.\nВторая строка.", parsed.Text);
        Assert.Empty(parsed.Edits);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void One_Block_Is_An_Edit_With_The_Whole_Content()
    {
        var parsed = ResponseParser.Parse("<<<FILE /home/a.txt\nline 1\nline 2\n>>>FILE");

        var edit = Assert.Single(parsed.Edits);
        Assert.Equal("/home/a.txt", edit.Path);
        Assert.Equal("line 1\nline 2\n", edit.Content);
        Assert.Equal("", parsed.Text);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void Several_Blocks_With_Text_Around_Them()
    {
        var parsed = ResponseParser.Parse("""
            Исправил два файла.
            <<<FILE /a.txt
            A
            >>>FILE
            А ещё вот этот:
            <<<FILE /dir/b.txt
            B
            >>>FILE
            Готово.
            """.Replace("\r\n", "\n"));

        Assert.Equal(["/a.txt", "/dir/b.txt"], parsed.Edits.Select(e => e.Path));
        Assert.Equal(["A\n", "B\n"], parsed.Edits.Select(e => e.Content));
        Assert.Equal("Исправил два файла.\nА ещё вот этот:\nГотово.", parsed.Text);
    }

    [Fact]
    public void Windows_Line_Breaks_And_An_Empty_Block_Are_Handled()
    {
        var parsed = ResponseParser.Parse("<<<FILE /empty.txt\r\n>>>FILE\r\n<<<FILE /x.txt\r\nx\r\n>>>FILE\r\n");

        Assert.Equal(["", "x\n"], parsed.Edits.Select(e => e.Content));
    }

    [Fact]
    public void Unclosed_Block_Is_Ignored_With_A_Warning_And_Earlier_Edits_Stay()
    {
        var parsed = ResponseParser.Parse("<<<FILE /ok.txt\nok\n>>>FILE\nтекст\n<<<FILE /broken.txt\nне закрыт");

        Assert.Equal("/ok.txt", Assert.Single(parsed.Edits).Path);
        Assert.Equal("текст", parsed.Text);
        Assert.Contains("/broken.txt", Assert.Single(parsed.Warnings));
    }

    [Theory]
    [InlineData("<<<FILE \nx\n>>>FILE", "пустой")]
    [InlineData("<<<FILE\nx\n>>>FILE", "пустой")]
    [InlineData("<<<FILE relative/a.txt\nx\n>>>FILE", "не абсолютный")]
    [InlineData("<<<FILE a.txt\nx\n>>>FILE", "не абсолютный")]
    [InlineData("<<<FILE /a/../etc/x\nx\n>>>FILE", "«..»")]
    [InlineData("<<<FILE /a/\nx\n>>>FILE", "папку")]
    [InlineData("<<<FILE C:\\a.txt\nx\n>>>FILE", "не абсолютный")]
    public void Bad_Path_Makes_The_Block_Ignored_With_A_Warning(string response, string reason)
    {
        var parsed = ResponseParser.Parse(response);

        Assert.Empty(parsed.Edits);
        Assert.Contains(reason, Assert.Single(parsed.Warnings));
    }

    [Fact]
    public void More_Than_Five_Edits_Keep_The_First_Five_And_Warn()
    {
        var response = string.Concat(Enumerable.Range(1, 7).Select(i => $"<<<FILE /f{i}.txt\n{i}\n>>>FILE\n"));

        var parsed = ResponseParser.Parse(response);

        Assert.Equal(ResponseParser.MaxEdits, parsed.Edits.Count);
        Assert.Equal("/f5.txt", parsed.Edits[^1].Path);
        Assert.Contains("2", Assert.Single(parsed.Warnings));
    }

    [Fact]
    public void A_Header_Inside_A_Block_Is_Content()
    {
        var parsed = ResponseParser.Parse("<<<FILE /a.md\n<<<FILE /not/a/header\n>>>FILE");

        Assert.Equal("<<<FILE /not/a/header\n", Assert.Single(parsed.Edits).Content);
    }
}
