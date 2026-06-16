using MSAVA_INF.Environment;
using Serilog;
using Serilog.Events;

namespace MSAVA_App.Tests;

[NonParallelizable]
public class LocalEnvironmentTests
{
    [Test]
    public void Constructor_ReadsRequiredValuesFromProcessEnvironment()
    {
        var values = CreateValidEnvironmentValues();

        using var restore = new EnvironmentVariableRestore(values.Keys);
        SetUpperCaseEnvironmentValues(values);

        var env = new LocalEnvironment();

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
    public void Constructor_ReadsDevelopmentEnvFileFromRepositoryInfrastructureDirectory()
    {
        var values = CreateValidEnvironmentValues();
        string repositoryRoot = Path.Combine(Path.GetTempPath(), "msava-env-tests", Guid.NewGuid().ToString("N"));
        string infrastructureDirectory = Path.Combine(repositoryRoot, "MSAVA-INF");
        string apiDirectory = Path.Combine(repositoryRoot, "MSAVA-API");
        string originalCurrentDirectory = Directory.GetCurrentDirectory();

        using var restore = new EnvironmentVariableRestore(values.Keys.Append("ASPNETCORE_ENVIRONMENT"));
        ClearEnvironmentValues(values.Keys);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        try
        {
            Directory.CreateDirectory(infrastructureDirectory);
            Directory.CreateDirectory(apiDirectory);
            File.WriteAllLines(
                Path.Combine(infrastructureDirectory, ".env.development"),
                values.Select(value => $"{value.Key}={value.Value}"));

            Directory.SetCurrentDirectory(apiDirectory);

            var env = new LocalEnvironment();

            env.Values.JwtIssuerName.Should().Be("MSAVA Tests");
            env.Values.AdminUsername.Should().Be("test-admin");
            env.Values.PostgresBaseDbDbName.Should().Be("msava-test");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);

            if (Directory.Exists(repositoryRoot))
                Directory.Delete(repositoryRoot, recursive: true);
        }
    }

    [Test]
    public void Constructor_IgnoresDevelopmentEnvFileInBuildOutputDirectory()
    {
        var values = CreateValidEnvironmentValues();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "msava-env-tests", Guid.NewGuid().ToString("N"));
        string outputDirectory = Path.Combine(temporaryRoot, "bin", "Debug", "net10.0");
        string originalCurrentDirectory = Directory.GetCurrentDirectory();

        using var restore = new EnvironmentVariableRestore(values.Keys.Append("ASPNETCORE_ENVIRONMENT"));
        ClearEnvironmentValues(values.Keys);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllLines(
                Path.Combine(outputDirectory, ".env.development"),
                values.Select(value => $"{value.Key}={value.Value}"));

            Directory.SetCurrentDirectory(outputDirectory);

            Action act = () => _ = new LocalEnvironment();

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("Required configuration value 'jwt_issuer_signing_key' is missing or empty.*");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);

            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Test]
    public void GetSigningKeyBytes_RejectsSigningKeyShorterThan32Bytes()
    {
        var values = CreateValidEnvironmentValues();
        values["jwt_issuer_signing_key"] = "too-short";

        using var restore = new EnvironmentVariableRestore(values.Keys);
        SetUpperCaseEnvironmentValues(values);

        var env = new LocalEnvironment();

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

    private static Dictionary<string, string> CreateValidEnvironmentValues()
    {
        return new Dictionary<string, string>
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
    }

    private static void SetUpperCaseEnvironmentValues(Dictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            Environment.SetEnvironmentVariable(key, null);
            Environment.SetEnvironmentVariable(key.ToUpperInvariant(), value);
        }
    }

    private static void ClearEnvironmentValues(IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            Environment.SetEnvironmentVariable(key, null);
            Environment.SetEnvironmentVariable(key.ToUpperInvariant(), null);
        }
    }

    private sealed class EnvironmentVariableRestore : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new();

        public EnvironmentVariableRestore(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                Track(key);
                Track(key.ToUpperInvariant());
            }
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
