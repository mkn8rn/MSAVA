using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MSAVA_Shared.Models;

namespace MSAVA_App.Services.Session;

public static class JwtSessionParser
{
    public static SessionDTO? Parse(string jwt, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            logger?.LogWarning("Invalid JWT format (missing token)");
            return null;
        }

        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                logger?.LogWarning("Invalid JWT format (missing payload)");
                return null;
            }

            if (!TryBase64UrlDecodeToString(parts[1], out var payloadJson) ||
                string.IsNullOrWhiteSpace(payloadJson))
            {
                logger?.LogWarning("Unable to decode JWT payload");
                return null;
            }

            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                logger?.LogWarning("JWT payload was not a JSON object");
                return null;
            }

            return CreateSession(root);
        }
        catch (JsonException ex)
        {
            logger?.LogError(ex, "Failed to parse JWT payload JSON for session claims");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogError(ex, "Failed to read JWT payload claims");
            return null;
        }
    }

    private static SessionDTO CreateSession(JsonElement root)
    {
        var session = new SessionDTO
        {
            Claims = ReadClaims(root)
        };

        if (root.TryGetProperty("sub", out var subEl) &&
            subEl.ValueKind == JsonValueKind.String &&
            Guid.TryParse(subEl.GetString(), out var userId))
        {
            session.UserId = userId;
        }

        if (root.TryGetProperty("unique_name", out var usernameEl) &&
            usernameEl.ValueKind == JsonValueKind.String)
        {
            session.Username = usernameEl.GetString() ?? string.Empty;
        }

        session.Roles = ReadRoles(root);
        session.IsAdmin = session.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);
        session.IsBanned = session.Roles.Contains("Banned", StringComparer.OrdinalIgnoreCase);
        session.IsWhitelisted = session.Roles.Contains("Whitelisted", StringComparer.OrdinalIgnoreCase);
        session.AccessGroups = ReadAccessGroups(root);

        if (TryReadEpochClaim(root, "iat", out var issuedAt) ||
            TryReadEpochClaim(root, "nbf", out issuedAt))
        {
            session.IssuedAt = issuedAt;
        }
        else
        {
            session.IssuedAt = DateTime.MinValue;
        }

        session.ExpiresAt = TryReadEpochClaim(root, "exp", out var expiresAt)
            ? expiresAt
            : DateTime.MinValue;

        return session;
    }

    private static Dictionary<string, List<string>> ReadClaims(JsonElement root)
    {
        var claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            claims[property.Name] = ToStringList(property.Value);
        }

        return claims;
    }

    private static List<string> ReadRoles(JsonElement root)
    {
        var roles = new List<string>();

        if (root.TryGetProperty("role", out var roleEl))
        {
            AddRoleValues(roleEl, roles);
        }
        else if (root.TryGetProperty("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", out var roleUriEl))
        {
            AddRoleValues(roleUriEl, roles);
        }

        roles.RemoveAll(string.IsNullOrWhiteSpace);
        return roles;
    }

    private static void AddRoleValues(JsonElement roleElement, List<string> roles)
    {
        if (roleElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in roleElement.EnumerateArray())
            {
                if (role.ValueKind == JsonValueKind.String)
                    roles.Add(role.GetString() ?? string.Empty);
            }

            return;
        }

        if (roleElement.ValueKind == JsonValueKind.String)
            roles.Add(roleElement.GetString() ?? string.Empty);
    }

    private static List<Guid> ReadAccessGroups(JsonElement root)
    {
        var accessGroups = new List<Guid>();

        if (!root.TryGetProperty("accessGroups", out var accessGroupsElement) ||
            accessGroupsElement.ValueKind != JsonValueKind.String)
        {
            return accessGroups;
        }

        var accessGroupsValue = accessGroupsElement.GetString();
        if (string.IsNullOrWhiteSpace(accessGroupsValue))
            return accessGroups;

        foreach (var part in accessGroupsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var accessGroupId))
                accessGroups.Add(accessGroupId);
        }

        return accessGroups;
    }

    private static List<string> ToStringList(JsonElement element)
    {
        var values = new List<string>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    values.Add(item.ToString());
                }
                break;
            case JsonValueKind.Object:
                values.Add(element.GetRawText());
                break;
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                break;
            default:
                values.Add(element.ToString());
                break;
        }

        return values;
    }

    private static bool TryBase64UrlDecodeToString(string base64Url, out string value)
    {
        value = string.Empty;

        try
        {
            string paddedBase64 = base64Url.Replace('-', '+').Replace('_', '/');
            switch (paddedBase64.Length % 4)
            {
                case 0:
                    break;
                case 2:
                    paddedBase64 += "==";
                    break;
                case 3:
                    paddedBase64 += "=";
                    break;
                default:
                    return false;
            }

            var bytes = Convert.FromBase64String(paddedBase64);
            value = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryReadEpochClaim(JsonElement root, string name, out DateTime value)
    {
        value = default;
        if (!root.TryGetProperty(name, out var element))
            return false;

        if (!TryReadEpochSeconds(element, out var seconds))
            return false;

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryReadEpochSeconds(JsonElement element, out long seconds)
    {
        seconds = default;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out seconds),
            JsonValueKind.String => long.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out seconds),
            _ => false
        };
    }
}
