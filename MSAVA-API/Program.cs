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

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Register local environment
var env = new LocalEnvironment();
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
        path: "Logs/serilog-.txt",
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
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new NotBannedRequirement())
        .Build();
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

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });

    c.OperationFilter<OctetStreamOperationFilter>();
    c.OperationFilter<AuthorizeCheckOperationFilter>();
});

// Register main database context
string baseDbConnectionString = $"Host={env.Values.PostgresBaseDbHost};Port={env.Values.PostgresBaseDbPort};Database={env.Values.PostgresBaseDbDbName};Username={env.Values.PostgresBaseDbUser};Password={env.Values.PostgresBaseDbPassword};Ssl Mode={env.Values.PostgresBaseDbSslMode}";
builder.Services.AddDbContext<BaseDataContext>(options =>
    options.UseNpgsql(baseDbConnectionString));

// Register HTTP services
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// Register services
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IFileDownloadService, FileDownloadService>();
builder.Services.AddScoped<IFileIngestionService, FileIngestionService>();
builder.Services.AddScoped<IFileDeduplicationService, FileDeduplicationService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IFileQueryService, FileQueryService>();
builder.Services.AddScoped<ISeedingService, SeedingService>();
builder.Services.AddScoped<FilePersistenceService>();
builder.Services.AddScoped<AccessGroupService>();
builder.Services.AddScoped<InviteCodeService>();
builder.Services.AddScoped<YouTubeImportService>();
builder.Services.AddScoped<GoogleDriveImportService>();
builder.Services.AddScoped<OneDriveImportService>();

// Register custom loggers
builder.Services.AddScoped<ServiceLogger>();

// Register authorization handlers
builder.Services.AddScoped<IAuthorizationHandler, NotBannedHandler>();

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
    });

WebApplication app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "Handled {RequestPath}";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme ?? string.Empty);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString() ?? string.Empty);
    };
});

app.UseRouting();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "Data")),
    RequestPath = "/api/files/public",
    OnPrepareResponse = ctx =>
    {
        var filePath = ctx.File.PhysicalPath;
        if (string.IsNullOrEmpty(filePath))
        {
            ctx.Context.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Context.Abort();
            return;
        }

        var fileName = Path.GetFileName(filePath);
        var lastDot = fileName.LastIndexOf('.');
        if (lastDot <= 0)
        {
            ctx.Context.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Context.Abort();
            return;
        }

        var hashHex = fileName[..lastDot].ToUpperInvariant();
        var extension = fileName[(lastDot + 1)..].ToLowerInvariant();

        try
        {
            var metadataStore = ctx.Context.RequestServices.GetRequiredService<MetadataStore>();
            var fileHash = Convert.FromHexString(hashHex);
            var refId = metadataStore.CheckAccess(fileHash, extension, userAccessGroups: null);

            if (refId is null)
            {
                ctx.Context.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Context.Abort();
            }
        }
        catch
        {
            ctx.Context.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Context.Abort();
        }
    }
});

app.UseMiddleware<ExceptionCatcherMiddleware>();

app.UseAuthentication();
app.UseMiddleware<RequestContextMiddleware>();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<ISeedingService>();
    seeder.Seed();
}

app.Run();
