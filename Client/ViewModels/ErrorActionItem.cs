using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

/// <summary>Кнопка действия под ошибкой в панели файлов. Первая в списке — основная (accent).</summary>
public sealed record ErrorActionItem(UserErrorAction Action, string Label, bool IsPrimary)
{
    public static IReadOnlyList<ErrorActionItem> From(IEnumerable<UserErrorAction> actions)
        => actions.Select((a, i) => new ErrorActionItem(a, UserError.ActionLabel(a), i == 0)).ToList();
}
