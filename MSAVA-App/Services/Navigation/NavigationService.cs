using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MSAVA_App.Presentation.Login;
using MSAVA_App.Services.Session;
using Microsoft.Extensions.Logging;
using Uno.Extensions.Navigation;

namespace MSAVA_App.Services.Navigation;

public class NavigationService
{
    private static readonly ConcurrentDictionary<Type, NavigationServiceOptions> _options = new();
    private readonly LocalSessionService _session;
    private readonly ILogger<NavigationService> _logger;
    private INavigator? _navigator;
    private object? _rootOwner;

    public NavigationService(LocalSessionService session, ILogger<NavigationService> logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void SetNavigator(INavigator navigator)
    {
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _logger.LogInformation("Navigator set on NavigationService: {NavigatorType}", navigator.GetType().FullName);
    }

    // Sets the logical root owner (typically ShellModel) to anchor default in-shell navigation
    public void SetRootOwner(object owner)
    {
        _rootOwner = owner ?? throw new ArgumentNullException(nameof(owner));
        _logger.LogInformation("Root owner set on NavigationService: {OwnerType}", owner.GetType().FullName);
    }

    public static void RegisterFor<TViewModel>(NavigationServiceOptions options) where TViewModel : class
        => _options[typeof(TViewModel)] = options ?? new NavigationServiceOptions();

    public static NavigationServiceOptions GetOptionsFor<TViewModel>() where TViewModel : class
        => _options.TryGetValue(typeof(TViewModel), out var opts) ? opts : new NavigationServiceOptions();

    public Task<bool> EnsureAccessAsync<TViewModel>(CancellationToken ct = default) where TViewModel : class
    {
        var opts = GetOptionsFor<TViewModel>();
        var allowed = opts.Public || _session.IsLoggedIn;

        if (!allowed)
        {
            _logger.LogWarning("Access denied for VM {ViewModel}. Public={Public}, IsLoggedIn={IsLoggedIn}", typeof(TViewModel).FullName, opts.Public, _session.IsLoggedIn);
        }
        else
        {
            _logger.LogDebug("Access granted for VM {ViewModel}. Public={Public}, IsLoggedIn={IsLoggedIn}", typeof(TViewModel).FullName, opts.Public, _session.IsLoggedIn);
        }

        return Task.FromResult(allowed);
    }

    public async Task<bool> NavigateTo<TViewModel>(object owner,
        string? qualifier = null,
        object? data = null,
        CancellationToken ct = default) where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (_navigator is null)
        {
            throw new InvalidOperationException("Navigator not initialized. Call NavigationService.SetNavigator at app startup (e.g., from ShellModel).");
        }

        var vmType = typeof(TViewModel).FullName;
        _logger.LogInformation("NavigateTo requested: {ViewModel} | Owner={OwnerType} | Qualifier={Qualifier} | DataType={DataType}",
            vmType, owner.GetType().FullName, string.IsNullOrWhiteSpace(qualifier) ? "(default)" : qualifier, data?.GetType().FullName ?? "null");

        var allowed = await EnsureAccessAsync<TViewModel>(ct);
        // When blocked, always navigate relative to root owner and clear back stack
        var effectiveOwner = GetEffectiveOwner(owner);
        if (!allowed)
        {
            await _navigator.NavigateViewModelAsync<LoginModel>(effectiveOwner, qualifier: Qualifiers.ClearBackStack, cancellation: ct);
            return false;
        }

        var effectiveQualifier = string.IsNullOrWhiteSpace(qualifier) ? Qualifiers.ClearBackStack : qualifier;

        await _navigator.NavigateViewModelAsync<TViewModel>(effectiveOwner, qualifier: effectiveQualifier, data: data, cancellation: ct);
        return true;
    }

    private object GetEffectiveOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return _rootOwner ?? owner;
    }
}
