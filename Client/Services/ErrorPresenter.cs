using System.Net.Http;
using System.Net.Sockets;
using FileManagerClient.Api;

namespace FileManagerClient.Services;

/// <summary>Вид ошибки для UI: от него зависит иллюстрация и набор действий.</summary>
public enum UserErrorKind
{
    Generic,
    Auth,
    Network,
    NotFound,
    AccessDenied,
    BadRequest,
    Server,
    UnknownHostKey,
    HostKeyMismatch,
}

/// <summary>Действие-кнопка под сообщением об ошибке. Первое в списке — основное (Enter).</summary>
public enum UserErrorAction
{
    Retry,
    OpenProfile,
    Close,
}

/// <summary>Данные ключа сервера для диалогов знакомства / подмены.</summary>
public sealed record HostKeyInfo(string Host, int Port, string? Expected, string? Presented);

/// <summary>
/// Человеческая версия ошибки: «что случилось и что делать» (docs/ui-spec.md, «Принципы» п.3).
/// Технические детали (код, сырой текст) — только в <see cref="Details"/>, под «Подробнее».
/// </summary>
public sealed record UserError(
    string Title,
    string Text,
    string? Details,
    UserErrorKind Kind,
    IReadOnlyList<UserErrorAction> Actions,
    HostKeyInfo? HostKey = null)
{
    public static string ActionLabel(UserErrorAction action) => action switch
    {
        UserErrorAction.Retry => "Повторить",
        UserErrorAction.OpenProfile => "Открыть профиль",
        _ => "Закрыть",
    };
}

/// <summary>
/// Контекст, в котором произошла ошибка: от него зависят тексты.
/// </summary>
/// <param name="ServerName">Имя профиля/сервера для текстов («Сервер prod-01 …»).</param>
/// <param name="IsConnecting">Ошибка случилась при установке соединения.</param>
/// <param name="Path">Путь, с которым работали (для 404/403 по файлам).</param>
public sealed record ErrorContext(string? ServerName = null, bool IsConnecting = false, string? Path = null);

/// <summary>
/// Чистая функция «исключение → UserError». Без UI и без обращений к сети: легко тестируется.
/// </summary>
public static class ErrorPresenter
{
    public static UserError Present(Exception exception, ErrorContext? context = null)
    {
        context ??= new ErrorContext();
        var server = string.IsNullOrWhiteSpace(context.ServerName) ? "Сервер" : $"Сервер {context.ServerName}";
        var details = BuildDetails(exception);

        switch (exception)
        {
            case ApiException { IsHostKeyMismatch: true } api:
                return new UserError("Чужой запах!",
                    $"Ключ {HostLabel(api, context)} изменился с прошлого подключения. Так бывает после переустановки сервера, но так же выглядит перехват соединения.",
                    details, UserErrorKind.HostKeyMismatch, [UserErrorAction.Close],
                    new HostKeyInfo(api.Host ?? context.ServerName ?? "", api.Port ?? 0, api.ExpectedFingerprint, api.PresentedFingerprint));

            case ApiException { IsUnknownHostKey: true } api:
                return new UserError("Знакомимся с сервером",
                    $"Свинка видит {HostLabel(api, context)} впервые. Сверьте отпечаток ключа с администратором сервера.",
                    details, UserErrorKind.UnknownHostKey, [UserErrorAction.Close],
                    new HostKeyInfo(api.Host ?? context.ServerName ?? "", api.Port ?? 0, null, api.PresentedFingerprint));

            case ApiException api:
                return PresentApi(api, context, server, details);

            case HttpRequestException:
            case SocketException:
            case TimeoutException:
            case TaskCanceledException:
                return Network(details);

            case OperationCanceledException:
                return new UserError("Отменено", "Операция остановлена, ничего не изменилось.", null,
                    UserErrorKind.Generic, [UserErrorAction.Close]);

            default:
                return Server(context, server, details);
        }
    }

