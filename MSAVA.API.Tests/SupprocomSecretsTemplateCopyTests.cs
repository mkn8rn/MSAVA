using System.Diagnostics;

namespace MSAVA_API.Tests;

[NonParallelizable]
public class SupprocomSecretsTemplateCopyTests
{
    [Test]
    public void ApiBuildOutput_ContainsSupprocomTemplatesButNoActiveEnvFiles()
    {
        string outputDirectory = Path.Combine(
            FindRepositoryRoot(),
            "MSAVA-API",
            "bin",
            CurrentBuildConfiguration(),
            CurrentTargetFramework());
        string environmentDirectory = Path.Combine(outputDirectory, "Environment");

        AssertEnvironmentTemplateState(environmentDirectory);
    }

    [Test]
    public async Task ApiPublishOutput_ContainsSupprocomTemplatesButNoActiveEnvFiles()
    {
        string repositoryRoot = FindRepositoryRoot();
        string publishDirectory = Path.Combine(
            Path.GetTempPath(),
            "msava-publish-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(publishDirectory);
            await RunDotnetPublishAsync(repositoryRoot, publishDirectory);

            AssertEnvironmentTemplateState(Path.Combine(publishDirectory, "Environment"));
        }
        finally
        {
            if (Directory.Exists(publishDirectory))
                Directory.Delete(publishDirectory, recursive: true);
        }
    }

    private static void AssertEnvironmentTemplateState(string environmentDirectory)
    {
        Directory.Exists(environmentDirectory).Should().BeTrue();
        File.Exists(Path.Combine(environmentDirectory, ".env.template")).Should().BeTrue();
        File.Exists(Path.Combine(environmentDirectory, ".env.development.template")).Should().BeTrue();
        File.Exists(Path.Combine(environmentDirectory, ".dev.env.template")).Should().BeFalse();

        Directory
            .EnumerateFiles(environmentDirectory)
            .Select(Path.GetFileName)
            .Where(IsActiveEnvFile)
            .Should()
            .BeEmpty("Supprocom.Secrets build assets must never copy active env files");
    }

    private static async Task RunDotnetPublishAsync(string repositoryRoot, string publishDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repositoryRoot
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "MSAVA-API", "MSAVA-API.csproj"));
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-p:UseAppHost=false");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(publishDirectory);
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet publish.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Timed out while publishing MSAVA-API for Supprocom template verification.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet publish failed with exit code {process.ExitCode} while verifying Supprocom template publishing.");
    }

    private static bool IsActiveEnvFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (fileName.Equals(".env.template", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".env.development.template", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".dev.env.template", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".env.development", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".dev.env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MSAVA.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MSAVA repository root.");
    }

    private static string CurrentBuildConfiguration()
    {
        string[] segments = TestContext.CurrentContext.TestDirectory.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        int binIndex = Array.FindIndex(
            segments,
            segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));

        if (binIndex >= 0 && binIndex + 1 < segments.Length)
            return segments[binIndex + 1];

        return "Debug";
    }

    private static string CurrentTargetFramework() =>
        new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Name;
}
