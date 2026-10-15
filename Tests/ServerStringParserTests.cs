using FileManagerClient.Models;
using FileManagerClient.Services;

namespace Tests;

public class ServerStringParserTests
{
    private static ParsedServer P(string text) => ServerStringParser.Parse(text) ?? throw new Xunit.Sdk.XunitException("не распознано: " + text);

    [Theory]
    [InlineData("ssh root@1.2.3.4", "1.2.3.4", 22, "root", null)]
    [InlineData("ssh root@1.2.3.4 password", "1.2.3.4", 22, "root", "password")]
    [InlineData("ssh -p 2222 user@host.example", "host.example", 2222, "user", null)]
    [InlineData("ssh user@host.example -p 2222", "host.example", 2222, "user", null)]
    [InlineData("ssh -l user host.example", "host.example", 22, "user", null)]
    [InlineData("ssh user@host.example:2222", "host.example", 2222, "user", null)]
    [InlineData("user@host.example", "host.example", 22, "user", null)]
    [InlineData("user@host.example:22", "host.example", 22, "user", null)]
    [InlineData("user:secret@host.example", "host.example", 22, "user", "secret")]
    [InlineData("root:p@ss:w0rd@1.2.3.4", "1.2.3.4", 22, "root", "p@ss:w0rd")]
    [InlineData("sshpass -p 'se cret' ssh user@host.example", "host.example", 22, "user", "se cret")]
    [InlineData("ssh root@1.2.3.4 'p@ss:w0rd with space'", "1.2.3.4", 22, "root", "p@ss:w0rd with space")]
    [InlineData("root@1.2.3.4 \"abc 123\"", "1.2.3.4", 22, "root", "abc 123")]
    [InlineData("ssh -o Port=2200 -o User=bob host.example", "host.example", 2200, "bob", null)]
    public void Ssh_forms(string text, string host, int port, string? user, string? password)
    {
        var p = P(text);
        Assert.Equal(Protocol.Sftp, p.Protocol);
        Assert.Equal(host, p.Host);
        Assert.Equal(port, p.Port);
        Assert.Equal(user, p.User);
        Assert.Equal(password, p.Password);
    }

    [Fact]
    public void Identity_file()
    {
        var p = P("ssh -i ~/.ssh/id_ed25519 deploy@prod.example.com");
        Assert.Equal("~/.ssh/id_ed25519", p.KeyPath);
        Assert.Equal("deploy", p.User);
        Assert.Null(p.Password);
    }

    [Theory]
    [InlineData("1.2.3.4", "1.2.3.4", 22)]
    [InlineData("1.2.3.4:2200", "1.2.3.4", 2200)]
    [InlineData("example.com:2222", "example.com", 2222)]
    [InlineData("[::1]:22", "::1", 22)]
    [InlineData("[2001:db8::1]:2200", "2001:db8::1", 2200)]
    [InlineData("::1", "::1", 22)]
    [InlineData("root@[::1]:2022", "::1", 2022)]
    public void Bare_hosts(string text, string host, int port)
    {
        var p = P(text);
        Assert.Equal(host, p.Host);
        Assert.Equal(port, p.Port);
        Assert.Equal(Protocol.Sftp, p.Protocol);
    }

    [Fact]
    public void Uris()
    {
        var s = P("sftp://user:p%40ss%3Aw0rd@host.example:2222/var/www");
        Assert.Equal((Protocol.Sftp, "host.example", 2222, "user", "p@ss:w0rd", "/var/www"), (s.Protocol, s.Host, s.Port, s.User, s.Password, s.Path));

        var f = P("ftp://bob:pw@ftp.example.com:21/pub");
        Assert.Equal((Protocol.Ftp, 21, "bob", "pw", "/pub"), (f.Protocol, f.Port, f.User, f.Password, f.Path));

        Assert.Equal(Protocol.Ftp, P("ftps://ftp.example.com").Protocol);
        Assert.Equal(21, P("ftps://ftp.example.com").Port);
        Assert.Equal(2200, P("ssh://u@h.example:2200").Port);
        Assert.Equal("::1", P("sftp://u@[::1]:22/x").Host);
    }

    [Fact]
    public void Scp_and_rsync_paths()
    {
        var p = P("user@host.example:/var/www");
        Assert.Equal("/var/www", p.Path);
        Assert.Equal(22, p.Port);

        var s = P("scp -P 2222 ./a.txt user@host.example:/tmp/");
        Assert.Equal((2222, "user", "/tmp/"), (s.Port, s.User, s.Path));

        var r = P("rsync -avz -e 'ssh -p 2200 -i ~/.ssh/k' ./site/ deploy@h.example:/srv/site");
        Assert.Equal((2200, "~/.ssh/k", "deploy", "/srv/site"), (r.Port, r.KeyPath, r.User, r.Path));
    }

    [Fact]
    public void Ftp_command()
    {
        var p = P("ftp ftp.example.com 2121");
        Assert.Equal((Protocol.Ftp, 2121), (p.Protocol, p.Port));
    }

    [Fact]
    public void Hosting_email_russian_with_crlf()
    {
        var p = P("Ваш сервер готов!\r\nIP-адрес: 1.2.3.4\r\nЛогин: root\r\nПароль: Abc:123@x\r\nПорт SSH: 2200\r\n");
        Assert.Equal((Protocol.Sftp, "1.2.3.4", 2200, "root", "Abc:123@x"), (p.Protocol, p.Host, p.Port, p.User, p.Password));
    }

    [Fact]
    public void Hosting_email_english_any_order_and_dash()
    {
        var p = P("Password - hunter2\nUser: admin\nServer: srv.example.org\nPort: 2022");
        Assert.Equal(("srv.example.org", 2022, "admin", "hunter2"), (p.Host, p.Port, p.User, p.Password));
    }

    [Fact]
    public void Hosting_email_ftp()
    {
        var p = P("FTP-сервер: ftp.example.ru\nПользователь: u123\nПароль: pa ss\nПорт: 21");
        Assert.Equal((Protocol.Ftp, "ftp.example.ru", 21, "u123", "pa ss"), (p.Protocol, p.Host, p.Port, p.User, p.Password));
    }

    [Fact]
    public void Hosting_email_sftp_mentioned()
    {
        var p = P("Подключение по SFTP\nХост: files.example.com\nLogin: me\nPass: x1");
        Assert.Equal(Protocol.Sftp, p.Protocol);
        Assert.Equal("files.example.com", p.Host);
    }

    [Fact]
    public void Ssh_config_block()
    {
        var p = P("Host prod\n  HostName 10.1.2.3\n  User deploy\n  Port 2200\n  IdentityFile ~/.ssh/prod_key\n");
        Assert.Equal(("prod", "10.1.2.3", 2200, "deploy", "~/.ssh/prod_key"), (p.Name, p.Host, p.Port, p.User, p.KeyPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("Заметка про сервер")]
    [InlineData("12:30")]
    [InlineData("ssh root@host:99999")]
    [InlineData("just_a_word")]
    [InlineData("Host: example.com")]
    public void Not_a_server(string text) => Assert.Null(ServerStringParser.Parse(text));

    [Fact]
    public void Confidence_and_field_count()
    {
        Assert.False(P("report.pdf").IsConfident);
        Assert.True(P("1.2.3.4").IsConfident);
        Assert.True(P("root@example.com").IsConfident);
        Assert.Equal(1, P("example.com").FieldCount);
    }
}
