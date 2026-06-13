using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MSAVA_INF.Models;
using Serilog.Events;
using Serilog;

namespace MSAVA_INF.Environment
{
    public class LocalEnvironment : ILocalEnvironment
    {
        private const int MinimumJwtSigningKeyBytes = 32;

        public LocalEnvironmentValues Values { get; }
        private readonly Dictionary<string, string> _values;
        private static readonly string EnvFolder = Path.GetDirectoryName(typeof(LocalEnvironment).Assembly.Location)!;
        private static readonly string EnvFileName = IsDevelopment() ? ".env.development" : ".env";
        private static readonly string EnvFilePath = Path.Combine(EnvFolder, EnvFileName);

        public LocalEnvironment()
        {
            _values = LoadEnvFile(EnvFilePath);
            Values = new LocalEnvironmentValues
            {
                JwtIssuerSigningKey = GetRequiredValue("jwt_issuer_signing_key"),
                JwtIssuerName = GetRequiredValue("jwt_issuer_name"),
                JwtIssuerAudience = GetRequiredValue("jwt_issuer_audience"),
                AdminUsername = GetRequiredValue("admin_username"),
                AdminPassword = GetRequiredValue("admin_password"),
                PostgresBaseDbUser = GetRequiredValue("postgres_basedb_user"),
                PostgresBaseDbPassword = GetRequiredValue("postgres_basedb_password"),
                PostgresBaseDbHost = GetRequiredValue("postgres_basedb_host"),
                PostgresBaseDbPort = ParseRequiredInt("postgres_basedb_port"),
                PostgresBaseDbDbName = GetRequiredValue("postgres_basedb_dbname"),
                PostgresBaseDbSslMode = GetRequiredValue("postgres_basedb_ssl_mode"),
                SerilogInformationLevel = ParseRequiredEnum<LogEventLevel>("serilog_information_level"),
                SerilogRollingInterval = ParseRequiredEnum<RollingInterval>("serilog_rolling_interval"),
                SerilogRetainedFileCountLimit = ParseNullableInt(GetRequiredValue("serilog_retained_file_count_limit")),
                SerilogFileSizeLimitBytes = ParseRequiredLong("serilog_file_size_limit_bytes"),
                SerilogRollOnFileSizeLimit = ParseRequiredBool("serilog_roll_on_file_size_limit")
            };
        }

        private string GetRequiredValue(string key)
        {
            var environmentValue = System.Environment.GetEnvironmentVariable(key)
                ?? System.Environment.GetEnvironmentVariable(key.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(environmentValue))
                return environmentValue;

            if (_values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;

            throw new InvalidOperationException($"Required configuration value '{key}' is missing or empty. Set it as a process environment variable or add it to {EnvFileName}.");
        }

        private int ParseRequiredInt(string key)
        {
            var value = GetRequiredValue(key);
            if (int.TryParse(value, out var result))
                return result;
            throw new InvalidOperationException($"Environment variable '{key}' could not be parsed as int: '{value}'");
        }

        private long ParseRequiredLong(string key)
        {
            var value = GetRequiredValue(key);
            if (long.TryParse(value, out var result))
                return result;
            throw new InvalidOperationException($"Environment variable '{key}' could not be parsed as long: '{value}'");
        }

        private bool ParseRequiredBool(string key)
        {
            var value = GetRequiredValue(key);
            if (bool.TryParse(value, out var result))
                return result;
            throw new InvalidOperationException($"Environment variable '{key}' could not be parsed as bool: '{value}'");
        }

        private TEnum ParseRequiredEnum<TEnum>(string key) where TEnum : struct
        {
            var value = GetRequiredValue(key);
            if (Enum.TryParse<TEnum>(value, true, out var result))
                return result;
            throw new InvalidOperationException($"Environment variable '{key}' could not be parsed as {typeof(TEnum).Name}: '{value}'");
        }

        private static int? ParseNullableInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.ToLowerInvariant() == "null")
                return null;
            if (int.TryParse(value, out var i))
                return i;
            throw new InvalidOperationException($"Environment variable for nullable int could not be parsed: '{value}'");
        }

        public static bool IsDevelopment()
        {
            var aspnetEnv = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            return string.Equals(aspnetEnv, "Development", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> LoadEnvFile(string path)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return dict;
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;
                var idx = trimmed.IndexOf('=');
                if (idx <= 0) continue;
                var key = trimmed.Substring(0, idx).Trim();
                var value = trimmed.Substring(idx + 1).Trim();
                dict[key] = value;
            }
            return dict;
        }

        public byte[] GetSigningKeyBytes()
        {
            string key = Values.JwtIssuerSigningKey;
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"'jwt_issuer_signing_key' is missing or empty in {EnvFileName}");

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            if (keyBytes.Length < MinimumJwtSigningKeyBytes)
            {
                throw new InvalidOperationException(
                    $"'jwt_issuer_signing_key' must be at least {MinimumJwtSigningKeyBytes} UTF-8 bytes for HMAC SHA-256 signing.");
            }

            return keyBytes;
        }
    }
}
