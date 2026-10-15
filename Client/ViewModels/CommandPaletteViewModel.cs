using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileManagerClient.Commands;
using FileManagerClient.Controls;

namespace FileManagerClient.ViewModels;

/// <summary>Строка результата палитры.</summary>
public sealed class PaletteItemViewModel
{
    public PaletteItemViewModel(SearchResult result, string? disabledReason)
    {
        Command = result.Command;
        Matches = result.TitleMatches;
        DisabledReason = disabledReason;
    }

    public AppCommand Command { get; }
    public IReadOnlyList<int> Matches { get; }
    public string? DisabledReason { get; }
    public bool IsEnabled => DisabledReason is null;
    public string Title => Command.Title;
    public string Caption => DisabledReason is null ? Command.Category : $"{Command.Category} · {DisabledReason}";
    public string? Shortcut => Command.Shortcut;
    public bool HasShortcut => !string.IsNullOrEmpty(Command.Shortcut);
    public string IconKey => Command.IconKey;
    public PigIconKind PigIcon => Command.PigIcon;
    public bool HasPigIcon => Command.PigIcon != PigIconKind.None;
    public bool HasLineIcon => !HasPigIcon;
    public double RowOpacity => IsEnabled ? 1 : 0.5;
}

public partial class CommandPaletteViewModel : ViewModelBase
{
    public const string Placeholder = "Что сделать? Начните вводить команду";

    private readonly CommandRegistry _registry;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _selectedIndex = -1;

    public CommandPaletteViewModel(CommandRegistry registry)
    {
        _registry = registry;
        _registry.Changed += (_, _) =>
        {
            if (IsOpen) Rebuild(keepSelection: true);
        };
    }

    public ObservableCollection<PaletteItemViewModel> Items { get; } = new();

    public bool HasItems => Items.Count > 0;
    public bool IsEmptyResult => Items.Count == 0;
    public string PlaceholderText => Placeholder;

    /// <summary>Вызывается при открытии палитры (например, перечитать список плагинных команд).</summary>
    public Action? Opening { get; init; }

    public void Open()
    {
        Opening?.Invoke();
        Query = string.Empty;
        Rebuild();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Rebuild();

    private void Rebuild(bool keepSelection = false)
    {
        var previous = keepSelection && SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex].Command.Id : null;
        Items.Clear();
        foreach (var r in _registry.Search(Query))
            Items.Add(new PaletteItemViewModel(r, r.Command.GetDisabledReason()));
        var idx = previous is null ? -1 : Items.ToList().FindIndex(i => i.Command.Id == previous);
        // Выбор по умолчанию — первая доступная команда.
        SelectedIndex = idx >= 0 ? idx : Items.ToList().FindIndex(i => i.IsEnabled) is var f and >= 0 ? f : (Items.Count > 0 ? 0 : -1);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmptyResult));
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0) return;
        SelectedIndex = ((SelectedIndex < 0 ? (delta > 0 ? -1 : 0) : SelectedIndex) + delta + Items.Count) % Items.Count;
    }

    /// <summary>Выполняет выбранную команду. Недоступные команды не запускаются. Возвращает true, если запущена.</summary>
    public async Task<bool> RunSelectedAsync() =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count && await RunAsync(Items[SelectedIndex]);

    public async Task<bool> RunAsync(PaletteItemViewModel item)
    {
        if (!item.IsEnabled)
            return false;
        var ctx = new CommandContext();
        // Закрываем палитру до запуска: диалоги команды не должны оказаться под оверлеем.
        // Команда, которой нужно продолжить диалог в палитре, ставит KeepPaletteOpen.
        IsOpen = false;
        await _registry.ExecuteAsync(item.Command, ctx);
        if (ctx.KeepPaletteOpen)
        {
            IsOpen = true;
            if (ctx.NewQuery is not null)
                Query = ctx.NewQuery;
        }
        return true;
    }
}
