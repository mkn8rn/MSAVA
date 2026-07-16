using Microsoft.Extensions.Configuration;
using MSAVA_INF.Environment;
using Serilog;
using Serilog.Events;
using Supprocom.Secrets;

namespace MSAVA_App.Tests;

[NonParallelizable]
public class LocalEnvironmentTests
{
    [Test]
    public void Constructor_ReadsRequiredValuesFromConfiguration()
    {
        var config = CreateConfiguration(CreateValidEnvironmentValues());

        var env = new LocalEnvironment(config);

        env.Values.JwtIssuerName.Should().Be("MSAVA Tests");
        env.Values.PostgresBaseDbPort.Should().Be(5433);
        env.Values.SerilogInformationLevel.Should().Be(LogEventLevel.Debug);
        env.Values.SerilogRollingInterval.Should().Be(RollingInterval.Hour);
        env.Values.SerilogRetainedFileCountLimit.Should().Be(7);
        env.Values.SerilogFileSizeLimitBytes.Should().Be(2048);
        env.Values.SerilogRollOnFileSizeLimit.Should().BeTrue();
        env.GetSigningKeyBytes().Should().Equal(
            System.Text.Encoding.UTF8.GetBytes("test-signing-key-with-at-least-32-characters"));
    }

    [Test]
    public void Constructor_AllowsNullSerilogRetainedFileCountLimit()
    {
        var values = CreateValidEnvironmentValues();
        values["serilog_retained_file_count_limit"] = "null";
        var config = CreateConfiguration(values);

        var env = new LocalEnvironment(config);

        env.Values.SerilogRetainedFileCountLimit.Should().BeNull();
    }

    [TestCase("postgres_basedb_port", "0", "Configuration value 'postgres_basedb_port' must be a positive integer.")]
    [TestCase("postgres_basedb_port", "+5432", "Configuration value 'postgres_basedb_port' must be a positive integer.")]
    [TestCase("postgres_basedb_port", "-5432", "Configuration value 'postgres_basedb_port' must be a positive integer.")]
    [TestCase("serilog_retained_file_count_limit", "0", "Configuration value 'serilog_retained_file_count_limit' must be null or a positive integer.")]
    [TestCase("serilog_retained_file_count_limit", "-1", "Configuration value 'serilog_retained_file_count_limit' must be null or a positive integer.")]
    [TestCase("serilog_retained_file_count_limit", "1.5", "Configuration value 'serilog_retained_file_count_limit' must be null or a positive integer.")]
    [TestCase("serilog_file_size_limit_bytes", "0", "Configuration value 'serilog_file_size_limit_bytes' must be a positive long integer.")]
    [TestCase("serilog_file_size_limit_bytes", "+2048", "Configuration value 'serilog_file_size_limit_bytes' must be a positive long integer.")]
    [TestCase("serilog_file_size_limit_bytes", "-2048", "Configuration value 'serilog_file_size_limit_bytes' must be a positive long integer.")]
    public void Constructor_RejectsInvalidPositiveNumericValues(
        string key,
        string invalidValue,
        string expectedMessage)
    {
        var values = CreateValidEnvironmentValues();
        values[key] = invalidValue;
        var config = CreateConfiguration(values);

        Action act = () => _ = new LocalEnvironment(config);

        var exception = act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedMessage);