    private static UserError PresentApi(ApiException api, ErrorContext ctx, string server, string details)
    {
        var message = api.ServerMessage;

        if (api.StatusCode == 401 || api.Kind is "auth_failed" or "authentication_failed" || LooksLikeAuthFailure(message))
            return Auth(server, details);

        switch (api.StatusCode)
        {
            case 404:
                return new UserError("Не нашли",
                    ctx.Path is null
                        ? "Такого файла или папки на сервере нет. Возможно, его уже удалили или переместили."
                        : $"«{ctx.Path}» на сервере нет. Возможно, его уже удалили или переместили. Обновите список.",
                    details, UserErrorKind.NotFound, [UserErrorAction.Close]);

            case 403:
                return new UserError("Нет доступа",
                    ctx.Path is null
                        ? $"{server} не разрешил это действие. Проверьте права доступа пользователя."
                        : $"{server} не разрешил действие с «{ctx.Path}». Проверьте права доступа пользователя.",
                    details, UserErrorKind.AccessDenied, [UserErrorAction.Close]);

            case 400:
                if (LooksLikeNetworkFailure(message))
                    return Network(details);
                return new UserError("Не получилось",
                    "Сервер не смог выполнить это действие. Проверьте имя, путь и параметры и попробуйте ещё раз.",
                    details, UserErrorKind.BadRequest,
                    ctx.IsConnecting ? [UserErrorAction.OpenProfile, UserErrorAction.Retry] : [UserErrorAction.Close]);

            case 408 or 502 or 503 or 504:
                return Network(details);

            default:
                if (LooksLikeNetworkFailure(message))
                    return Network(details);
                return Server(ctx, server, details);
        }
    }

    private static UserError Auth(string server, string details)
        => new("Хрю, не пускают",
            $"{server} не принял логин или пароль. Проверьте их в профиле.",
            details, UserErrorKind.Auth, [UserErrorAction.OpenProfile, UserErrorAction.Retry]);

    private static UserError Network(string details)
        => new("Связь пропала",
            "Сервер не отвечает. Проверьте адрес и подключение к интернету.",
            details, UserErrorKind.Network, [UserErrorAction.Retry]);

    /// <summary>
    /// 500 и всё непонятное. Бэкенд сейчас не отличает неверный пароль от прочих сбоев и отдаёт 500
    /// «Internal server error» — при подключении самая частая причина именно в данных входа.
    /// </summary>
    private static UserError Server(ErrorContext ctx, string server, string details)
        => ctx.IsConnecting
            ? new UserError("Не удалось подключиться",
                $"{server} не принял подключение. Проверьте адрес, логин и пароль в профиле или попробуйте ещё раз.",
                details, UserErrorKind.Server, [UserErrorAction.OpenProfile, UserErrorAction.Retry])
            : new UserError("Что-то пошло не так",
                "Не получилось выполнить действие. Попробуйте ещё раз; если повторится — откройте «Подробнее» и покажите это администратору.",
                details, UserErrorKind.Server, [UserErrorAction.Retry, UserErrorAction.Close]);

    private static string HostLabel(ApiException api, ErrorContext ctx)
        => !string.IsNullOrWhiteSpace(ctx.ServerName) ? ctx.ServerName : api.Host ?? "сервер";

    private static bool LooksLikeAuthFailure(string message)
    {
        var m = message.ToLowerInvariant();
        return m.Contains("permission denied") || m.Contains("authentication") || m.Contains("auth fail")
               || m.Contains("authenticat") || m.Contains("login incorrect") || m.Contains("530 ")
               || m.Contains("invalid credentials") || m.Contains("access denied for user");
    }

    private static bool LooksLikeNetworkFailure(string message)
    {
        var m = message.ToLowerInvariant();
        return m.Contains("connection refused") || m.Contains("timed out") || m.Contains("timeout")
               || m.Contains("no route to host") || m.Contains("name or service not known")
               || m.Contains("no such host") || m.Contains("network is unreachable")
               || m.Contains("connection reset") || m.Contains("actively refused");
    }

    private static string BuildDetails(Exception ex)
    {
        var lines = new List<string>();
        if (ex is ApiException api)
        {
            lines.Add($"HTTP {api.StatusCode}" + (api.Kind is null ? "" : $" · {api.Kind}"));
            lines.Add(api.ServerMessage);
            if (api.Host is not null) lines.Add($"Хост: {api.Host}:{api.Port}");
            if (api.ExpectedFingerprint is not null) lines.Add($"Ожидался: {api.ExpectedFingerprint}");
            if (api.PresentedFingerprint is not null) lines.Add($"Предъявлен: {api.PresentedFingerprint}");
        }
        else
        {
            lines.Add($"{ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is { } inner)
                lines.Add($"{inner.GetType().Name}: {inner.Message}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
