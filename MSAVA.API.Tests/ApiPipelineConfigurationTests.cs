using System;
using System.IO;

namespace MSAVA_API.Tests;

public class ApiPipelineConfigurationTests
{
    [Test]
    public void Program_UsesHstsOutsideDevelopmentBeforeHttpsRedirection()
    {
        string programText = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "MSAVA-API", "Program.cs"));
        string normalizedProgramText = programText.Replace("\r\n", "\n");

        normalizedProgramText.Should().Contain("else\n{\n    app.UseHsts();\n}");

        int developmentBranchIndex = normalizedProgramText.IndexOf("if (app.Environment.IsDevelopment())", StringComparison.Ordinal);
        int hstsIndex = normalizedProgramText.IndexOf("app.UseHsts();", StringComparison.Ordinal);
        int httpsRedirectionIndex = normalizedProgramText.IndexOf("app.UseHttpsRedirection();", StringComparison.Ordinal);

        developmentBranchIndex.Should().BeGreaterThanOrEqualTo(0);
        hstsIndex.Should().BeGreaterThan(developmentBranchIndex);
        httpsRedirectionIndex.Should().BeGreaterThan(hstsIndex);
    }

    [Test]
    public void Program_CreatesPublicFilesDirectoryBeforeStaticFileProvider()
    {
        string programText = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "MSAVA-API", "Program.cs"));
        string normalizedProgramText = programText.Replace("\r\n", "\n");

        normalizedProgramText.Should().Contain("string publicFilesDirectory = Path.Combine(AppContext.BaseDirectory, \"Data\");");
        normalizedProgramText.Should().Contain("Directory.CreateDirectory(publicFilesDirectory);");
        normalizedProgramText.Should().Contain("FileProvider = new PhysicalFileProvider(publicFilesDirectory)");

        int directoryCreateIndex = normalizedProgramText.IndexOf(
            "Directory.CreateDirectory(publicFilesDirectory);",
            StringComparison.Ordinal);
        int staticFilesIndex = normalizedProgramText.IndexOf("app.UseStaticFiles(new StaticFileOptions", StringComparison.Ordinal);

        directoryCreateIndex.Should().BeGreaterThanOrEqualTo(0);
        staticFilesIndex.Should().BeGreaterThan(directoryCreateIndex);
    }

    [Test]
    public void Program_CreatesLogDirectoryBeforeSerilogFileSink()
    {
        string programText = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "MSAVA-API", "Program.cs"));
        string normalizedProgramText = programText.Replace("\r\n", "\n");

        normalizedProgramText.Should().Contain("string logDirectory = Path.Combine(AppContext.BaseDirectory, \"Logs\");");
        normalizedProgramText.Should().Contain("Directory.CreateDirectory(logDirectory);");
        normalizedProgramText.Should().Contain("path: Path.Combine(logDirectory, \"serilog-.txt\")");

        int directoryCreateIndex = normalizedProgramText.IndexOf(
            "Directory.CreateDirectory(logDirectory);",
            StringComparison.Ordinal);
        int fileSinkIndex = normalizedProgramText.IndexOf(
            "path: Path.Combine(logDirectory, \"serilog-.txt\")",
            StringComparison.Ordinal);

        directoryCreateIndex.Should().BeGreaterThanOrEqualTo(0);
        fileSinkIndex.Should().BeGreaterThan(directoryCreateIndex);
    }

    [Test]
    public void Program_DoesNotRegisterGlobalSwaggerSecurityRequirement()
    {
        string programText = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "MSAVA-API", "Program.cs"));

        programText.Should().Contain("c.AddSecurityDefinition(\"Bearer\"");
        programText.Should().NotContain("c.AddSecurityRequirement(",
            "anonymous actions should be able to omit bearer auth through AuthorizeCheckOperationFilter");
    }

    [Test]
    public void Program_RegistersExceptionCatcherBeforeRoutingAndStaticFiles()
    {
        string programText = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "MSAVA-API", "Program.cs"));
        string normalizedProgramText = programText.Replace("\r\n", "\n");

        int exceptionMiddlewareIndex = normalizedProgramText.IndexOf(
            "app.UseMiddleware<ExceptionCatcherMiddleware>();",
            StringComparison.Ordinal);
        int routingIndex = normalizedProgramText.IndexOf("app.UseRouting();", StringComparison.Ordinal);
        int publicFileMiddlewareIndex = normalizedProgramText.IndexOf(
            "app.UseMiddleware<PublicFileAccessMiddleware>",
            StringComparison.Ordinal);
        int staticFilesIndex = normalizedProgramText.IndexOf("app.UseStaticFiles(new StaticFileOptions", StringComparison.Ordinal);
        int authenticationIndex = normalizedProgramText.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);

        exceptionMiddlewareIndex.Should().BeGreaterThanOrEqualTo(0);
        routingIndex.Should().BeGreaterThan(exceptionMiddlewareIndex);
        publicFileMiddlewareIndex.Should().BeGreaterThan(exceptionMiddlewareIndex);
        staticFilesIndex.Should().BeGreaterThan(exceptionMiddlewareIndex);
        authenticationIndex.Should().BeGreaterThan(exceptionMiddlewareIndex);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MSAVA-API", "Program.cs")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MSAVA repository root.");
    }
}
