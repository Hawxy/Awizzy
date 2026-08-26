using Avalonia.Interactivity;
using Awizzy.App.ViewModels;
using SukiUI.Controls;

namespace Awizzy.App.Views;

public partial class SettingsDialog : SukiWindow
{
    public SettingsDialog() => InitializeComponent();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsDialogViewModel viewModel)
            return;

        try
        {
            Close(viewModel.ToResult());
        }
        catch (ArgumentException ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.IsVisible = true;
        }
    }
}
