using Avalonia.Controls;
using FileManagerClient.Converters;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Views.Dialogs;

/// <summary>
/// «Свойства» файла или папки. Открывается сразу; размер папки подгружается (состояние «Считаем размер…»),
/// при закрытии подсчёт на сервере отменяется. Esc и «Закрыть» — закрыть.
/// </summary>
public partial class PropertiesDialog : DialogWindowBase
{
    public PropertiesDialog() => InitializeComponent();

    public PropertiesDialog(PropertiesViewModel vm) : this()
    {
        DataContext = vm;
        if (new FileItemToIconConverter().Convert(vm.Item, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture)
            is Avalonia.Media.Geometry geometry)
            TypeIcon.Data = geometry;

        var retry = MakeButton("Повторить", "tool", onClick: async (_, _) => await vm.LoadAsync(), minWidth: 110);
        retry.IsVisible = false;
        var close = MakeButton("Закрыть", "primary", isDefault: true, isCancel: true, onClick: (_, _) => Close(),
            minWidth: 110);
        ButtonsPanel.Children.Add(retry);
        ButtonsPanel.Children.Add(close);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PropertiesViewModel.HasError))
                retry.IsVisible = vm.HasError;
        };
        Opened += async (_, _) => await vm.LoadAsync();
        Closed += (_, _) => vm.Dispose();
    }

    protected override Control? InitialFocus() => ButtonsPanel.Children.LastOrDefault() as Control;

    protected override void Cancel() => Close();
}
