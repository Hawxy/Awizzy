using Avalonia.Interactivity;
using Awizzy.App.ViewModels;
using SukiUI.Controls;

namespace Awizzy.App.Views;

public partial class AddIntegrationDialog : SukiWindow
{
    public AddIntegrationDialog() => InitializeComponent();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddIntegrationDialogViewModel viewModel)
            Close(viewModel.ToInput());
    }
}
