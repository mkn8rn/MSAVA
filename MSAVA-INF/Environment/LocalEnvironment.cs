using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly string _envFileName;
        private readonly string _envFilePathForErrors;

        public LocalEnvironment()
        {
            _envFileName = GetEnvFileName();
            string? envFilePath = ResolveEnvFilePath(_envFileName);
            _envFilePathForErrors = envFilePath ?? Path.Combine(Directory.GetCurrentDirectory(), _envFileName);
            _values = envFilePath is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : LoadEnvFile(envFilePath);
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
                PostgresBaseDbPort = ParseRequiredPositiveInt("postgres_basedb_port"),
                PostgresBaseDbDbName = GetRequiredValue("postgres_basedb_dbname"),
                PostgresBaseDbSslMode = GetRequiredValue("postgres_basedb_ssl_mode"),
                SerilogInformationLevel = ParseRequiredEnum<LogEventLevel>("serilog_information_level"),
                SerilogRollingInterval = ParseRequiredEnum<RollingInterval>("serilog_rolling_interval"),
                SerilogRetainedFileCountLimit = ParseNullablePositiveInt("serilog_retained_file_count_limit"),
                SerilogFileSizeLimitBytes = ParseRequiredPositiveLong("serilog_file_size_limit_bytes"),
                SerilogRollOnFileSizeLimit = ParseRequiredBool("serilog_roll_on_file_size_limit")
            };
        }

        private string GetRequiredValue(string key)
        {
            var environmentValue = System.Environment.GetEnvironmentVariable(key)
                ?? System.Environment.GetEnvironmentVariable(key.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(environmentValue))
                return RejectPlaceholderValue(key, environmentValue);

            if (_values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return RejectPlaceholderValue(key, value);

            throw new InvalidOperationException($"Required configuration value '{key}' is missing or empty. Set it as a process environment variable or add it to {_envFilePathForErrors}.");
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

        private static bool IsExamplePlaceholder(string value)
        {
            string normalizedValue = value.Trim();
            return normalizedValue.StartsWith("replace-with-", StringComparison.OrdinalIgnoreCase) ||
                normalizedValue.Equals("change-me", StringComparison.OrdinalIgnoreCase) ||
                normalizedValue.Equals("changeme", StringComparison.OrdinalIgnoreCase);
        }

        private int ParseRequiredPositiveInt(string key)
        {
            var value = GetRequiredValue(key);
            if (TryParseUnsignedInt(value, out var result) && result > 0)
                return result;

            throw new InvalidOperationException($"Environment variable '{key}' must be a positive integer: '{value}'");
        }

        private long ParseRequiredPositiveLong(string key)
        {
            var value = GetRequiredValue(key);
            if (TryParseUnsignedLong(value, out var result) && result > 0)
                return result;

            throw new InvalidOperationException($"Environment variable '{key}' must be a positive long integer: '{value}'");
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

        private int? ParseNullablePositiveInt(string key)
        {
            var value = GetRequiredValue(key);
            string normalizedValue = value.Trim();

            if (normalizedValue.Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            if (TryParseUnsignedInt(normalizedValue, out var i) && i > 0)
                return i;

            throw new InvalidOperationException($"Environment variable '{key}' must be null or a positive integer: '{value}'");
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
                throw new InvalidOperationException($"'jwt_issuer_signing_key' is missing or empty in {_envFileName}");

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            if (keyBytes.Length < MinimumJwtSigningKeyBytes)
            {
                throw new InvalidOperationException(
                    $"'jwt_issuer_signing_key' must be at least {MinimumJwtSigningKeyBytes} UTF-8 bytes for HMAC SHA-256 signing.");
            }

            return keyBytes;
        }

        private static string GetEnvFileName()
        {
            return IsDevelopment() ? ".env.development" : ".env";
        }

        private static string? ResolveEnvFilePath(string envFileName)
        {
            foreach (string candidatePath in GetEnvFileCandidates(envFileName))
            {
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            return null;
        }

        private static IEnumerable<string> GetEnvFileCandidates(string envFileName)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string directory in GetSearchDirectories())
            {
                foreach (string candidatePath in GetDirectoryCandidates(directory, envFileName))
                {
                    if (seen.Add(candidatePath))
                        yield return candidatePath;
                }
            }
        }

        private static IEnumerable<string> GetSearchDirectories()
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            if (IsBuildOutputOrIntermediateDirectory(currentDirectory))
                yield break;

            foreach (string directory in EnumerateDirectoryAndAncestors(currentDirectory))
                yield return directory;
        }

        private static IEnumerable<string> EnumerateDirectoryAndAncestors(string path)
        {
            var directory = new DirectoryInfo(path);

            while (directory is not null)
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }

        private static bool IsBuildOutputOrIntermediateDirectory(string path)
        {
            var directory = new DirectoryInfo(path);

            while (directory is not null)
            {
                if (string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directory.Name, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                directory = directory.Parent;
            }

            return false;
        }

        private static IEnumerable<string> GetDirectoryCandidates(string directory, string envFileName)
        {
            yield return Path.Combine(directory, envFileName);
            yield return Path.Combine(directory, "MSAVA-INF", envFileName);
        }
    }
}
