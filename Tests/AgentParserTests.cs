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

        Assert.Equal(ResponseParser.MaxActions, parsed.Edits.Count);
        Assert.Equal("/f5.txt", parsed.Edits[^1].Path);
        Assert.Contains("2", Assert.Single(parsed.Warnings));
    }

    [Fact]
    public void A_Header_Inside_A_Block_Is_Content()
    {
        var parsed = ResponseParser.Parse("<<<FILE /a.md\n<<<FILE /not/a/header\n>>>FILE");

        Assert.Equal("<<<FILE /not/a/header\n", Assert.Single(parsed.Edits).Content);
    }

    [Fact]
    public void One_Delete_Is_A_Deletion_And_Not_Text()
    {
        var parsed = ResponseParser.Parse("Удаляю.\n<<<DELETE /home/old file.txt>>>");

        Assert.Equal("/home/old file.txt", Assert.Single(parsed.Deletes).Path);
        Assert.Empty(parsed.Edits);
        Assert.Empty(parsed.Warnings);
        Assert.Equal("Удаляю.", parsed.Text);
    }

    [Fact]
    public void Several_Deletes_Keep_The_Order_Of_Appearance()
    {
        var parsed = ResponseParser.Parse("<<<DELETE /b>>>\r\n<<<DELETE   /a.txt >>>  \r\n<<<DELETE /c/d>>>");

        Assert.Equal(["/b", "/a.txt", "/c/d"], parsed.Deletes.Select(d => d.Path));
        Assert.Equal("", parsed.Text);
    }

    [Fact]
    public void Delete_Together_With_File_Gives_Both()
    {
        var parsed = ResponseParser.Parse("<<<FILE /a.txt\nA\n>>>FILE\nи ещё\n<<<DELETE /b.txt>>>");

        Assert.Equal("/a.txt", Assert.Single(parsed.Edits).Path);
        Assert.Equal("/b.txt", Assert.Single(parsed.Deletes).Path);
        Assert.Equal("и ещё", parsed.Text);
        Assert.Empty(parsed.Warnings);
    }

    [Fact]
    public void Delete_Inside_A_File_Block_Is_Content()
    {
        var parsed = ResponseParser.Parse("<<<FILE /a.md\nтекст\n<<<DELETE /etc/passwd>>>\n>>>FILE");

        Assert.Empty(parsed.Deletes);
        Assert.Equal("текст\n<<<DELETE /etc/passwd>>>\n", Assert.Single(parsed.Edits).Content);
    }

    [Theory]
    [InlineData("<<<DELETE >>>", "пустой")]
    [InlineData("<<<DELETE>>>", "пустой")]
    [InlineData("<<<DELETE relative/a.txt>>>", "не абсолютный")]
    [InlineData("<<<DELETE /a/../b>>>", "«..»")]
    [InlineData("<<<DELETE /a/./b>>>", "«.»")]
    [InlineData("<<<DELETE ..>>>", "не абсолютный")]
    [InlineData("<<<DELETE />>>", "папку")]
    [InlineData("<<<DELETE ///>>>", "папку")]
    [InlineData("<<<DELETE /a\\b>>>", "недопустимые")]
    [InlineData("<<<DELETE /a\tb>>>", "недопустимые")]
    [InlineData("<<<DELETE /dir/>>>", "папку")]
    public void Bad_Delete_Path_Is_Ignored_With_A_Warning(string response, string reason)
    {
        var parsed = ResponseParser.Parse(response);

        Assert.Empty(parsed.Deletes);
        Assert.Contains(reason, Assert.Single(parsed.Warnings));
    }

    [Fact]
    public void Delete_Line_Without_A_Closing_Marker_Is_Ignored_With_A_Warning()
    {
        var parsed = ResponseParser.Parse("<<<DELETE /a.txt\nтекст");

        Assert.Empty(parsed.Deletes);
        Assert.Equal("текст", parsed.Text);
        Assert.Contains("/a.txt", Assert.Single(parsed.Warnings));
    }

    [Fact]
    public void The_Same_Path_In_File_And_Delete_Ignores_Both_Blocks()
    {
        var parsed = ResponseParser.Parse(
            "<<<FILE /a.txt\nA\n>>>FILE\n<<<FILE /keep.txt\nK\n>>>FILE\n<<<DELETE /a.txt>>>\n<<<DELETE /other.txt>>>");

        Assert.Equal("/keep.txt", Assert.Single(parsed.Edits).Path);
        Assert.Equal("/other.txt", Assert.Single(parsed.Deletes).Path);
        var warning = Assert.Single(parsed.Warnings);
        Assert.Contains("/a.txt", warning);
        Assert.Contains("оба", warning);
    }

    [Fact]
    public void The_Limit_Of_Five_Counts_File_And_Delete_Together()
    {
        var response = "<<<FILE /f1\n1\n>>>FILE\n<<<DELETE /d1>>>\n<<<FILE /f2\n2\n>>>FILE\n<<<DELETE /d2>>>\n<<<DELETE /d3>>>\n"
            + "<<<FILE /f3\n3\n>>>FILE\n<<<DELETE /d4>>>";

        var parsed = ResponseParser.Parse(response);

        Assert.Equal(ResponseParser.MaxActions, parsed.Edits.Count + parsed.Deletes.Count);
        Assert.Equal(["/f1", "/f2"], parsed.Edits.Select(e => e.Path));
        Assert.Equal(["/d1", "/d2", "/d3"], parsed.Deletes.Select(d => d.Path));
        Assert.Contains("лишних действий: 2", Assert.Single(parsed.Warnings));
    }

    [Fact]
    public void More_Than_Five_Deletes_Keep_The_First_Five_And_Warn()
    {
        var parsed = ResponseParser.Parse(string.Join('\n', Enumerable.Range(1, 8).Select(i => $"<<<DELETE /f{i}>>>")));

        Assert.Equal(5, parsed.Deletes.Count);
        Assert.Equal("/f5", parsed.Deletes[^1].Path);
        Assert.Contains("лишних действий: 3", Assert.Single(parsed.Warnings));
    }
}
