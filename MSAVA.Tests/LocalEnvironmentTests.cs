using MSAVA_INF.Environment;
using Serilog;
using Serilog.Events;

namespace MSAVA_App.Tests;

public class LocalEnvironmentTests
{
    [Test]
    public void Constructor_ReadsRequiredValuesFromProcessEnvironment()
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

        using var restore = new EnvironmentVariableRestore(values.Keys);

        foreach (var (key, value) in values)
        {
            Environment.SetEnvironmentVariable(key, null);
            Environment.SetEnvironmentVariable(key.ToUpperInvariant(), value);
        }

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
