using MSAVA_App.Presentation.Login;
using MSAVA_App.Presentation.Welcome;
using MSAVA_App.Services.Navigation;
using MSAVA_App.Services.Session;
using MSAVA_Shared.Diagnostics;
using Microsoft.Extensions.Logging;
using Uno.Extensions.Authentication;
using Uno.Extensions.Navigation;

namespace MSAVA_App.Presentation;

public class ShellModel
{
    private readonly NavigationService _navigation;
    private readonly IAuthenticationService _auth;
    private readonly ILogger<ShellModel> _logger;
    private bool _initialized;

    public ShellModel(
        LocalSessionService localSession,
        INavigator navigator,
        NavigationService navigation,
        IAuthenticationService auth,
        ILogger<ShellModel> logger)
    {
        ArgumentNullException.ThrowIfNull(localSession);

        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigation.SetNavigator(navigator);
        _navigation.SetRootOwner(this);
        localSession.LoggedOut += OnLoggedOut;

        InitializationTask = RunShellTaskAsync(
            InitializeAsync,
            "Shell initialization navigation failed.");
    }

    internal Task InitializationTask { get; }

    private async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        var authenticated = await _auth.RefreshAsync();
        if (authenticated)
        {
            await _navigation.NavigateTo<MainModel>(this, qualifier: Qualifiers.Nested);
        }
        else
        {
            await _navigation.NavigateTo<LoginModel>(this, qualifier: Qualifiers.Nested);
        }
    }

    private void OnLoggedOut(object? sender, EventArgs e)
    {
        _ = RunShellTaskAsync(
            () => _navigation.NavigateTo<LoginModel>(this, qualifier: Qualifiers.ClearBackStack),
            "Shell logout navigation failed.");
    }

    private async Task RunShellTaskAsync(Func<Task> operation, string failureMessage)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await operation();
        }
        catch (Exception ex) when (!CriticalExceptionPolicy.ContainsCriticalException(ex))
        {
            _logger.LogWarning(ex, "{FailureMessage}", failureMessage);
        }
    }
}