        exception.Which.Message.Should().NotContain(invalidValue);
    }

    [TestCase("serilog_information_level", "2", "Configuration value 'serilog_information_level' must be a named LogEventLevel value.")]
    [TestCase("serilog_information_level", "999", "Configuration value 'serilog_information_level' must be a named LogEventLevel value.")]
    [TestCase("serilog_information_level", "Informational", "Configuration value 'serilog_information_level' must be a named LogEventLevel value.")]
    [TestCase("serilog_rolling_interval", "3", "Configuration value 'serilog_rolling_interval' must be a named RollingInterval value.")]
    [TestCase("serilog_rolling_interval", "+3", "Configuration value 'serilog_rolling_interval' must be a named RollingInterval value.")]
    [TestCase("serilog_rolling_interval", "Daily", "Configuration value 'serilog_rolling_interval' must be a named RollingInterval value.")]
    public void Constructor_RejectsNumericAndUndefinedEnumValues(
        string key,
        string invalidValue,
        string expectedMessage)
    {
        var values = CreateValidEnvironmentValues();
        values[key] = invalidValue;
        var config = CreateConfiguration(values);

        Action act = () => _ = new LocalEnvironment(config);

        var exception = act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedMessage);

        exception.Which.Message.Should().NotContain(invalidValue);
    }

    [Test]
    public void Constructor_RejectsInvalidBooleanValueWithoutEchoingValue()
    {
        var values = CreateValidEnvironmentValues();
        const string invalidValue = "yes-please";
        values["serilog_roll_on_file_size_limit"] = invalidValue;
        var config = CreateConfiguration(values);

        Action act = () => _ = new LocalEnvironment(config);

        var exception = act.Should().Throw<InvalidOperationException>()
            .WithMessage("Configuration value 'serilog_roll_on_file_size_limit' must be true or false.");

        exception.Which.Message.Should().NotContain(invalidValue);
    }

    [Test]
    public void Constructor_RejectsMissingRequiredValueWithoutMentioningSecretFilePaths()
    {
        var values = CreateValidEnvironmentValues();
        values.Remove("jwt_issuer_signing_key");
        var config = CreateConfiguration(values);

        Action act = () => _ = new LocalEnvironment(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Required configuration value 'jwt_issuer_signing_key' is missing or empty.");
    }

    [Test]
    public void GetSigningKeyBytes_RejectsSigningKeyShorterThan32Bytes()
    {
        var values = CreateValidEnvironmentValues();
        values["jwt_issuer_signing_key"] = "too-short";
        var config = CreateConfiguration(values);
        var env = new LocalEnvironment(config);

        Action act = () => env.GetSigningKeyBytes();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("'jwt_issuer_signing_key' must be at least 32 UTF-8 bytes for HMAC SHA-256 signing.");
    }

    [TestCase("replace-with-local-admin-password")]
    [TestCase(" REPLACE-WITH-POSTGRES-PASSWORD ")]
    [TestCase("change-me")]
    [TestCase("changeme")]
    public void RejectPlaceholderValue_RejectsCopiedExamplePlaceholders(string value)
    {
        Action act = () => LocalEnvironment.RejectPlaceholderValue("admin_password", value);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Required configuration value 'admin_password' still contains an example placeholder. Replace it with a real deployment value.");
    }

    [Test]
    public void RejectPlaceholderValue_ReturnsConfiguredValue()
    {
        string configuredValue = "correct-horse-battery-staple";

        string result = LocalEnvironment.RejectPlaceholderValue("admin_password", configuredValue);

        result.Should().Be(configuredValue);
    }

    [Test]
    public void SupprocomSecrets_CreatesProductionActiveEnvFromTemplateAndReadsEqualsInValues()
    {
        string directory = CreateTempDirectory();

        try
        {
            WriteEnvironmentTemplate(
                Path.Combine(directory, ".env.template"),
                CreateValidEnvironmentValues(new()
                {
                    ["jwt_issuer_name"] = "MSAVA Production",
                    ["admin_password"] = "value=with=equals"
                }));

            var config = CreateSupprocomConfiguration(directory, "Production");
            var env = new LocalEnvironment(config);

            env.Values.JwtIssuerName.Should().Be("MSAVA Production");
            env.Values.AdminPassword.Should().Be("value=with=equals");
            File.Exists(Path.Combine(directory, ".env")).Should().BeTrue();
            File.Exists(Path.Combine(directory, ".env.development")).Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Test]
    public void SupprocomSecrets_UsesDevelopmentTemplateAsReplacementForBaseEnv()
    {
        string directory = CreateTempDirectory();

        try
        {
            WriteEnvironmentTemplate(
                Path.Combine(directory, ".env.template"),
                CreateValidEnvironmentValues(new() { ["jwt_issuer_name"] = "MSAVA Production" }));
            WriteEnvironmentTemplate(
                Path.Combine(directory, ".env.development.template"),
                CreateValidEnvironmentValues(new() { ["jwt_issuer_name"] = "MSAVA Development" }));

            var config = CreateSupprocomConfiguration(directory, "Development");
            var env = new LocalEnvironment(config);

            env.Values.JwtIssuerName.Should().Be("MSAVA Development");
            File.Exists(Path.Combine(directory, ".env")).Should().BeFalse(
                "MSAVA development configuration replaces the base file instead of overlaying it");
            File.Exists(Path.Combine(directory, ".env.development")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Test]
    public void SupprocomSecrets_ProcessEnvironmentOverridesTemplateValues()
    {
        string directory = CreateTempDirectory();
        const string key = "JWT_ISSUER_NAME";

        using var restore = new EnvironmentVariableRestore([key]);
        Environment.SetEnvironmentVariable(key, "MSAVA Process Environment");

        try
        {
            WriteEnvironmentTemplate(
                Path.Combine(directory, ".env.template"),
                CreateValidEnvironmentValues(new() { ["jwt_issuer_name"] = "MSAVA File" }));

            var config = CreateSupprocomConfiguration(directory, "Production");
            var env = new LocalEnvironment(config);

            env.Values.JwtIssuerName.Should().Be("MSAVA Process Environment");
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Test]
    public void SupprocomSecrets_ConfigurationLookupIsCaseInsensitive()
    {
        string directory = CreateTempDirectory();
        Dictionary<string, string> values = CreateValidEnvironmentValues();
        values.Remove("jwt_issuer_name");
        values["JWT_ISSUER_NAME"] = "MSAVA Uppercase File Key";

        try
        {
            WriteEnvironmentTemplate(Path.Combine(directory, ".env.template"), values);

            var config = CreateSupprocomConfiguration(directory, "Production");
            var env = new LocalEnvironment(config);

            env.Values.JwtIssuerName.Should().Be("MSAVA Uppercase File Key");
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Test]
    public void SupprocomSecrets_IgnoresCommentsAndBlankLines()
    {
        string directory = CreateTempDirectory();

        try
        {
            string templatePath = Path.Combine(directory, ".env.template");
            File.WriteAllLines(
                templatePath,
                ["# leading comment", "", .. CreateValidEnvironmentValues().Select(value => $"{value.Key}={value.Value}"), "", "   # trailing comment"]);

            var config = CreateSupprocomConfiguration(directory, "Production");
            var env = new LocalEnvironment(config);

            env.Values.JwtIssuerName.Should().Be("MSAVA Tests");
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    [Test]
    public void SupprocomSecrets_ReportsMalformedDotenvWithoutUsingMsavaParser()
    {
        string directory = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(directory, ".env"), "not an assignment");

            Action act = () => _ = CreateSupprocomConfiguration(directory, "Production");

            act.Should().Throw<SupprocomSecretsException>()
                .Where(exception => exception.Code == "InvalidDotenvAssignment")
                .WithMessage("Invalid assignment*Expected KEY=value.");
        }
        finally
        {
            DeleteDirectoryIfPresent(directory);
        }
    }

    private static IConfigurationRoot CreateConfiguration(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values!)
            .Build();
    }

    private static IConfigurationRoot CreateSupprocomConfiguration(string directory, string environmentName)
    {
        return new ConfigurationBuilder()
            .AddSupprocomSecrets(options =>
            {
                options.EnvironmentName = environmentName;
                options.File.Directory = directory;
                options.File.DevelopmentName = ".env.development";
                options.File.DevelopmentComposition = SecretFileComposition.Replace;
            })
            .Build();
    }

    private static Dictionary<string, string> CreateValidEnvironmentValues(
        Dictionary<string, string>? overrides = null)
    {
        var values = new Dictionary<string, string>
        {
            ["jwt_issuer_signing_key"] = "test-signing-key-with-at-least-32-characters",
            ["jwt_issuer_name"] = "MSAVA Tests",
            ["jwt_issuer_audience"] = "MSAVA Test Audience",
            ["admin_username"] = "test-admin",
            ["admin_password"] = "test-admin-password",
            ["postgres_basedb_user"] = "postgres-test",
            ["postgres_basedb_password"] = "postgres-test-password",
            ["postgres_basedb_host"] = "localhost",
            ["postgres_basedb_port"] = "5433",
            ["postgres_basedb_dbname"] = "msava-test",
            ["postgres_basedb_ssl_mode"] = "Disable",
            ["serilog_information_level"] = "Debug",
            ["serilog_rolling_interval"] = "Hour",
            ["serilog_retained_file_count_limit"] = "7",
            ["serilog_file_size_limit_bytes"] = "2048",
            ["serilog_roll_on_file_size_limit"] = "true"
        };

        if (overrides is null)
            return values;

        foreach (var (key, value) in overrides)
            values[key] = value;

        return values;
    }

    private static void WriteEnvironmentTemplate(string path, Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, values.Select(value => $"{value.Key}={value.Value}"));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "msava-env-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class EnvironmentVariableRestore : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new();

        public EnvironmentVariableRestore(IEnumerable<string> keys)
        {
            foreach (string key in keys)
                Track(key);
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previousValues)
                Environment.SetEnvironmentVariable(key, value);
        }

        private void Track(string key)
        {
            if (!_previousValues.ContainsKey(key))
                _previousValues[key] = Environment.GetEnvironmentVariable(key);
        }
    }
}
