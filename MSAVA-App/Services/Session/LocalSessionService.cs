using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MSAVA_Shared.Models;
using MSAVA_App.Services.Api;

namespace MSAVA_App.Services.Session;
public class LocalSessionService
{
    private readonly ILogger<LocalSessionService> _logger;
    private readonly ApiService _api;

    private string? _accessToken;
    private SessionDTO? _session;

    public event EventHandler? LoggedOut;

    public LocalSessionService(ILogger<LocalSessionService> logger, ApiService api)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public string? AccessToken => _accessToken;
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(_accessToken);
    public SessionDTO? CurrentSession => _session;

    public async Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required", nameof(username));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required", nameof(password));

        var http = _api.CreateClient();

        var requestBody = new LoginRequestDTO
        {
            Username = username,
            Password = password
        };

        using var msg = _api.CreateJsonRequest(HttpMethod.Post, ApiService.Routes.AuthLogin, requestBody, anonymous: true);

        try
        {
            using var resp = await http.SendAsync(msg, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Login failed with status code {StatusCode}", resp.StatusCode);
                return null;
            }

            var payload = await resp.Content.ReadFromJsonAsync<LoginResponseDTO>(cancellationToken: cancellationToken);
            var token = payload?.Token;
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Login response did not contain a token");
                return null;
            }

            _accessToken = token;
            _api.SetAccessToken(token);
            _session = JwtSessionParser.Parse(token, _logger);
            if (_session != null)
            {
                _session.LoggedIn = true;
            }
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call authentication API");
            return null;
        }
    }

    public void Logout()
    {
        _accessToken = null;
        _session = null;
        _api.ClearAccessToken();
        LoggedOut?.Invoke(this, EventArgs.Empty);
    }
}
