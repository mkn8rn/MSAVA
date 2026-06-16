using System.Xml.Linq;
using System.Text.Json;
using System.Reflection;
using MSAVA_App.Models;
using MSAVA_BLL.Services.Interfaces;
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
    public void SharedModels_DoNotUseNullForgivingInitializers()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sharedModelsDirectory = Path.Combine(repositoryRoot, "MSAVA-Shared", "Models");
        string[] nullForgivingInitializers =
        [
            "default!",
            "null!"
        ];

        var filesWithNullForgivingInitializers = Directory
            .EnumerateFiles(sharedModelsDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine => nullForgivingInitializers.Any(marker =>
                sourceLine.Line.Contains(marker, StringComparison.Ordinal)))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        filesWithNullForgivingInitializers.Should().BeEmpty(
            "shared API contracts should use required, nullable, or concrete default values instead of hiding nullability problems");
    }

    [Test]
    public void SearchFileDataDto_DefaultRequiredStringsAreEmpty()
    {
        var dto = new SearchFileDataDTO();

        dto.FilePath.Should().BeEmpty();
        dto.MimeType.Should().BeEmpty();
        dto.FileExtension.Should().BeEmpty();
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
    public void AppCode_DoesNotExposeGlobalServiceProvider()
    {
        string repositoryRoot = FindRepositoryRoot();
        string appDirectory = Path.Combine(repositoryRoot, "MSAVA-App");

        var globalServiceProviderReferences = Directory
            .EnumerateFiles(appDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                string relativePath = Path.GetRelativePath(repositoryRoot, file);
                string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
                    !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
            })
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine => ContainsGlobalServiceProviderReference(sourceLine.Line))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        globalServiceProviderReferences.Should().BeEmpty(
            "app pages and view-models should use constructor injection or explicit route/navigation services instead of a global App service provider");
    }

    private static bool ContainsGlobalServiceProviderReference(string line)
    {
        if (line.Contains("static IServiceProvider", StringComparison.Ordinal))
            return true;

        int appServicesIndex = line.IndexOf("App.Services", StringComparison.Ordinal);
        return appServicesIndex >= 0 &&
            (appServicesIndex == 0 || line[appServicesIndex - 1] != '_');
    }

    [Test]
    public void CurrentSessionFileServices_DoNotReadRawRequestSession()
    {
        Type[] currentSessionFileServices =
        [
            typeof(FileDeduplicationService),
            typeof(FilePersistenceService)
        ];

        var rawRequestSessionMembers = currentSessionFileServices
            .SelectMany(serviceType =>
            {
                var constructorParameters = serviceType
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Where(parameter => parameter.ParameterType == typeof(IRequestSessionAccessor))
                    .Select(parameter => $"{serviceType.Name} constructor parameter '{parameter.Name}'");

                var fields = serviceType
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(field => field.FieldType == typeof(IRequestSessionAccessor))
                    .Select(field => $"{serviceType.Name} field '{field.Name}'");

                return constructorParameters.Concat(fields);
            })
            .ToList();

        rawRequestSessionMembers.Should().BeEmpty(
            "file services that make create/reference authorization decisions should use IUserSessionService for refreshed current-session state");
    }

    [Test]
    public void SourceCode_DoesNotUseEmptyCatchBlocks()
    {
        string repositoryRoot = FindRepositoryRoot();
        string emptyCatchBlock = "catch " + "{ }";

        var filesWithEmptyCatchBlocks = EnumerateSourceFiles(repositoryRoot)
            .Where(file => File.ReadAllText(file).Contains(emptyCatchBlock, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(repositoryRoot, file))
            .ToList();

        filesWithEmptyCatchBlocks.Should().BeEmpty(
            "exceptions should either propagate, be filtered, or leave diagnostic evidence when intentionally ignored");
    }

    [Test]
    public void SourceCode_DoesNotContainMojibakeText()
    {
        string repositoryRoot = FindRepositoryRoot();
        char mojibakeLead = (char)0x00E2;
        char replacementCharacter = (char)0xFFFD;

        var filesWithMojibakeText = EnumerateSourceFiles(repositoryRoot)
            .Where(file =>
            {
                string content = File.ReadAllText(file);

                return content.Contains(mojibakeLead) ||
                    content.Contains(replacementCharacter);
            })
            .Select(file => Path.GetRelativePath(repositoryRoot, file))
            .ToList();

        filesWithMojibakeText.Should().BeEmpty(
            "source comments and literals should not contain corrupted smart quotes or replacement characters");
    }

    [Test]
    public void SourceCode_DoesNotUseUnfilteredBroadExceptionCatchOutsideExceptionMiddleware()
    {
        string repositoryRoot = FindRepositoryRoot();
        string broadCatchMarker = "catch (" + nameof(Exception);
        string exceptionMiddlewarePath = Path.Combine(
            "MSAVA-API",
            "Middleware",
            "ExceptionCatcherMiddleware.cs");

        var unfilteredBroadCatches = EnumerateSourceFiles(repositoryRoot)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine =>
                sourceLine.Line.Contains(broadCatchMarker, StringComparison.Ordinal) &&
                !sourceLine.Line.Contains(" when ", StringComparison.Ordinal) &&
                !Path.GetRelativePath(repositoryRoot, sourceLine.File).Equals(
                    exceptionMiddlewarePath,
                    StringComparison.OrdinalIgnoreCase))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        unfilteredBroadCatches.Should().BeEmpty(
            "broad exception catches outside the central middleware should use filters that preserve critical failures");
    }

    [Test]
    public void ProductionSource_DoesNotResolveServicesFromHttpContextRequestServices()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] requestServiceLookupMarkers =
        [
            "RequestServices.GetRequiredService",
            "RequestServices.GetService"
        ];

        var requestServiceLookups = EnumerateProductionSourceFiles(repositoryRoot)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine => requestServiceLookupMarkers.Any(marker =>
                sourceLine.Line.Contains(marker, StringComparison.Ordinal)))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        requestServiceLookups.Should().BeEmpty(
            "production request handlers should receive dependencies through constructors or InvokeAsync parameters instead of resolving them from HttpContext.RequestServices");
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

    private static IEnumerable<string> EnumerateSourceFiles(string repositoryRoot)
    {
        string[] excludedSegments = [".git", "bin", "obj", "docs"];

        return Directory
            .EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                string relativePath = Path.GetRelativePath(repositoryRoot, file);
                string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return !excludedSegments.Any(excludedSegment =>
                    segments.Contains(excludedSegment, StringComparer.OrdinalIgnoreCase));
            });
    }

    private static IEnumerable<string> EnumerateProductionSourceFiles(string repositoryRoot)
    {
        string[] productionDirectories =
        [
            "MSAVA-API",
            "MSAVA-App",
            "MSAVA-BLL",
            "MSAVA-DAL",
            "MSAVA-INF",
            "MSAVA-Shared"
        ];

        return productionDirectories.SelectMany(directory =>
        {
            string absoluteDirectory = Path.Combine(repositoryRoot, directory);

            return Directory.Exists(absoluteDirectory)
                ? Directory.EnumerateFiles(absoluteDirectory, "*.cs", SearchOption.AllDirectories)
                    .Where(file =>
                    {
                        string relativePath = Path.GetRelativePath(repositoryRoot, file);
                        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
                            !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
                    })
                : [];
        });
    }
}
