using MSAVA_INF.Models;
using Npgsql;

namespace MSAVA_INF.Environment;

public static class PostgresConnectionStringFactory
{
    public static string CreateBaseDbConnectionString(LocalEnvironmentValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new NpgsqlConnectionStringBuilder
        {
            Host = RequireConfigured(values.PostgresBaseDbHost, nameof(values.PostgresBaseDbHost)),
            Port = RequireValidPort(values.PostgresBaseDbPort),
            Database = RequireConfigured(values.PostgresBaseDbDbName, nameof(values.PostgresBaseDbDbName)),
            Username = RequireConfigured(values.PostgresBaseDbUser, nameof(values.PostgresBaseDbUser)),
            Password = RequireConfigured(values.PostgresBaseDbPassword, nameof(values.PostgresBaseDbPassword)),
            SslMode = ParseSslMode(values.PostgresBaseDbSslMode)
        }.ConnectionString;
    }

    private static string RequireConfigured(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{propertyName} must be configured.");

        return value;
    }

    private static int RequireValidPort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"{nameof(LocalEnvironmentValues.PostgresBaseDbPort)} must be between 1 and 65535.");
        }

        return port;
    }

    private static SslMode ParseSslMode(string value)
    {
        string configuredValue = RequireConfigured(value, nameof(LocalEnvironmentValues.PostgresBaseDbSslMode));
        string normalizedValue = configuredValue.Trim();

        if (!IsNumericEnumLiteral(normalizedValue) &&
            Enum.TryParse<SslMode>(normalizedValue, ignoreCase: true, out var sslMode) &&
            Enum.IsDefined(sslMode))
        {
            return sslMode;
        }

        throw new InvalidOperationException(
            $"{nameof(LocalEnvironmentValues.PostgresBaseDbSslMode)} could not be parsed as {nameof(SslMode)}.");
    }

    private static bool IsNumericEnumLiteral(string value)
    {
        return value.Length > 0 &&
            (char.IsAsciiDigit(value[0]) || value[0] is '+' or '-');
    }
}
