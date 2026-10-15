using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FileManagerClient.Api;
using FileManagerClient.Models;
using SvcSystems.UI.Terminal;

namespace FileManagerClient.Terminal;

public partial class TerminalWindow : Window
{
    private readonly FileManagerApiClient? _api;
    private readonly Guid _connectionId;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TerminalControlModel _model = new(new TerminalOptions
    {
        Cols = 100, Rows = 30, Scrollback = 5000, ReflowOnResize = false,
    });
    private TerminalSocket? _socket;
    private bool _running;
    private TerminalSize Size => new(Math.Clamp(_model.Terminal.Cols, 2, 500),
        Math.Clamp(_model.Terminal.Rows, 1, 300));

    public TerminalWindow(FileManagerApiClient api, Guid connectionId, string name) : this()
    {
        _api = api;
        _connectionId = connectionId;
        Title = $"Терминал — {name}";
    }

    public TerminalWindow()
    {
        InitializeComponent();
        Screen.Model = _model;
        Screen.AddHandler(KeyDownEvent, ClipboardKeyDown, RoutingStrategies.Tunnel);
        _model.UserInput += (_, e) => Send(() => _socket?.SendInput(e.Data));
        _model.Terminal.Engine.DataReceived += (_, e) =>
            Send(() => _socket?.SendInput(Encoding.UTF8.GetBytes(e.Data)));
        _model.SizeChanged += (_, _) => Send(() => _socket?.Resize(Size));
        Opened += async (_, _) => await StartAsync();
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _socket?.Dispose();
            _model.Terminal.Engine.Dispose();
        };
    }

    private void Send(Action action)
    {
        try { action(); }
        catch (IOException ex) { Status.Text = ex.Message; }
    }

    private async Task StartAsync()
    {
        if (_api is null || _running || _lifetime.IsCancellationRequested) return;
        _running = true;
        Reconnect.IsEnabled = false;
        Status.Text = "Подключение…";
        try
        {
            _socket = await _api.OpenTerminalAsync(_connectionId, Size, _lifetime.Token);
            _socket.Resize(Size); // The window may have resized during the handshake.
            Status.Text = "SSH · подключено";
            Screen.Focus();
            await _socket.RunAsync(async text =>
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_lifetime.IsCancellationRequested) _model.Feed(text);
                }), _lifetime.Token);
            Status.Text = "Сеанс завершён";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            Status.Text = "Нет связи. Проверьте SSH-подключение и доступ к shell на сервере.";
        }
        finally
        {
            _socket?.Dispose();
            _socket = null;
            _running = false;
            Reconnect.IsEnabled = !_lifetime.IsCancellationRequested;
        }
    }

    private async void ReconnectClick(object? sender, RoutedEventArgs e)
    {
        _model.Feed("\u001bc");
        await StartAsync();
    }

    private void ClipboardKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Shift)) return;
        if (e.Key == Key.C)
        {
            e.Handled = true;
            CopyClick(sender, e);
        }
        else if (e.Key == Key.V)
        {
            e.Handled = true;
            PasteClick(sender, e);
        }
    }

    private async void CopyClick(object? sender, RoutedEventArgs e)
    {
        try { await Screen.CopySelectionAsync(); }
        catch (Exception) { Status.Text = "Буфер обмена недоступен."; }
        Screen.Focus();
    }

    private async void PasteClick(object? sender, RoutedEventArgs e)
    {
        if (_socket is null) return;
        try { await Screen.PasteFromClipboardAsync(); }
        catch (Exception) { Status.Text = "Не удалось вставить текст."; }
        Screen.Focus();
    }
}
