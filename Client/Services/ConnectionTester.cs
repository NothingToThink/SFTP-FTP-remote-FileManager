using FileManagerClient.Api;
using FileManagerClient.Models;

namespace FileManagerClient.Services;

/// <summary>Проверка параметров профиля «вживую» (кнопка «Проверить подключение»). Бросает исключение при неудаче.</summary>
public interface IConnectionTester
{
    Task TestAsync(SavedProfile profile, CancellationToken ct = default);
}

/// <summary>
/// Создаёт временное соединение на бэкенде, подключается и в любом случае удаляет его.
/// Профиль при этом не сохраняется.
/// </summary>
public sealed class ApiConnectionTester(Func<string> serverUrl) : IConnectionTester
{
    public async Task TestAsync(SavedProfile profile, CancellationToken ct = default)
    {
        using var api = new FileManagerApiClient(serverUrl());
        var id = await api.CreateConnectionAsync(profile, ct);
        try
        {
            await api.ConnectAsync(id, ct);
        }
        finally
        {
            try
            {
                await api.DeleteConnectionAsync(id, CancellationToken.None);
            }
            catch
            {
                // уборка временного соединения не должна заслонять исходный результат
            }
        }
    }
}
