using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using MSAVA_INF.Models;
using Serilog;
using Serilog.Events;

namespace MSAVA_INF.Environment;

public class LocalEnvironment : ILocalEnvironment
{
    private const int MinimumJwtSigningKeyBytes = 32;

    public LocalEnvironment(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Values = new LocalEnvironmentValues
        {
            JwtIssuerSigningKey = GetRequiredValue(configuration, "jwt_issuer_signing_key"),
            JwtIssuerName = GetRequiredValue(configuration, "jwt_issuer_name"),
            JwtIssuerAudience = GetRequiredValue(configuration, "jwt_issuer_audience"),
            AdminUsername = GetRequiredValue(configuration, "admin_username"),
            AdminPassword = GetRequiredValue(configuration, "admin_password"),
            PostgresBaseDbUser = GetRequiredValue(configuration, "postgres_basedb_user"),
            PostgresBaseDbPassword = GetRequiredValue(configuration, "postgres_basedb_password"),
            PostgresBaseDbHost = GetRequiredValue(configuration, "postgres_basedb_host"),
            PostgresBaseDbPort = ParseRequiredPositiveInt(configuration, "postgres_basedb_port"),
            PostgresBaseDbDbName = GetRequiredValue(configuration, "postgres_basedb_dbname"),
            PostgresBaseDbSslMode = GetRequiredValue(configuration, "postgres_basedb_ssl_mode"),
            SerilogInformationLevel = ParseRequiredEnum<LogEventLevel>(configuration, "serilog_information_level"),
            SerilogRollingInterval = ParseRequiredEnum<RollingInterval>(configuration, "serilog_rolling_interval"),
            SerilogRetainedFileCountLimit = ParseNullablePositiveInt(configuration, "serilog_retained_file_count_limit"),
            SerilogFileSizeLimitBytes = ParseRequiredPositiveLong(configuration, "serilog_file_size_limit_bytes"),
            SerilogRollOnFileSizeLimit = ParseRequiredBool(configuration, "serilog_roll_on_file_size_limit")
        };
    }

    public LocalEnvironmentValues Values { get; }

    public byte[] GetSigningKeyBytes()
    {
        string key = Values.JwtIssuerSigningKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("'jwt_issuer_signing_key' is missing or empty.");

        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length < MinimumJwtSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"'jwt_issuer_signing_key' must be at least {MinimumJwtSigningKeyBytes} UTF-8 bytes for HMAC SHA-256 signing.");
        }

        return keyBytes;
    }

    internal static string RejectPlaceholderValue(string key, string value)
    {
        if (IsExamplePlaceholder(value))
        {
            throw new InvalidOperationException(
                $"Required configuration value '{key}' still contains an example placeholder. Replace it with a real deployment value.");
        }

        return value;
    }

    private static string GetRequiredValue(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
            return RejectPlaceholderValue(key, value);

        throw new InvalidOperationException(
            $"Required configuration value '{key}' is missing or empty.");
    }

    private static int ParseRequiredPositiveInt(IConfiguration configuration, string key)
    {
        string value = GetRequiredValue(configuration, key);
        if (TryParseUnsignedInt(value, out int result) && result > 0)
            return result;

        throw new InvalidOperationException($"Configuration value '{key}' must be a positive integer.");
    }

    private static long ParseRequiredPositiveLong(IConfiguration configuration, string key)
    {
        string value = GetRequiredValue(configuration, key);
        if (TryParseUnsignedLong(value, out long result) && result > 0)
            return result;

        throw new InvalidOperationException($"Configuration value '{key}' must be a positive long integer.");
    }

    private static bool ParseRequiredBool(IConfiguration configuration, string key)
    {
        string value = GetRequiredValue(configuration, key);
        if (bool.TryParse(value, out bool result))
            return result;

        throw new InvalidOperationException($"Configuration value '{key}' must be true or false.");
    }

    private static TEnum ParseRequiredEnum<TEnum>(IConfiguration configuration, string key)
        where TEnum : struct, Enum
    {
        string value = GetRequiredValue(configuration, key);
        string normalizedValue = value.Trim();
        if (!IsNumericEnumLiteral(normalizedValue) &&
            Enum.TryParse(normalizedValue, ignoreCase: true, out TEnum result) &&
            Enum.IsDefined(result))
        {
            return result;
        }

        throw new InvalidOperationException($"Configuration value '{key}' must be a named {typeof(TEnum).Name} value.");
    }

    private static int? ParseNullablePositiveInt(IConfiguration configuration, string key)
    {
        string normalizedValue = GetRequiredValue(configuration, key).Trim();

        if (normalizedValue.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;

        if (TryParseUnsignedInt(normalizedValue, out int result) && result > 0)
            return result;

        throw new InvalidOperationException($"Configuration value '{key}' must be null or a positive integer.");
    }

    private static bool IsExamplePlaceholder(string value)
    {
        string normalizedValue = value.Trim();
        return normalizedValue.StartsWith("replace-with-", StringComparison.OrdinalIgnoreCase) ||
            normalizedValue.Equals("change-me", StringComparison.OrdinalIgnoreCase) ||
            normalizedValue.Equals("changeme", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericEnumLiteral(string value)
    {
        return value.Length > 0 &&
            (char.IsAsciiDigit(value[0]) || value[0] is '+' or '-');
    }

    private static bool TryParseUnsignedInt(string value, out int result)
    {
        return int.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryParseUnsignedLong(string value, out long result)
    {
        return long.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out result);
    }
}
