using MSAVA_INF.Environment;
using MSAVA_INF.Models;
using Npgsql;
using Serilog;
using Serilog.Events;

namespace MSAVA_App.Tests;

public class PostgresConnectionStringFactoryTests
{
    [Test]
    public void CreateBaseDbConnectionString_PreservesSpecialCharactersInConnectionValues()
    {
        var values = CreateValues(
            user: "msava;user",
            password: "p@ss;word=with spaces",
            host: "db.internal",
            database: "msava;prod");

        string connectionString = PostgresConnectionStringFactory.CreateBaseDbConnectionString(values);

        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Host.Should().Be("db.internal");
        parsed.Port.Should().Be(5432);
        parsed.Database.Should().Be("msava;prod");
        parsed.Username.Should().Be("msava;user");
        parsed.Password.Should().Be("p@ss;word=with spaces");
        parsed.SslMode.Should().Be(SslMode.Require);
    }

    [TestCase("host", "PostgresBaseDbHost must be configured.")]
    [TestCase("database", "PostgresBaseDbDbName must be configured.")]
    [TestCase("user", "PostgresBaseDbUser must be configured.")]
    [TestCase("password", "PostgresBaseDbPassword must be configured.")]
    [TestCase("sslMode", "PostgresBaseDbSslMode must be configured.")]
    public void CreateBaseDbConnectionString_RejectsBlankRequiredStringValues(
        string blankField,
        string expectedMessage)
    {
        var values = blankField switch
        {
            "host" => CreateValues(host: " "),
            "database" => CreateValues(database: " "),
            "user" => CreateValues(user: " "),
            "password" => CreateValues(password: " "),
            "sslMode" => CreateValues(sslMode: " "),
            _ => throw new ArgumentOutOfRangeException(nameof(blankField), blankField, null)
        };

        Action act = () => PostgresConnectionStringFactory.CreateBaseDbConnectionString(values);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [TestCase(0)]
    [TestCase(65536)]
    public void CreateBaseDbConnectionString_RejectsOutOfRangePort(int port)
    {
        var values = CreateValues(port: port);

        Action act = () => PostgresConnectionStringFactory.CreateBaseDbConnectionString(values);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("PostgresBaseDbPort must be between 1 and 65535.");
    }

    [Test]
    public void CreateBaseDbConnectionString_RejectsInvalidSslMode()
    {
        var values = CreateValues(sslMode: "DefinitelyNotSsl");

        Action act = () => PostgresConnectionStringFactory.CreateBaseDbConnectionString(values);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("PostgresBaseDbSslMode could not be parsed as SslMode: 'DefinitelyNotSsl'.");
    }

    private static LocalEnvironmentValues CreateValues(
        string user = "postgres",
        string password = "postgres-password",
        string host = "localhost",
        int port = 5432,
        string database = "msava",
        string sslMode = "Require")
    {
        return new LocalEnvironmentValues
        {
            JwtIssuerSigningKey = "test-signing-key-with-at-least-32-characters",
            JwtIssuerName = "MSAVA Tests",
            JwtIssuerAudience = "MSAVA Test Audience",
            AdminUsername = "admin",
            AdminPassword = "admin-password",
            PostgresBaseDbUser = user,
            PostgresBaseDbPassword = password,
            PostgresBaseDbHost = host,
            PostgresBaseDbPort = port,
            PostgresBaseDbDbName = database,
            PostgresBaseDbSslMode = sslMode,
            SerilogInformationLevel = LogEventLevel.Information,
            SerilogRollingInterval = RollingInterval.Day,
            SerilogRetainedFileCountLimit = 31,
            SerilogFileSizeLimitBytes = 10_485_760,
            SerilogRollOnFileSizeLimit = true
        };
    }
}
