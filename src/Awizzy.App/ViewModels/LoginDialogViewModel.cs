using Awizzy.Core.Abstractions;
using Awizzy.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Awizzy.App.ViewModels;

public partial class LoginDialogViewModel(
    ISsoOidcAuthService authService,
    IBrowserLauncher browser) : ObservableObject
{
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty]
    private string _userCode = string.Empty;

    [ObservableProperty]
    private string _statusText = "Contacting AWS…";

    [ObservableProperty]
    private bool _failed;

    public event EventHandler<bool>? Completed;

    public async Task RunAsync(SsoIntegration integration)
    {
        try
        {
            var authorization = await authService.BeginLoginAsync(integration, _cts.Token);
            UserCode = authorization.UserCode;
            StatusText = "Approve the request in your browser. Verify the code matches:";
            browser.Open(authorization.VerificationUriComplete);

            await authService.CompleteLoginAsync(integration, authorization, _cts.Token);
            Completed?.Invoke(this, true);
        }
        catch (OperationCanceledException)
        {
            Completed?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            Failed = true;
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        Completed?.Invoke(this, false);
    }
}
