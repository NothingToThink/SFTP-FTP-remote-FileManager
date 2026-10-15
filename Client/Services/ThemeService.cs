using Avalonia;
using Avalonia.Styling;

namespace FileManagerClient.Services;

/// <summary>
/// Переключение светлой/тёмной темы. Выбор сохраняется в ClientSettings
/// и применяется при старте приложения. По умолчанию — светлая.
/// </summary>
public class ThemeService
{
    private readonly ClientSettings _settings;

    public ThemeService(ClientSettings settings)
    {
        _settings = settings;
    }

    public bool IsDark => _settings.Theme == "dark";

    public ThemeVariant Current => IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

    /// <summary>Применяет сохранённую тему к приложению (вызывается на старте).</summary>
    public void Apply()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Current;
    }

    /// <summary>Устанавливает тему, применяет и сохраняет в настройки.</summary>
    public void Set(bool dark)
    {
        _settings.Theme = dark ? "dark" : "light";
        Apply();
        try { _settings.Save(); }
        catch { /* настройки не сохранились — тема всё равно применена на сессию */ }
    }

    public void Toggle() => Set(!IsDark);
}
