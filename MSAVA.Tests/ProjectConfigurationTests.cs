using System.Xml.Linq;
using System.Text.Json;
using MSAVA_App.Models;
using MSAVA_BLL.Services.Files;
using MSAVA_Shared.Models;

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
    public void InfrastructureProject_DoesNotCopyRuntimeDataPlaceholdersToBuildOutput()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "MSAVA-INF", "MSAVA-INF.csproj");
        var project = XDocument.Load(projectPath);

        var runtimeDataCopyItems = project
            .Descendants("None")
            .Where(item => ((string?)item.Attribute("Update"))?.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase) == true)
            .Where(item => item.Elements("CopyToOutputDirectory").Any())
            .Select(item => (string?)item.Attribute("Update"))
            .ToList();

        runtimeDataCopyItems.Should().BeEmpty(
            "runtime file storage should be created by code rather than copied from tracked placeholders");
    }

    [Test]
    public void ApiProject_DoesNotCopyRuntimeLogPlaceholdersToBuildOutput()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "MSAVA-API", "MSAVA-API.csproj");
        var project = XDocument.Load(projectPath);

        var runtimeLogCopyItems = project
            .Descendants("None")
            .Where(item => ((string?)item.Attribute("Update"))?.StartsWith("Logs\\", StringComparison.OrdinalIgnoreCase) == true)
            .Where(item => item.Elements("CopyToOutputDirectory").Any())
            .Select(item => (string?)item.Attribute("Update"))
            .ToList();

        runtimeLogCopyItems.Should().BeEmpty(
            "runtime logs should be created by the application rather than copied from tracked placeholders");
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

    [Test]
    public void SharedProject_DoesNotReferenceAspNetCorePackages()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "MSAVA-Shared", "MSAVA-Shared.csproj");
        var project = XDocument.Load(projectPath);

        var aspNetCorePackageReferences = project
            .Descendants("PackageReference")
            .Select(item => (string?)item.Attribute("Include"))
            .Where(include => include?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true)
            .ToList();

        aspNetCorePackageReferences.Should().BeEmpty(
            "shared contracts should stay independent of server-only ASP.NET abstractions");
    }

    [Test]
    public void SharedModels_DoNotExposeFormFileTypes()
    {
        string sharedModelsDirectory = Path.Combine(FindRepositoryRoot(), "MSAVA-Shared", "Models");

        var filesWithFormFileTypes = Directory
            .EnumerateFiles(sharedModelsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("IFormFile", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        filesWithFormFileTypes.Should().BeEmpty(
            "shared contracts should not expose ASP.NET multipart binding types");
    }

    [Test]
    public void AppCodeBehind_DoesNotReflectOverDataContextShape()
    {
        string presentationDirectory = Path.Combine(FindRepositoryRoot(), "MSAVA-App", "Presentation");

        var filesWithDataContextReflection = Directory
            .EnumerateFiles(presentationDirectory, "*.xaml.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                string content = File.ReadAllText(file);
                return content.Contains("GetType().GetProperty", StringComparison.Ordinal) &&
                    content.Contains("DataContext", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToList();

        filesWithDataContextReflection.Should().BeEmpty(
            "code-behind should use typed view-model contracts instead of reflection over DataContext wrappers");
    }

    [Test]
    public void AppSettings_ApiClientKeysMatchBoundOptions()
    {
        string appDirectory = Path.Combine(FindRepositoryRoot(), "MSAVA-App");
        var supportedKeys = typeof(ApiClientOptions)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unsupportedKeys = Directory
            .EnumerateFiles(appDirectory, "appsettings*.json", SearchOption.TopDirectoryOnly)
            .SelectMany(file => GetApiClientKeys(file)
                .Where(key => !supportedKeys.Contains(key))
                .Select(key => $"{Path.GetFileName(file)}:{ApiClientOptions.SectionName}:{key}"))
            .ToList();

        unsupportedKeys.Should().BeEmpty(
            "committed ApiClient configuration should not contain keys that no bound option reads");
    }

    [Test]
    public void FileUploadFormFields_MatchServerFormDtoPropertyNames()
    {
        var serverDto = typeof(SaveFileFromFormFileDTO);

        serverDto.GetProperty(FileUploadFormFields.FileName).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.FileExtension).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.FormFile).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.AccessGroupId).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.Tags).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.Categories).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.Description).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.PublicViewing).Should().NotBeNull();
        serverDto.GetProperty(FileUploadFormFields.PublicDownload).Should().NotBeNull();
    }

    private static bool IsDotEnvItem(string? itemPath)
    {
        return string.Equals(itemPath, ".env", StringComparison.OrdinalIgnoreCase)
            || string.Equals(itemPath, ".env.development", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetApiClientKeys(string jsonFile)
    {
        using var stream = File.OpenRead(jsonFile);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty(ApiClientOptions.SectionName, out var apiClient) ||
            apiClient.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in apiClient.EnumerateObject())
            yield return property.Name;
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
