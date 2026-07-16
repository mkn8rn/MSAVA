using System.Xml.Linq;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Diagnostics;
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
    public void ApiProject_ReferencesSupprocomSecretsPackage()
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), "MSAVA-API", "MSAVA-API.csproj");
        var project = XDocument.Load(projectPath);

        var packageReferences = project
            .Descendants("PackageReference")
            .Select(item => (string?)item.Attribute("Include"))
            .ToList();

        packageReferences.Should().Contain("Supprocom.Secrets",
            "API startup owns the Supprocom.Secrets configuration source registration");
    }

    [Test]
    public void CentralPackageVersions_PinSupprocomSecretsToExactPublicVersion()
    {
        string packagePath = Path.Combine(FindRepositoryRoot(), "Directory.Packages.props");
        var packages = XDocument.Load(packagePath);

        var version = packages
            .Descendants("PackageVersion")
            .Single(item => string.Equals(
                (string?)item.Attribute("Include"),
                "Supprocom.Secrets",
                StringComparison.Ordinal))
            .Attribute("Version")
            ?.Value;

        version.Should().Be("0.1.2",
            "MSAVA must consume the published package requested by CO, not a local source or later package");
    }

    [Test]
    public void ApiProject_UsesSupprocomEnvironmentTemplateConvention()
    {
        string environmentDirectory = Path.Combine(FindRepositoryRoot(), "MSAVA-API", "Environment");

        File.Exists(Path.Combine(environmentDirectory, ".env.template")).Should().BeTrue();
        File.Exists(Path.Combine(environmentDirectory, ".env.development.template")).Should().BeTrue();
        File.Exists(Path.Combine(environmentDirectory, ".dev.env.template")).Should().BeFalse(
            "MSAVA development configuration is a .env.development replacement, not a .dev.env overlay");
        File.Exists(Path.Combine(environmentDirectory, ".env")).Should().BeFalse();
        File.Exists(Path.Combine(environmentDirectory, ".env.development")).Should().BeFalse();
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
    public void ProductionSource_DoesNotUseNullForgivingSuppressions()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] nullForgivingSuppressions =
        [
            "default!",
            "null!"
        ];

        var suppressions = EnumerateProductionSourceFiles(repositoryRoot)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine => nullForgivingSuppressions.Any(marker =>
                sourceLine.Line.Contains(marker, StringComparison.Ordinal)))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        suppressions.Should().BeEmpty(
            "production code should express nullability with required members, nullable references, or real defaults instead of suppressing compiler checks");
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
    public void AppSource_DoesNotUseAdHocDiagnosticWrites()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] diagnosticWrites =
        [
            "Debug.WriteLine",
            "Trace.WriteLine",
            "Console.WriteLine",
            "Console.Error.WriteLine"
        ];

        var references = EnumerateAppSourceFiles(repositoryRoot)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine => diagnosticWrites.Any(marker =>
                sourceLine.Line.Contains(marker, StringComparison.Ordinal)))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        references.Should().BeEmpty(
            "app diagnostics should go through injected loggers so failures are visible in the configured logging pipeline");
    }

    [Test]
    public void AppSource_DoesNotUseIdentifierNullForgivingSuppressions()
    {
        string repositoryRoot = FindRepositoryRoot();
        var nullForgivingIdentifier = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*!(?!=)", RegexOptions.Compiled);

        var suppressions = EnumerateAppSourceFiles(repositoryRoot)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine => nullForgivingIdentifier.IsMatch(sourceLine.Line))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        suppressions.Should().BeEmpty(
            "app code should use nullability flow, nullable contracts, or explicit validation helpers instead of suffix null-forgiving operators");
    }

    [Test]
    public void AppCode_DoesNotExposeGlobalServiceProvider()
    {
        string repositoryRoot = FindRepositoryRoot();

        var globalServiceProviderReferences = EnumerateAppSourceFiles(repositoryRoot)
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

    [Test]
    public void ShellModel_UsesLoggedAsyncShellTaskGuard()
    {
        string shellModelPath = Path.Combine(
            FindRepositoryRoot(),
            "MSAVA-App",
            "Presentation",
            "ShellModel.cs");
        string source = File.ReadAllText(shellModelPath);

        source.Should().Contain("ILogger<ShellModel>",
            "shell startup and logout navigation failures should be visible in the configured logging pipeline");
        source.Should().Contain("internal Task InitializationTask { get; }",
            "the constructor-started initialization task should remain observable for diagnostics and tests");
        source.Should().Contain("RunShellTaskAsync",
            "fire-and-forget shell navigation should go through one guarded helper");
        source.Should().Contain("CriticalExceptionPolicy.ContainsCriticalException(ex)",
            "the shell task guard should log recoverable failures without swallowing critical runtime failures");
        source.Should().NotContain("_ = InitializeAsync();",
            "constructor-started initialization should not discard the raw task");
        source.Should().NotContain("async void",
            "ShellModel event handlers should use logged task helpers instead of unobserved async void methods");
    }

    [Test]
    public void FileManagementPage_UsesLoggedAsyncPageTaskGuard()
    {
        string pagePath = Path.Combine(
            FindRepositoryRoot(),
            "MSAVA-App",
            "Presentation",
            "FileManagement",
            "FileManagementPage.xaml.cs");
        string source = File.ReadAllText(pagePath);

        source.Should().Contain("RunPageTaskAsync",
            "file-management event handlers should route asynchronous work through one logged helper");
        source.Should().Contain("CriticalExceptionPolicy.ContainsCriticalException(ex)",
            "recoverable UI task failures should be logged without swallowing critical runtime failures");
        source.Should().Contain("StartInitialRefresh",
            "Loaded and DataContextChanged should share the same initial-refresh flow");
        source.Should().NotContain("async void",
            "file-management event handlers should not use unobserved async void methods");
        source.Should().NotContain("_ = vm.GoToMainAsync();",
            "navigation tasks should go through the page task guard");
        source.Should().NotContain("_ = vm.StartAddAsync();",
            "add-mode tasks should go through the page task guard");
        source.Should().NotContain("await vm.UploadAsync();",
            "upload failures should be logged by the page task guard");
        source.Should().NotContain("await vm.SaveAndRefreshAsync();",
            "initial refresh failures should be logged by the page task guard");
    }

    [Test]
    public void FileManagementModel_LogsRecoverableAutoDismissFailures()
    {
        string modelPath = Path.Combine(
            FindRepositoryRoot(),
            "MSAVA-App",
            "Presentation",
            "FileManagement",
            "FileManagementModel.cs");
        string source = File.ReadAllText(modelPath);

        source.Should().Contain("ILogger<FileManagementModel>",
            "background upload-result dismissal failures should be visible in the configured logging pipeline");
        source.Should().Contain("NullLogger<FileManagementModel>.Instance",
            "tests and fallback construction should have a deterministic logger when DI does not provide one");
        source.Should().Contain("CriticalExceptionPolicy.ContainsCriticalException(ex)",
            "recoverable auto-dismiss failures should be logged without swallowing critical runtime failures");
        source.Should().Contain("Failed to dismiss upload result after delay.",
            "the logged failure should identify the background auto-dismiss task");
        source.Should().Contain("catch (OperationCanceledException)",
            "normal dismissal cancellation should remain a quiet non-error path");
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
    public void SourceCode_DoesNotUseSwallowingBareCatchBlocks()
    {
        string repositoryRoot = FindRepositoryRoot();

        var swallowingBareCatches = EnumerateSourceFiles(repositoryRoot)
            .SelectMany(file => FindSwallowingBareCatchBlocks(file, repositoryRoot))
            .ToList();

        swallowingBareCatches.Should().BeEmpty(
            "parameterless catches that intentionally recover should still log or filter specific exceptions, while cleanup catches should rethrow");
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
    public void ProductionSource_HasSingleCriticalExceptionPolicyDefinition()
    {
        string repositoryRoot = FindRepositoryRoot();

        var definitions = EnumerateProductionSourceFiles(repositoryRoot)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine =>
                sourceLine.Line.Contains("class CriticalExceptionPolicy", StringComparison.Ordinal) ||
                sourceLine.Line.Contains("class AppCriticalExceptionPolicy", StringComparison.Ordinal))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        definitions.Should().BeEquivalentTo(
            ["MSAVA-Shared\\Diagnostics\\CriticalExceptionPolicy.cs:3"],
            "critical exception classification should have one shared implementation so API, BLL, and app filters cannot drift");
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
    public void ProductionServiceLogWrites_PassCallerCancellationToken()
    {
        string repositoryRoot = FindRepositoryRoot();

        var logWritesWithoutCancellation = EnumerateProductionSourceFiles(repositoryRoot)
            .Where(file => !Path.GetFileName(file).Equals(
                "ServiceLogger.cs",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => FindServiceLoggerWritesWithoutCancellation(file, repositoryRoot))
            .ToList();

        logWritesWithoutCancellation.Should().BeEmpty(
            "audit-log persistence should observe the same cancellation token as the operation it records");
    }

    [Test]
    public void ProviderImportServices_DoNotLogTemporaryPathsOrFfmpegArguments()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] providerImportFiles =
        [
            Path.Combine(repositoryRoot, "MSAVA-BLL", "Services", "Import", "GoogleDriveImportService.cs"),
            Path.Combine(repositoryRoot, "MSAVA-BLL", "Services", "Import", "OneDriveImportService.cs"),
            Path.Combine(repositoryRoot, "MSAVA-BLL", "Services", "Import", "YouTubeImportService.cs")
        ];
        string[] unsafeLogSnippets =
        [
            "temp path",
            "Downloading video to {",
            "Downloading audio to {",
            "Starting FFmpeg mux: {",
            "string.Join(' ', psi.ArgumentList)"
        ];

        var unsafeLogLines = providerImportFiles
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = line,
                    LineNumber = index + 1
                }))
            .Where(sourceLine => unsafeLogSnippets.Any(snippet =>
                sourceLine.Line.Contains(snippet, StringComparison.Ordinal)))
            .Select(sourceLine => $"{Path.GetRelativePath(repositoryRoot, sourceLine.File)}:{sourceLine.LineNumber}")
            .ToList();

        unsafeLogLines.Should().BeEmpty(
            "provider import progress logs should not reveal server-local temp paths or complete FFmpeg command lines");
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
    public async Task GitIndex_DoesNotTrackGeneratedRuntimeOrSecretArtifacts()
    {
        string repositoryRoot = FindRepositoryRoot();

        var unsafeTrackedFiles = (await GetTrackedFilesAsync(repositoryRoot))
            .Where(IsGeneratedRuntimeOrSecretArtifact)
            .ToList();

        unsafeTrackedFiles.Should().BeEmpty(
            "build outputs, runtime data, local databases, real dotenv files, internal reports, and user-specific IDE files should stay out of source control");
    }

    [Test]
    public void GlobalJson_UsesStableSdkWithExplicitFeatureBandRollForward()
    {
        string globalJsonPath = Path.Combine(FindRepositoryRoot(), "global.json");
        using var stream = File.OpenRead(globalJsonPath);
        using var document = JsonDocument.Parse(stream);

        JsonElement sdk = document.RootElement.GetProperty("sdk");
        string sdkVersion = sdk.GetProperty("version").GetString() ?? string.Empty;

        sdkVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+$",
            "the repository SDK pin should not select prerelease SDK builds");
        sdkVersion.Should().StartWith("10.0.",
            "the projects target net10.0 and CI installs the 10.0 SDK line");
        sdk.GetProperty("rollForward").GetString().Should().Be("latestFeature",
            "CI installs 10.0.x and local machines may have newer .NET 10 feature bands than the baseline");
        sdk.GetProperty("allowPrerelease").GetBoolean().Should().BeFalse(
            "production builds should not opt into prerelease SDKs by default");
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

    [Test]
    public void FileCreationSaveDtos_ImplementSharedMetadataContract()
    {
        Type metadataContract = typeof(IFileCreationMetadataRequest);

        typeof(SaveFileFromStreamDTO).Should().BeAssignableTo(metadataContract);
        typeof(SaveFileFromFetchDTO).Should().BeAssignableTo(metadataContract);
        typeof(SaveFileFromUrlDTO).Should().BeAssignableTo(metadataContract);
        typeof(SaveFileFromFormFileDTO).Should().BeAssignableTo(metadataContract);
    }

    [Test]
    public void FileImportDtos_ImplementSharedSupplementalMetadataContract()
    {
        Type supplementalMetadataContract = typeof(IFileSupplementalMetadataRequest);

        typeof(FetchFileGoogleDriveDTO).Should().BeAssignableTo(supplementalMetadataContract);
        typeof(FetchFileFromOneDriveDTO).Should().BeAssignableTo(supplementalMetadataContract);
        typeof(FetchFileYouTubeDTO).Should().BeAssignableTo(supplementalMetadataContract);
    }

    [Test]
    public void ContinuousIntegration_RunsBothUnitTestProjects()
    {
        string workflowPath = Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "ci.yml");
        string workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain(
            "dotnet test ./MSAVA.Tests/MSAVA.Tests.csproj",
            "CI should run the main application and BLL test project");
        workflow.Should().Contain(
            "dotnet test ./MSAVA.API.Tests/MSAVA.API.Tests.csproj",
            "CI should run the API authorization, middleware, and controller tests");
    }

    [Test]
    public void ContinuousIntegration_BoundsBuildAndTestSteps()
    {
        string workflowPath = Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "ci.yml");
        string workflow = File.ReadAllText(workflowPath);

        var untimedSteps = FindUntimedBuildOrTestWorkflowSteps(workflow).ToList();

        untimedSteps.Should().BeEmpty(
            "CI build and test commands should have explicit step timeouts so blocked agents or hosted runners fail predictably");
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

    private static async Task<IReadOnlyList<string>> GetTrackedFilesAsync(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git to inspect tracked files.");

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Timed out while inspecting tracked files with git ls-files.");
        }

        string output = await outputTask;
        string error = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git ls-files failed with exit code {process.ExitCode}: {error}");

        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeGitPath)
            .ToList();
    }

    private static string NormalizeGitPath(string path) =>
        path.Replace('\\', '/');

    private static bool IsGeneratedRuntimeOrSecretArtifact(string trackedPath)
    {
        string fileName = Path.GetFileName(trackedPath);

        if (trackedPath.Split('/').Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (trackedPath.StartsWith("MSAVA-API/Logs/", StringComparison.OrdinalIgnoreCase) ||
            trackedPath.StartsWith("MSAVA-INF/Data/", StringComparison.OrdinalIgnoreCase) ||
            trackedPath.StartsWith("docs/internal/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".suo", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".user", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsActiveDotenvFileName(fileName))
        {
            return true;
        }

        return false;
    }

    private static bool IsActiveDotenvFileName(string fileName)
    {
        if (IsTrackedDotenvTemplate(fileName))
            return false;

        return fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".env.development", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".dev.env", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrackedDotenvTemplate(string fileName)
    {
        return fileName.Equals(".env.template", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".env.development.template", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(".dev.env.template", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindUntimedBuildOrTestWorkflowSteps(string workflow)
    {
        string[] lines = workflow.Replace("\r\n", "\n").Split('\n');
        var currentStepLines = new List<string>();

        foreach (string line in lines)
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("- name:", StringComparison.Ordinal))
            {
                foreach (string untimedStep in FindUntimedBuildOrTestCommands(currentStepLines))
                    yield return untimedStep;

                currentStepLines.Clear();
                currentStepLines.Add(trimmed);
                continue;
            }

            if (currentStepLines.Count == 0)
                continue;

            currentStepLines.Add(trimmed);
        }

        foreach (string untimedStep in FindUntimedBuildOrTestCommands(currentStepLines))
            yield return untimedStep;
    }

    private static IEnumerable<string> FindUntimedBuildOrTestCommands(
        IReadOnlyList<string> stepLines)
    {
        if (stepLines.Count == 0)
            yield break;

        string currentStepName = stepLines[0]["- name:".Length..].Trim();
        bool stepHasTimeout = stepLines.Any(line =>
            line.StartsWith("timeout-minutes:", StringComparison.Ordinal));

        foreach (string line in stepLines)
        {
            if (!line.StartsWith("run:", StringComparison.Ordinal))
                continue;

            string command = line["run:".Length..].Trim();
            if (IsLongRunningBuildOrTestCommand(command) && !stepHasTimeout)
                yield return $"{currentStepName}: {command}";
        }
    }

    private static bool IsLongRunningBuildOrTestCommand(string command) =>
        command.StartsWith("msbuild ", StringComparison.Ordinal) ||
        command.StartsWith("dotnet test ", StringComparison.Ordinal);

    private static IEnumerable<string> FindServiceLoggerWritesWithoutCancellation(
        string file,
        string repositoryRoot)
    {
        string source = File.ReadAllText(file);
        const string marker = "_serviceLogger.WriteLogAsync";
        int searchStart = 0;

        while (true)
        {
            int markerIndex = source.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (markerIndex < 0)
                yield break;

            int openParenthesisIndex = source.IndexOf('(', markerIndex);
            if (openParenthesisIndex < 0)
                yield break;

            int closeParenthesisIndex = FindMatchingParenthesis(source, openParenthesisIndex);
            if (closeParenthesisIndex < 0)
            {
                yield return $"{Path.GetRelativePath(repositoryRoot, file)}:{GetLineNumber(source, markerIndex)}";
                yield break;
            }

            string call = source.Substring(
                markerIndex,
                closeParenthesisIndex - markerIndex + 1);
            if (!call.Contains("cancellationToken", StringComparison.Ordinal))
                yield return $"{Path.GetRelativePath(repositoryRoot, file)}:{GetLineNumber(source, markerIndex)}";

            searchStart = closeParenthesisIndex + 1;
        }
    }

    private static IEnumerable<string> FindSwallowingBareCatchBlocks(
        string file,
        string repositoryRoot)
    {
        string source = File.ReadAllText(file);
        var bareCatch = new Regex(@"\bcatch\s*\{", RegexOptions.Compiled);

        foreach (Match match in bareCatch.Matches(source))
        {
            int openBraceIndex = source.IndexOf('{', match.Index);
            if (openBraceIndex < 0)
                continue;

            int closeBraceIndex = FindMatchingBrace(source, openBraceIndex);
            if (closeBraceIndex < 0)
            {
                yield return $"{Path.GetRelativePath(repositoryRoot, file)}:{GetLineNumber(source, match.Index)}";
                continue;
            }

            string catchBody = source.Substring(
                openBraceIndex + 1,
                closeBraceIndex - openBraceIndex - 1);
            if (!catchBody.Contains("throw", StringComparison.Ordinal))
                yield return $"{Path.GetRelativePath(repositoryRoot, file)}:{GetLineNumber(source, match.Index)}";
        }
    }

    private static int FindMatchingParenthesis(string source, int openParenthesisIndex)
    {
        int depth = 0;

        for (int index = openParenthesisIndex; index < source.Length; index++)
        {
            if (source[index] == '(')
            {
                depth++;
                continue;
            }

            if (source[index] != ')')
                continue;

            depth--;
            if (depth == 0)
                return index;
        }

        return -1;
    }

    private static int FindMatchingBrace(string source, int openBraceIndex)
    {
        int depth = 0;

        for (int index = openBraceIndex; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
                continue;
            }

            if (source[index] != '}')
                continue;

            depth--;
            if (depth == 0)
                return index;
        }

        return -1;
    }

    private static int GetLineNumber(string source, int index)
    {
        int lineNumber = 1;

        for (int i = 0; i < index; i++)
        {
            if (source[i] == '\n')
                lineNumber++;
        }

        return lineNumber;
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

    private static IEnumerable<string> EnumerateAppSourceFiles(string repositoryRoot)
    {
        string appDirectory = Path.Combine(repositoryRoot, "MSAVA-App");

        return Directory
            .EnumerateFiles(appDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                string relativePath = Path.GetRelativePath(repositoryRoot, file);
                string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
                    !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
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
