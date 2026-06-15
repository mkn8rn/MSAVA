using System.Xml.Linq;

namespace MSAVA_App.Tests;

public class ProjectConfigurationTests
{
    [Test]
    public void InfrastructureProject_DoesNotCopyDotEnvFilesToBuildOutput()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "MSAVA-INF", "MSAVA-INF.csproj");
        var project = XDocument.Load(projectPath);

        var dotenvCopyItems = project
            .Descendants("None")
            .Where(item => IsDotEnvItem((string?)item.Attribute("Update")))
            .Where(item => item.Elements("CopyToOutputDirectory").Any())
            .Select(item => (string?)item.Attribute("Update"))
            .ToList();

        dotenvCopyItems.Should().BeEmpty("dotenv files should not be copied into bin directories");
    }

    [Test]
    public void SharedModels_DoNotExposeBearerTokenFields()
    {
        string sharedModelsDirectory = Path.Combine(FindRepositoryRoot(), "MSAVA-Shared", "Models");

        var filesWithBearerTokenFields = Directory
            .EnumerateFiles(sharedModelsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("BearerToken", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        filesWithBearerTokenFields.Should().BeEmpty(
            "shared API contracts should not carry provider bearer tokens or other credential-shaped fields");
    }

    private static bool IsDotEnvItem(string? itemPath)
    {
        return string.Equals(itemPath, ".env", StringComparison.OrdinalIgnoreCase)
            || string.Equals(itemPath, ".env.development", StringComparison.OrdinalIgnoreCase);
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
}
