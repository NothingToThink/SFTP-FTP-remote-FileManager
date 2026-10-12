namespace FileManagerClient.Services;

/// <summary>
/// Приложение-широкая настройка «Уменьшить анимацию». PigMascot/PigTail подписываются сами,
/// поэтому views ничего не биндят: достаточно выставить <see cref="ReduceMotion"/>.
/// </summary>
public static class MotionSettings
{
    private static bool _reduceMotion;

    public static event Action? Changed;

    public static bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (_reduceMotion == value) return;
            _reduceMotion = value;
            Changed?.Invoke();
        }
    }
}
