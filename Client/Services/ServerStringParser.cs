using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using FileManagerClient.Models;

namespace FileManagerClient.Services;

/// <summary>Сервер, разобранный из строки или письма хостера. Порт всегда заполнен (явный или по умолчанию).</summary>
public sealed record ParsedServer(
    Protocol Protocol,
    string Host,
    int? Port = null,
    string? User = null,
    string? Password = null,
    string? KeyPath = null,
    string? Path = null,
    string? Name = null)
{
    /// <summary>Порт был указан в тексте (а не подставлен по умолчанию).</summary>
    public bool PortExplicit { get; init; }

    /// <summary>Протокол назван явно: схема, команда ssh/ftp или слово SFTP/FTP.</summary>
    public bool ProtocolExplicit { get; init; }

    /// <summary>Сколько полей удалось извлечь; адрес сам по себе — одно поле.</summary>
    public int FieldCount =>
        1 + (PortExplicit ? 1 : 0) + (User is not null ? 1 : 0) + (Password is not null ? 1 : 0)
        + (KeyPath is not null ? 1 : 0) + (ProtocolExplicit ? 1 : 0);

    /// <summary>
    /// Достаточно уверенное совпадение, чтобы реагировать на Ctrl+V вне полей ввода:
    /// не меньше двух полей либо голый IP-адрес (иначе любое «report.pdf» сошло бы за хост).
    /// </summary>
    public bool IsConfident => FieldCount >= 2 || IPAddress.TryParse(Host, out _);
}

/// <summary>
/// Разбирает строку из буфера обмена в параметры подключения: команды ssh/sftp/scp/rsync/sshpass,
/// URI sftp:// ssh:// ftp:// ftps://, user:pass@host:port, письма хостеров (RU/EN) и блоки ~/.ssh/config.
/// Чистая функция: ничего не читает и не пишет, пароли не логирует.
/// </summary>
public static class ServerStringParser
{
    private const int MaxLength = 4000;

