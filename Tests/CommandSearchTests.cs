using FileManagerClient.Commands;

namespace Tests;

public class CommandSearchTests
{
    private static AppCommand Cmd(string id, string title, string category = "Файлы", params string[] keywords) => new()
    {
        Id = id, Title = title, Category = category, Keywords = keywords, Handler = _ => Task.CompletedTask,
    };

    private static CommandRegistry Registry()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("a", "Новая папка", "Файлы", "mkdir"));
        r.Register(Cmd("b", "Новый файл", "Файлы"));
        r.Register(Cmd("c", "Переключить тему", "Приложение", "dark"));
        r.Register(Cmd("d", "Обновить", "Навигация"));
        r.Register(Cmd("e", "Подключиться: prod-01", "Подключение"));
        return r;
    }

    [Fact]
    public void Prefix_of_second_word_matches_and_highlights()
    {
        var res = Registry().Search("пап");
        var first = Assert.Single(res);
        Assert.Equal("a", first.Command.Id);
        Assert.Equal(new[] { 6, 7, 8 }, first.TitleMatches);
    }

    [Fact]
    public void Title_start_beats_substring()
    {
        var r = new CommandRegistry();
        r.Register(Cmd("sub", "Скачать файл"));
        r.Register(Cmd("pre", "Файл свойства"));
        Assert.Equal("pre", r.Search("фай")[0].Command.Id);
    }

    [Fact]
    public void Keywords_and_category_are_searched_and_yo_is_normalized()
    {
        var r = Registry();
        Assert.Equal("a", Assert.Single(r.Search("mkdir")).Command.Id);
        Assert.Equal("c", Assert.Single(r.Search("приложение")).Command.Id);
        Assert.Equal("d", Assert.Single(r.Search("обновить")).Command.Id);
        r.Register(Cmd("y", "Всё скачать"));
        Assert.Contains(r.Search("все"), x => x.Command.Id == "y");
    }

    [Fact]
    public void All_words_must_match()
    {
        Assert.Empty(Registry().Search("нов zzz"));
        var res = Registry().Search("нов");
        Assert.Equal(3, res.Count);                 // «Обновить» совпала подстрокой
        Assert.Equal("d", res[^1].Command.Id);      // но слова с начала заголовка ранжируются выше
    }

    [Fact]
    public void Empty_query_puts_recent_first_and_hides_subresults()
    {
        var r = Registry();
        r.Register(new AppCommand
        {
            Id = "hidden", Title = "Подключиться: x", Category = "x", ShowWhenQueryEmpty = false,
            Handler = _ => Task.CompletedTask,
        });
        r.MarkUsed(r.All().First(c => c.Id == "d"));
        var res = r.Search("");
        Assert.Equal("d", res[0].Command.Id);
        Assert.DoesNotContain(res, x => x.Command.Id == "hidden");
        Assert.Contains(r.Search("подключ"), x => x.Command.Id == "hidden");
    }

    [Fact]
    public void Registry_raises_changed_and_exposes_context_commands_dynamically()
    {
        var r = new CommandRegistry();
        var changes = 0;
        r.Changed += (_, _) => changes++;
        Assert.Empty(r.ForContext(AppCommand.FileContext));
        r.Register(new AppCommand { Id = "p", Title = "Посчитать SHA-256", Category = "Плагин", Context = "file", Handler = _ => Task.CompletedTask });
        Assert.Single(r.ForContext(AppCommand.FileContext));
        Assert.Equal(1, changes);
        r.Unregister("p");
        Assert.Empty(r.ForContext(AppCommand.FileContext));
    }
}
