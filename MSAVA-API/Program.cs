using MSAVA_API.Authorization;
using MSAVA_API.Authentication;
using MSAVA_API.Diagnostics;
using MSAVA_API.Filters;
using MSAVA_API.Handlers;
using MSAVA_API.Middleware;
using MSAVA_BLL.Services;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_INF.Environment;
using MSAVA_INF.Managers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Import;
using MSAVA_Shared.Models;
using Supprocom.Secrets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
Directory.CreateDirectory(logDirectory);

builder.Configuration.AddSupprocomSecrets(options =>
{
    options.EnvironmentName = builder.Environment.EnvironmentName;
    options.File.DevelopmentName = ".env.development";
    options.File.DevelopmentComposition = SecretFileComposition.Replace;
});

// Register validated local environment view
var env = new LocalEnvironment(builder.Configuration);
builder.Services.AddSingleton<ILocalEnvironment>(env);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .MinimumLevel.Is(env.Values.SerilogInformationLevel)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .WriteTo.Async(a => a.File(
        path: Path.Combine(logDirectory, "serilog-.txt"),
        rollingInterval: env.Values.SerilogRollingInterval,
        retainedFileCountLimit: env.Values.SerilogRetainedFileCountLimit,
        fileSizeLimitBytes: env.Values.SerilogFileSizeLimitBytes,
        rollOnFileSizeLimit: env.Values.SerilogRollOnFileSizeLimit,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
    ))
    .CreateLogger();
builder.Host.UseSerilog();

Log.Information("Serilog configured with Information level: {InformationLevel}, Rolling Interval: {RollingInterval}, Retained File Count Limit: {RetainedFileCountLimit}, File Size Limit Bytes: {FileSizeLimitBytes}, Roll On File Size Limit: {RollOnFileSizeLimit}",
    env.Values.SerilogInformationLevel, env.Values.SerilogRollingInterval, env.Values.SerilogRetainedFileCountLimit, env.Values.SerilogFileSizeLimitBytes, env.Values.SerilogRollOnFileSizeLimit);

// Register controllers
builder.Services.AddControllers(options =>
{
    options.Filters.Add<TaintedPathFilter>();
});

builder.Services.AddAuthorization(options =>
{
    ApiAuthorizationOptions.Configure(options);
});

// Swagger setup WITH JWT SUPPORT
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "M-SAVA-API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });

    c.OperationFilter<OctetStreamOperationFilter>();
    c.OperationFilter<AuthorizeCheckOperationFilter>();
});

// Register main database context
string baseDbConnectionString = PostgresConnectionStringFactory.CreateBaseDbConnectionString(env.Values);
builder.Services.AddDbContext<BaseDataContext>(options =>
    options.UseNpgsql(baseDbConnectionString));

// Register HTTP services
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services
    .AddHttpClient(FileIngestionService.RemoteFileHttpClientName)
    .ConfigurePrimaryHttpMessageHandler(RemoteFileHttpMessageHandlerFactory.Create);

// Register services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IRequestSessionAccessor, HttpContextRequestSessionAccessor>();
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IFileDownloadService, FileDownloadService>();
builder.Services.AddScoped<IFileIngestionService, FileIngestionService>();
builder.Services.AddScoped<IFileDeduplicationService, FileDeduplicationService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IFileQueryService, FileQueryService>();
builder.Services.AddScoped<ISeedingService, SeedingService>();
builder.Services.AddScoped<IAccessGroupService, AccessGroupService>();
builder.Services.AddScoped<IInviteCodeService, InviteCodeService>();
builder.Services.AddScoped<IFileImportService<FetchFileYouTubeDTO>, YouTubeImportService>();
builder.Services.AddScoped<IFileImportService<FetchFileGoogleDriveDTO>, GoogleDriveImportService>();
builder.Services.AddScoped<IFileImportService<FetchFileFromOneDriveDTO>, OneDriveImportService>();
builder.Services.AddScoped<FilePersistenceService>();

// Register custom loggers
builder.Services.AddScoped<ServiceLogger>();

// Register authorization handlers
builder.Services.AddScoped<IAuthorizationHandler, CurrentUserAccessHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CurrentAdminHandler>();
builder.Services.AddScoped<PersistedJwtTokenValidator>();

// Register managers
builder.Services.AddSingleton<MetadataStore>();
builder.Services.AddScoped<FileManager>();

// JWT Authentication setup
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = env.Values.JwtIssuerName,
            ValidateAudience = true,
            ValidAudience = env.Values.JwtIssuerAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero, 
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(env.GetSigningKeyBytes())
        };
        options.EventsType = typeof(PersistedJwtTokenValidator);
    });

WebApplication app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "Handled {RequestPath}";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", RequestLogValueSanitizer.Sanitize(httpContext.Request.Host.Value));
        diagnosticContext.Set("RequestScheme", RequestLogValueSanitizer.Sanitize(httpContext.Request.Scheme));
        diagnosticContext.Set("UserAgent", RequestLogValueSanitizer.Sanitize(httpContext.Request.Headers.UserAgent.ToString()));
    };
});

app.UseMiddleware<ExceptionCatcherMiddleware>();

app.UseRouting();

string publicFilesDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
const string publicFilesRequestPath = "/api/files/public";
Directory.CreateDirectory(publicFilesDirectory);

app.UseMiddleware<PublicFileAccessMiddleware>(publicFilesDirectory, publicFilesRequestPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(publicFilesDirectory),
    RequestPath = publicFilesRequestPath
});

app.UseAuthentication();
app.UseMiddleware<RequestContextMiddleware>();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<ISeedingService>();
    await seeder.SeedAsync();
}

app.Run();