    private static readonly Regex HostName = new(
        @"^[A-Za-z0-9]([A-Za-z0-9_-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9_-]{0,61}[A-Za-z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DottedDigits = new(@"^\d+(\.\d+)*$", RegexOptions.Compiled);
    private static readonly Regex ConfigHost = new(@"^\s*Host\s+(\S+)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ConfigLine = new(@"^\s*(HostName|User|Port|IdentityFile)(?:\s*=\s*|\s+)(.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FtpWord = new(@"(?<![a-zа-я0-9])ftps?(?![a-zа-я0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SftpWord = new(@"(?<![a-z0-9])(sftp|ssh)(?![a-z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Опции ssh/sftp/scp, у которых есть аргумент (для rsync/ftp набор подправляется ниже).
    private const string ArgOptions = "pPilFJLRDbcEemSWwBOQIoux";

    public static ParsedServer? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim().Trim('﻿');
        if (text.Length > MaxLength)
            return null;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (lines.Length == 0)
            return null;
        if (lines.Length == 1)
            return ParseLine(lines[0]);

        return ParseConfig(lines) ?? ParseLabelled(lines);
    }

    // ------------------------------------------------------------ ~/.ssh/config

    private static ParsedServer? ParseConfig(string[] lines)
    {
        string? alias = null, hostName = null, user = null, key = null;
        string? port = null;
        foreach (var line in lines)
        {
            var m = ConfigHost.Match(line);
            if (m.Success)
            {
                if (alias is not null)
                    break; // только первый блок
                if (m.Groups[1].Value.Contains('*') || m.Groups[1].Value.Contains('?'))
                    continue;
                alias = m.Groups[1].Value;
                continue;
            }
            if (alias is null)
                continue;
            var c = ConfigLine.Match(line);
            if (!c.Success)
                continue;
            var value = Unquote(c.Groups[2].Value);
            switch (c.Groups[1].Value.ToLowerInvariant())
            {
                case "hostname": hostName = value; break;
                case "user": user = value; break;
                case "port": port = value; break;
                case "identityfile": key = value; break;
            }
        }

        if (alias is null || (hostName is null && user is null && key is null))
            return null;
        var host = hostName ?? alias;
        if (!IsValidHost(host))
            return null;
        int? p = null;
        if (port is not null)
        {
            p = ParsePort(port);
            if (p is null)
                return null;
        }
        return Make(Protocol.Sftp, true, host, p, user, null, key, null, alias);
    }

    // ------------------------------------------------------------ письма хостеров

    private static ParsedServer? ParseLabelled(string[] lines)
    {
        string? host = null, user = null, password = null, hostLabel = null;
        var ports = new List<(string Label, string Value)>();
        ParsedServer? baseline = null;
        var hasUri = false;

        foreach (var line in lines)
        {
            var clean = line.TrimStart('-', '*', '•', '·', '–', '—', ' ', '\t').Trim();
            if (!TrySplitLabel(clean, out var label, out var value))
            {
                if (baseline is null && LooksLikeCommandOrUri(clean))
                {
                    baseline = ParseLine(clean);
                    hasUri = baseline is not null;
                }
                continue;
            }

            var kind = Classify(label);
            value = Unquote(value.Trim());
            if (value.Length == 0)
                continue;
            switch (kind)
            {
                case 'p':
                    password ??= value;
                    break;
                case 'u':
                    user ??= value;
                    break;
                case 'o':
                    ports.Add((label, value));
                    break;
                case 'h':
                    if (host is null)
                    {
                        host = value;
                        hostLabel = label;
                    }
                    break;
            }
        }

        // Хост из подписи может быть «1.2.3.4», «1.2.3.4:22», URI или «ssh root@1.2.3.4»
        int? hostPort = null;
        string? uriUser = null, uriPass = null, path = null;
        var protocol = (Protocol?)null;
        if (host is not null)
        {
            var first = host.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            ParsedServer? fromValue = host.Contains("://") || LooksLikeCommandOrUri(host) ? ParseLine(host) : null;
            if (fromValue is not null)
            {
                host = fromValue.Host;
                hostPort = fromValue.PortExplicit ? fromValue.Port : null;
                uriUser = fromValue.User;
                uriPass = fromValue.Password;
                path = fromValue.Path;
                protocol = fromValue.ProtocolExplicit ? fromValue.Protocol : null;
            }
            else if (TrySplitHostPort(first, out var h, out var hp) && IsValidHost(h))
            {
                host = h;
                hostPort = hp;
            }
            else
            {
                host = null;
            }
        }

        if (host is null && baseline is not null)
        {
            host = baseline.Host;
            hostPort = baseline.PortExplicit ? baseline.Port : null;
            uriUser = baseline.User;
            uriPass = baseline.Password;
            path = baseline.Path;
            protocol = baseline.ProtocolExplicit ? baseline.Protocol : null;
            _ = hasUri;
        }
        if (host is null)
            return null;

        user ??= uriUser;
        password ??= uriPass;
        if (user is null && password is null && hostPort is null && baseline is null)
            return null; // один «Хост:» без остального — не письмо хостера

        // Протокол: схема/команда > слово в подписи хоста > упоминание в тексте
        var whole = string.Join('\n', lines);
        var hostIsFtp = hostLabel is not null && FtpWord.IsMatch(hostLabel) && !SftpWord.IsMatch(hostLabel);
        var explicitProto = true;
        if (protocol is null)
        {
            if (hostIsFtp) protocol = Protocol.Ftp;
            else if (SftpWord.IsMatch(whole)) protocol = Protocol.Sftp;
            else if (FtpWord.IsMatch(whole)) protocol = Protocol.Ftp;
            else explicitProto = false;
        }

        // Порт: подпись с ftp/ssh должна совпадать с протоколом, иначе первый
        int? port = hostPort;
        if (port is null && ports.Count > 0)
        {
            var want = protocol == Protocol.Ftp ? "ftp" : "ssh";
            var pick = ports.FirstOrDefault(x => x.Label.Contains(want, StringComparison.OrdinalIgnoreCase));
            if (pick.Value is null)
                pick = ports.FirstOrDefault(x => !x.Label.Contains("ftp", StringComparison.OrdinalIgnoreCase)
                                                 && !x.Label.Contains("ssh", StringComparison.OrdinalIgnoreCase));
            if (pick.Value is null)
                pick = ports[0];
            port = ParsePort(pick.Value);
        }

        protocol ??= port == 21 ? Protocol.Ftp : Protocol.Sftp;
        return Make(protocol.Value, explicitProto, host, port, user, password, null, path, null);
    }

    /// <summary>Делит строку «Метка: значение» / «Метка - значение» по первому разделителю.</summary>
    private static bool TrySplitLabel(string line, out string label, out string value)
    {
        label = value = string.Empty;
        var idx = -1;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c is ':' or '：' or '=')
            {
                idx = i;
                break;
            }
            if (c is '-' or '–' or '—' && i > 0 && i + 1 < line.Length && line[i - 1] == ' ' && line[i + 1] == ' ')
            {
                idx = i;
                break;
            }
        }
        if (idx <= 0 || idx >= line.Length - 1)
            return false;
        label = line[..idx].Trim();
        value = line[(idx + 1)..].Trim();
        return label.Length is > 0 and <= 40 && label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4;
    }

    private static char Classify(string label)
    {
        var l = label.ToLowerInvariant().Replace("-", " ").Replace("_", " ");
        if (l.Contains("порт") || Regex.IsMatch(l, @"\bport\b")) return 'o';
        if (l.Contains("парол") || l.Contains("pass") || l.Contains("pwd")) return 'p';
        if (l.Contains("логин") || l.Contains("login") || l.Contains("user") || l.Contains("пользоват")
            || l.Contains("имя")) return 'u';
        if (Regex.IsMatch(l, @"\bip\b") || l.Contains("host") || l.Contains("хост") || l.Contains("сервер")
            || l.Contains("server") || l.Contains("адрес") || l.Contains("address")) return 'h';
        return ' ';
    }

    // ------------------------------------------------------------ одна строка

    private static bool LooksLikeCommandOrUri(string line)
    {
        if (line.Contains("://"))
            return true;
        var first = line.Split((char[]?)null, 2)[0].ToLowerInvariant();
        return first is "ssh" or "sftp" or "scp" or "rsync" or "sshpass" or "ftp" or "lftp";
    }

    private static ParsedServer? ParseLine(string line)
    {
        var tokens = Tokenize(line);
        if (tokens.Count == 0)
            return null;

        string? sshpass = null;
        if (tokens[0].Equals("sshpass", StringComparison.OrdinalIgnoreCase))
        {
            var i = 1;
            while (i < tokens.Count && tokens[i].StartsWith('-'))
            {
                var t = tokens[i];
                if (t == "-p" && i + 1 < tokens.Count) { sshpass = tokens[i + 1]; i += 2; continue; }
                if (t.StartsWith("-p") && t.Length > 2) { sshpass = t[2..]; i++; continue; }
                if (t is "-f" or "-d" or "-P" && i + 1 < tokens.Count) { i += 2; continue; }
                i++;
            }
            tokens = tokens.Skip(i).ToList();
            if (tokens.Count == 0)
                return null;
        }

        var cmd = tokens[0].ToLowerInvariant();
        var isCommand = cmd is "ssh" or "sftp" or "scp" or "rsync" or "ftp" or "lftp";
        var args = isCommand ? tokens.Skip(1).ToList() : tokens;
        if (!isCommand)
            cmd = "";
        else if (sshpass is not null && cmd is "ftp" or "lftp")
            return null;

        var opts = new Opts();
        var positional = new List<string>();
        ScanArgs(cmd, args, opts, positional);

        // Адрес назначения
        var scpLike = cmd is "scp" or "rsync";
        string? dest = null;
        if (scpLike)
            dest = positional.FirstOrDefault(IsRemoteSpec);
        else if (positional.Count > 0)
            dest = positional[0];
        if (dest is null)
            return null;
        var extra = scpLike ? new List<string>() : positional.Skip(1).ToList();

        var t2 = ParseTarget(dest, isCommand, scpLike);
        if (t2 is null)
            return null;

        // ftp host [port]
        if (cmd is "ftp" or "lftp" && extra.Count == 1 && t2.Port is null && ParsePort(extra[0]) is { } fp)
        {
            t2 = t2 with { Port = fp };
            extra.Clear();
        }

        var user = t2.User ?? opts.User;
        var password = t2.Password ?? sshpass ?? opts.Password;
        // «ssh root@host пароль»: единственный лишний аргумент — пароль
        if (password is null && extra.Count == 1 && (isCommand || t2.User is not null || t2.Uri))
            password = extra[0];

        var port = opts.Port ?? t2.Port;
        if (opts.PortInvalid)
            return null;

        Protocol? proto = t2.Protocol;
        var explicitProto = proto is not null;
        if (proto is null && isCommand)
        {
            proto = cmd is "ftp" or "lftp" ? Protocol.Ftp : Protocol.Sftp;
            explicitProto = true;
        }
        proto ??= port == 21 ? Protocol.Ftp : Protocol.Sftp;

        return Make(proto.Value, explicitProto, t2.Host, port, user, password, opts.Key, t2.Path, null);
    }

    private sealed class Opts
    {
        public int? Port;
        public bool PortInvalid;
        public string? User, Key, Password;
    }

    private static void ScanArgs(string cmd, List<string> args, Opts o, List<string> positional)
    {
        var argOpts = ArgOptions;
        // у rsync/scp/sftp -p без аргумента (сохранить права), а порт — -P (кроме ssh)
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--")
            {
                positional.AddRange(args.Skip(i + 1));
                return;
            }
            if (!a.StartsWith('-') || a.Length == 1 || a.StartsWith("--"))
            {
                if (cmd == "rsync" && a.StartsWith("--"))
                    continue;
                if (a.StartsWith("--"))
                    continue;
                positional.Add(a);
                continue;
            }

            var letter = a[1];
            var isArgOpt = argOpts.Contains(letter);
            if (cmd is "scp" or "sftp" or "rsync" && letter == 'p')
                isArgOpt = false;
            if (cmd == "ssh" && letter == 'P')
                isArgOpt = false;
            if (cmd == "rsync" && letter != 'e')
                isArgOpt = false;
            if (cmd is "ftp" or "lftp")
                isArgOpt = letter == 'u' || letter == 'p';
            if (!isArgOpt)
                continue;

            string value;
            if (a.Length > 2)
                value = a[2..];
            else if (i + 1 < args.Count)
                value = args[++i];
            else
                continue;

            switch (letter)
            {
                case 'p' or 'P':
                    if (cmd is "ftp" or "lftp" && letter == 'p')
                    {
                        // lftp -p port
                    }
                    var p = ParsePort(value);
                    if (p is null) o.PortInvalid = true; else o.Port = p;
                    break;
                case 'i':
                    o.Key = value;
                    break;
                case 'l':
                    o.User = value;
                    break;
                case 'u' when cmd is "ftp" or "lftp":
                    var comma = value.IndexOf(',');
                    if (comma >= 0) { o.User = value[..comma]; o.Password = value[(comma + 1)..]; }
                    else o.User = value;
                    break;
                case 'o':
                    ApplyOption(value, o);
                    break;
                case 'e' when cmd == "rsync":
                    var inner = Tokenize(value);
                    if (inner.Count > 0)
                        ScanArgs("ssh", inner.Skip(1).ToList(), o, new List<string>());
                    break;
            }
        }
    }

    private static void ApplyOption(string value, Opts o)
    {
        var m = Regex.Match(value, @"^\s*(\w+)\s*(?:=|\s)\s*(.+?)\s*$");
        if (!m.Success)
            return;
        var v = Unquote(m.Groups[2].Value);
        switch (m.Groups[1].Value.ToLowerInvariant())
        {
            case "port":
                var p = ParsePort(v);
                if (p is null) o.PortInvalid = true; else o.Port = p;
                break;
            case "user": o.User = v; break;
            case "identityfile": o.Key = v; break;
        }
    }

    /// <summary>Похоже на удалённый адрес scp/rsync: есть user@ или host:путь (но не диск Windows).</summary>
    private static bool IsRemoteSpec(string s)
    {
        if (s.Contains("://"))
            return true;
        if (Regex.IsMatch(s, @"^[A-Za-z]:[\\/]"))
            return false;
        return s.Contains('@') || (s.Contains(':') && !s.StartsWith('.') && !s.StartsWith('/'));
    }

    // ------------------------------------------------------------ адрес

    private sealed record Target(string Host, int? Port, string? User, string? Password, string? Path, Protocol? Protocol, bool Uri);

    private static Target? ParseTarget(string s, bool commandContext, bool allowPathAfterColon)
    {
        Protocol? scheme = null;
        var isUri = false;
        var rest = s;
        var sep = s.IndexOf("://", StringComparison.Ordinal);
        if (sep > 0)
        {
            var name = s[..sep].ToLowerInvariant();
            scheme = name switch
            {
                "sftp" or "ssh" => Protocol.Sftp,
                "ftp" or "ftps" or "ftpes" => Protocol.Ftp,
                _ => null,
            };
            if (scheme is null)
                return null;
            isUri = true;
            rest = s[(sep + 3)..];
        }

        string? path = null;
        string authority;
        if (isUri)
        {
            var slash = rest.IndexOf('/');
            // «/» может быть частью неэкранированного пароля, но URI по RFC экранирует его
            authority = slash >= 0 ? rest[..slash] : rest;
            path = slash >= 0 ? rest[slash..] : null;
        }
        else
        {
            authority = rest;
        }

        string? user = null, pass = null;
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            var info = authority[..at];
            authority = authority[(at + 1)..];
            var colon = info.IndexOf(':');
            if (colon >= 0)
            {
                user = info[..colon];
                pass = info[(colon + 1)..];
            }
            else
            {
                user = info;
            }
            if (isUri)
            {
                user = Uri.UnescapeDataString(user);
                if (pass is not null) pass = Uri.UnescapeDataString(pass);
            }
            if (user.Length == 0)
                user = null;
            if (user is null && pass is null && at == 0)
                return null;
        }

        // host[:port|:path], [v6][:port], голый IPv6
        string host;
        int? port = null;
        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            if (close < 0)
                return null;
            host = authority[1..close];
            var tail = authority[(close + 1)..];
            if (tail.Length > 0)
            {
                if (tail[0] != ':')
                    return null;
                if (!TrySplitPortPath(tail[1..], isUri, allowPathAfterColon, out port, out var pp))
                    return null;
                path ??= pp;
            }
        }
        else if (IPAddress.TryParse(authority, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            host = authority;
        }
        else
        {
            var colon = authority.IndexOf(':');
            if (colon < 0)
            {
                host = authority;
            }
            else
            {
                host = authority[..colon];
                if (!TrySplitPortPath(authority[(colon + 1)..], isUri, allowPathAfterColon, out port, out var pp))
                    return null;
                path ??= pp;
            }
        }

        if (!IsValidHost(host))
            return null;
        // «слово» без точки принимаем только если есть явный контекст: логин, схема, команда или порт
        var isIp = IPAddress.TryParse(host, out _);
        if (!isIp && !host.Contains('.') && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && user is null && !isUri && !commandContext && port is null)
            return null;

        if (path is not null)
        {
            var q = path.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) path = path[..q];
            if (path.Length <= 1 && path != "~")
                path = null;
            else if (isUri)
                path = Uri.UnescapeDataString(path);
        }
        return new Target(host, port, user, pass, path, scheme, isUri);
    }

