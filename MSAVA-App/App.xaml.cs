using MSAVA_App.Presentation.Login;
using MSAVA_App.Presentation.Welcome;
using Uno.Resizetizer;
using MSAVA_App.Services.Session;
using MSAVA_App.Services.Api;
using MSAVA_App.Services.Navigation;
using MSAVA_App.Services.Files;

namespace MSAVA_App;
public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }
    private IHost? _host;

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var builder = this.CreateBuilder(args)
            // Add navigation support for toolkit controls such as TabBar and NavigationView
            .UseToolkitNavigation()
            .Configure(host => host
#if DEBUG
                // Switch to Development environment when running in DEBUG
                .UseEnvironment(Environments.Development)
#endif
                .UseLogging(configure: (context, logBuilder) =>
                {
                    // Configure log levels for different categories of logging
                    logBuilder
                        .SetMinimumLevel(
                            context.HostingEnvironment.IsDevelopment() ?
                                LogLevel.Information :
                                LogLevel.Warning)

                        // Default filters for core Uno Platform namespaces
                        .CoreLogLevel(LogLevel.Warning);

                    // Uno Platform namespace filter groups
                    // Uncomment individual methods to see more detailed logging
                    //// Generic Xaml events
                    //logBuilder.XamlLogLevel(LogLevel.Debug);
                    //// Layout specific messages
                    //logBuilder.XamlLayoutLogLevel(LogLevel.Debug);
                    //// Storage messages
                    //logBuilder.StorageLogLevel(LogLevel.Debug);
                    //// Binding related messages
                    //logBuilder.XamlBindingLogLevel(LogLevel.Debug);
                    //// Binder memory references tracking
                    //logBuilder.BinderMemoryReferenceLogLevel(LogLevel.Debug);
                    //// DevServer and HotReload related
                    //logBuilder.HotReloadCoreLogLevel(LogLevel.Information);
                    //// Debug JS interop
                    //logBuilder.WebAssemblyLogLevel(LogLevel.Debug);

                }, enableUnoLogging: true)
                .UseSerilog(consoleLoggingEnabled: true, fileLoggingEnabled: true)
                .UseConfiguration(configure: configBuilder =>
                    configBuilder
                        .EmbeddedSource<App>()
                        .Section<AppConfig>()
                )
                // Enable localization (see appsettings.json for supported languages)
                .UseLocalization()
                .UseHttp((context, services) =>
                {
#if DEBUG
                // DelegatingHandler will be automatically injected
                services.AddTransient<DelegatingHandler, DebugHttpHandler>();
#endif
                    // Named HttpClient for the API (ASP.NET Core default dev ports)
                    var apiClientOptions = LoadApiClientOptions(context.Configuration);
                    services.AddSingleton(apiClientOptions);
                    services.AddHttpClient("MSAVA-Api", client =>
                    {
                        client.BaseAddress = apiClientOptions.RequireBaseAddress();
                    });
                })
                .UseAuthentication(auth =>
                auth.AddCustom(custom =>
                        custom
                .Login(async (sp, dispatcher, credentials, cancellationToken) =>
                {
                    var session = sp.GetRequiredService<LocalSessionService>();

                    if (!(credentials?.TryGetValue(nameof(LoginModel.Username), out var username) ?? false) || string.IsNullOrWhiteSpace(username))
                        return default;

                    if (!(credentials?.TryGetValue(nameof(LoginModel.Password), out var password) ?? false) || string.IsNullOrWhiteSpace(password))
                        return default;

                    var token = await session.LoginAsync(username!, password!, cancellationToken);
                    if (string.IsNullOrWhiteSpace(token))
                        return default; // fail login

                    credentials ??= new Dictionary<string, string>();
                    credentials[TokenCacheExtensions.AccessTokenKey] = token!;
                    return credentials;
                })
                .Refresh((sp, tokenDictionary, cancellationToken) =>
                {
                    return ValueTask.FromResult<IDictionary<string, string>?>(default);
                }), name: "CustomAuth")
                )
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<LocalSessionService>();
                    services.AddSingleton<ApiService>();
                    services.AddSingleton<NavigationService>();
                    services.AddSingleton<FileRetrievalService>();
                    services.AddSingleton<FileUploadClientService>();
                })
                .UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes)
            );
        MainWindow = builder.Window;

#if DEBUG
        MainWindow.UseStudio();
#endif
        MainWindow.SetWindowIcon();

        _host = await builder.NavigateAsync<Shell>
            (initialNavigate: (services, navigator) => Task.CompletedTask);
    }

    private static ApiClientOptions LoadApiClientOptions(
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        return new ApiClientOptions
        {
            Url = configuration[ApiClientOptions.UrlKey]
        };
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
        => Routes.Register(views, routes);
}
