using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Awizzy.App.ViewModels;
using SukiUI.Controls;

namespace Awizzy.App.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // With a single integration, selecting it as a filter cannot narrow the
        // list, so swallow the press before the ListBox turns it into a selection.
        // Presses on the card's buttons (Login, Sync, ...) still pass through.
        IntegrationsList.AddHandler(PointerPressedEvent, OnIntegrationsPointerPressed,
            RoutingStrategies.Tunnel);
    }

    private void OnIntegrationsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { Integrations.Count: 1 }
            && e.Source is Avalonia.Visual source
            && source.FindAncestorOfType<Button>(includeSelf: true) is null)
        {
            e.Handled = true;
        }
    }
}