    /// <summary>После «:» идёт порт («22», «22/путь») или, в стиле scp, путь («/var/www», «~/x»).</summary>
    private static bool TrySplitPortPath(string tail, bool isUri, bool allowPath, out int? port, out string? path)
    {
        port = null;
        path = null;
        if (tail.Length == 0)
            return true; // «host:» — пустой порт игнорируем
        var slash = tail.IndexOf('/');
        var portPart = slash >= 0 ? tail[..slash] : tail;
        if (portPart.Length > 0 && portPart.All(char.IsAsciiDigit))
        {
            port = ParsePort(portPart);
            if (port is null)
                return false;
            if (slash >= 0)
                path = tail[slash..];
            return true;
        }
        if (!isUri && (tail.StartsWith('/') || tail.StartsWith('~') || allowPath))
        {
            path = tail;
            return true;
        }
        return false;
    }

    private static bool TrySplitHostPort(string s, out string host, out int? port)
    {
        host = s;
        port = null;
        if (s.StartsWith('['))
        {
            var close = s.IndexOf(']');
            if (close < 0) return false;
            host = s[1..close];
            var tail = s[(close + 1)..];
            if (tail.Length == 0) return true;
            if (tail[0] != ':') return false;
            port = ParsePort(tail[1..]);
            return port is not null;
        }
        if (IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
            return true;
        var colon = s.IndexOf(':');
        if (colon < 0) return true;
        host = s[..colon];
        port = ParsePort(s[(colon + 1)..]);
        return port is not null;
    }

    private static bool IsValidHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253)
            return false;
        if (host.Contains(':'))
        {
            var z = host.IndexOf('%');
            return IPAddress.TryParse(z >= 0 ? host[..z] : host, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6;
        }
        if (DottedDigits.IsMatch(host))
        {
            var parts = host.Split('.');
            return parts.Length == 4 && parts.All(p => p.Length <= 3 && int.TryParse(p, out var n) && n <= 255);
        }
        return HostName.IsMatch(host) && !host.All(char.IsAsciiDigit);
    }

