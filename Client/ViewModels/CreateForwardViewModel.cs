using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

public partial class CreateForwardViewModel : ViewModelBase
{
    public ForwardType[] Types { get; } = { ForwardType.Local, ForwardType.Remote, ForwardType.Dynamic };

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private ForwardType _type = ForwardType.Local;
    [ObservableProperty] private string _bindPort = "0";
    [ObservableProperty] private string _targetHost = "127.0.0.1";
    [ObservableProperty] private string _targetPort = string.Empty;
    [ObservableProperty] private PortSuggestion? _selectedSuggestion;
    [ObservableProperty] private string? _suggestionStatus;
    [ObservableProperty] private bool _isScanning;

    private readonly List<PortSuggestion> _suggestions = new();

    /// <summary>Имя подключения для заголовка формы.</summary>
    [ObservableProperty] private string _connectionName = string.Empty;

    [ObservableProperty] private bool _bindPortInvalid;
    [ObservableProperty] private bool _targetHostInvalid;
    [ObservableProperty] private bool _targetPortInvalid;

    public bool IsLocalType => Type == ForwardType.Local;
    public bool IsRemoteType => Type == ForwardType.Remote;
    public bool IsDynamicType => Type == ForwardType.Dynamic;

    public string BindLabel => Type switch
    {
        ForwardType.Remote => "Порт на сервере (0 — любой)",
        ForwardType.Dynamic => "Порт SOCKS у вас (0 — любой)",
        _ => "Порт у вас (0 — любой)",
    };

    public string TargetHostLabel => Type == ForwardType.Remote
        ? "Хост на этом компьютере"
        : "Хост (как его видит сервер)";

    public bool HasSuggestions => Suggestions.Count > 0;

    public CreateForwardViewModel()
    {
        Suggestions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSuggestions));
    }

    [RelayCommand]
    private void SelectType(ForwardType type) => Type = type;

    public ObservableCollection<PortSuggestion> Suggestions { get; } = new();

    public bool NeedsTarget => Type != ForwardType.Dynamic;

    /// <summary>Опрос сервера: какие порты реально слушаются (используется окном создания).</summary>
    public async Task LoadSuggestionsAsync(IForwardingApi api, Guid connectionId)
    {
        IsScanning = true;
        SuggestionStatus = null;
        try
        {
            var found = await api.GetForwardSuggestionsAsync(connectionId, loopbackOnly: true, limit: 50);
            _suggestions.Clear();
            Suggestions.Clear();
            foreach (var suggestion in found.OrderByDescending(s => s.Address is "127.0.0.1" or "::1" or "localhost").ThenBy(s => s.Port))
            {
                _suggestions.Add(suggestion);
                Suggestions.Add(suggestion);
            }
            SuggestionStatus = found.Count == 0
                ? "Сервер не сообщил ни о каких слушающих портах — заполните поля вручную"
                : $"Найдено слушающих портов: {found.Count}";
        }
        catch (ApiException ex)
        {
            SuggestionStatus = "Сканер недоступен. " + ErrorPresenter.Present(ex).Text;
        }
        catch (Exception ex)
        {
            SuggestionStatus = "Сканер недоступен. " + ErrorPresenter.Present(ex).Text;
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnTypeChanged(ForwardType value)
    {
        OnPropertyChanged(nameof(NeedsTarget));
        OnPropertyChanged(nameof(IsLocalType));
        OnPropertyChanged(nameof(IsRemoteType));
        OnPropertyChanged(nameof(IsDynamicType));
        OnPropertyChanged(nameof(BindLabel));
        OnPropertyChanged(nameof(TargetHostLabel));
    }

    partial void OnSelectedSuggestionChanged(PortSuggestion? value)
    {
        if (value is null)
            return;
        TargetPort = value.Port.ToString();
        if (!string.IsNullOrEmpty(value.Address) && value.Address != "0.0.0.0")
            TargetHost = value.Address;
        if (string.IsNullOrWhiteSpace(Name))
            Name = value.Service ?? $"порт {value.Port}";
    }

    [ObservableProperty] private string? _errorMessage;

    public bool Validate()
    {
        BindPortInvalid = TargetHostInvalid = TargetPortInvalid = false;
        string? error = null;

        if (!int.TryParse(BindPort, out var bind) || bind is < 0 or > 65535)
        {
            BindPortInvalid = true;
            error = "Локальный порт: число 0–65535 (0 — выбрать свободный автоматически).";
        }

        if (Type != ForwardType.Dynamic)
        {
            if (string.IsNullOrWhiteSpace(TargetHost))
            {
                TargetHostInvalid = true;
                error ??= "Укажите целевой хост.";
            }

            if (!int.TryParse(TargetPort, out var target) || target is < 1 or > 65535)
            {
                TargetPortInvalid = true;
                error ??= "Целевой порт: число 1–65535.";
            }
        }

        ErrorMessage = error;
        return error is null;
    }
}