    // ------------------------------------------------------------ утилиты

    private static int? ParsePort(string s) =>
        int.TryParse(s.Trim(), out var p) && p is >= 1 and <= 65535 ? p : null;

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'') || (s[0] == '`' && s[^1] == '`')))
            return s[1..^1];
        return s;
    }

    private static ParsedServer Make(Protocol protocol, bool protocolExplicit, string host, int? port, string? user,
        string? password, string? key, string? path, string? name)
    {
        if (host.StartsWith('[') && host.EndsWith(']'))
            host = host[1..^1];
        var def = protocol == Protocol.Ftp ? 21 : 22;
        return new ParsedServer(protocol, host, port ?? def, string.IsNullOrEmpty(user) ? null : user,
            string.IsNullOrEmpty(password) ? null : password, string.IsNullOrEmpty(key) ? null : key, path, name)
        {
            PortExplicit = port is not null,
            ProtocolExplicit = protocolExplicit,
        };
    }

    /// <summary>Разбивка как в shell: пробелы, '…', "…" (с \" и \\), \пробел. Незакрытая кавычка читается до конца.</summary>
    private static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var started = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\'')
            {
                started = true;
                var end = line.IndexOf('\'', i + 1);
                if (end < 0) end = line.Length;
                sb.Append(line, i + 1, end - i - 1);
                i = end;
            }
            else if (c == '"')
            {
                started = true;
                i++;
                for (; i < line.Length && line[i] != '"'; i++)
                {
                    if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\' or '$' or '`')
                        i++;
                    sb.Append(line[i]);
                }
            }
            else if (c == '\\' && i + 1 < line.Length && line[i + 1] is ' ' or '\'' or '"' or '\\')
            {
                started = true;
                sb.Append(line[++i]);
            }
            else if (char.IsWhiteSpace(c))
            {
                if (started)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    started = false;
                }
            }
            else
            {
                started = true;
                sb.Append(c);
            }
        }
        if (started)
            result.Add(sb.ToString());
        return result;
    }
}
