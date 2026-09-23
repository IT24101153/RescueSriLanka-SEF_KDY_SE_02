using System.Text;
using System.Text.Json.Serialization;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using Microsoft.Extensions.Options;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Email;
using RescueSriLanka.Api.Services.Llm;
using RescueSriLanka.Api.Services.Storage;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Features.ComponentA.Data;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.Services;
using RescueSriLanka.Api.Features.ComponentC.Services;
using RescueSriLanka.Api.Features.ComponentC.Data;
using RescueSriLanka.Api.Features.ComponentD.Agents.Orchestration;
using RescueSriLanka.Api.Features.ComponentD.Agents.SafetyValidation;
using RescueSriLanka.Api.Features.ComponentD.Services;
using RescueSriLanka.Api.Features.ComponentD.Data;
// Component A and Component D each declare an IIncidentAnalysisAgent.
using AIncidentAnalysisAgent = RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent.IIncidentAnalysisAgent;
using DIncidentAnalysisAgent = RescueSriLanka.Api.Features.ComponentD.Agents.Orchestration.IIncidentAnalysisAgent;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------------------------------------------------------------- auth
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is missing. Add a Jwt section to appsettings.Development.json.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Component A — incidents, map and safety zones.
builder.Services.AddScoped<ISafetyZoneService, SafetyZoneService>();
builder.Services.AddScoped<IIncidentService, IncidentService>();
builder.Services.AddScoped<IImageStorageService, ImageStorageService>();

// Component B — help requests, travel advisories and the Planner Agent.
builder.Services.AddScoped<IHelpRequestService, HelpRequestService>();
builder.Services.AddScoped<ITravelAdvisoryService, TravelAdvisoryService>();
builder.Services.AddScoped<IPlannerAgentService, PlannerAgentService>();
builder.Services.AddScoped<IHelpRequestServiceForAgent, HelpRequestServiceForAgent>();
builder.Services.AddHttpClient<IAiAnalysisService, GeminiAnalysisService>();

// Component C — shelters, medical supplies, food/water stock and allocations.
builder.Services.AddScoped<IResourceManagementService, ResourceManagementService>();

// Component D — rescue teams, assignments, dispatch and its agents.
builder.Services.AddDbContext<ComponentDDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IIncidentReadService, IncidentReadService>();
builder.Services.AddScoped<IRescueTeamService, RescueTeamService>();
builder.Services.AddScoped<ITeamMatchingService, TeamMatchingService>();
builder.Services.AddScoped<IAssignmentService, AssignmentService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<ISafetyValidationAgent, SafetyValidationAgent>();
builder.Services.AddScoped<SafetyValidationTools>();
builder.Services.AddHttpClient<IGeminiSafetyValidationClient, GeminiSafetyValidationClient>();
builder.Services.AddScoped<IAssignmentSafetyValidationAgent, GeminiSafetyValidationAgent>();
builder.Services.AddScoped<DispatchAgentTools>();
builder.Services.AddHttpClient<DIncidentAnalysisAgent, GeminiIncidentAnalysisAgent>();
builder.Services.AddHttpClient<IDispatchRecommendationAgent, GeminiDispatchRecommendationAgent>();
builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();

// ---------------------------------------------------------------- photo storage
// Cloudinary when credentials are present, local disk otherwise. A deployed
// container's disk does not survive a restart, so uploads written there would
// disappear — but requiring an account to run the project locally would be
// worse, hence the fallback rather than a hard failure.
var cloudinarySection = builder.Configuration.GetSection("Cloudinary");
var cloudName = cloudinarySection["CloudName"];
var cloudinaryApiKey = cloudinarySection["ApiKey"];
var cloudinaryApiSecret = cloudinarySection["ApiSecret"];

if (!string.IsNullOrWhiteSpace(cloudName) &&
    !string.IsNullOrWhiteSpace(cloudinaryApiKey) &&
    !string.IsNullOrWhiteSpace(cloudinaryApiSecret))
{
    builder.Services.AddSingleton(
        new Cloudinary(new Account(cloudName, cloudinaryApiKey, cloudinaryApiSecret))
        {
            Api = { Secure = true }
        });

    builder.Services.AddHttpClient(nameof(CloudinaryImageStore), client =>
        client.Timeout = TimeSpan.FromSeconds(20));

    builder.Services.AddScoped<IImageStore, CloudinaryImageStore>();
}
else
{
    builder.Services.AddScoped<IImageStore, LocalDiskImageStore>();
}

// Some Cloudinary settings but not all is a mistake, not a choice — photos
// would quietly land on local disk instead. Say which one is missing.
var missingCloudinary = new[]
    {
        ("CloudName", cloudName),
        ("ApiKey", cloudinaryApiKey),
        ("ApiSecret", cloudinaryApiSecret)
    }
    .Where(setting => string.IsNullOrWhiteSpace(setting.Item2))
    .Select(setting => $"Cloudinary:{setting.Item1}")
    .ToList();
var cloudinaryHalfConfigured = missingCloudinary.Count is > 0 and < 3;

// ---------------------------------------------------------------- email
// Two notifications: a receipt to whoever files a report, and a district-wide
// warning once a coordinator confirms a High or Critical risk.
//
// testmail.app cannot send — it only receives — so it is not the transport. The
// transport is SMTP, and testmail is the inbox we aim it at while developing,
// plus an API for reading back what arrived. With no SMTP host configured the
// sender writes each message to the log instead, which keeps the whole feature
// runnable by anyone who clones this repository.
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection(EmailOptions.SectionName));

var emailOptions =
    builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
    ?? new EmailOptions();

builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddScoped<LoggingEmailSender>();

builder.Services.AddScoped<IEmailSender>(provider =>
{
    IEmailSender transport = emailOptions.Smtp.IsConfigured
        ? provider.GetRequiredService<SmtpEmailSender>()
        : provider.GetRequiredService<LoggingEmailSender>();

    // Redirecting at the last hop keeps the recipient query honest: the real
    // citizens in the district are still looked up and addressed by name.
    if (emailOptions.Testmail.IsConfigured && emailOptions.Testmail.RedirectAllMail)
    {
        return new TestmailRedirectingEmailSender(
            transport,
            provider.GetRequiredService<IOptions<EmailOptions>>(),
            provider.GetRequiredService<ILogger<TestmailRedirectingEmailSender>>());
    }

    return transport;
});

builder.Services.AddHttpClient<ITestmailClient, TestmailClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));

builder.Services.AddScoped<INotificationService, NotificationService>();

// Mail goes out on a background worker: nobody filing a report or approving an
// assessment should wait on a mail server, or fail because one is down.
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddSingleton<INotificationQueue>(
    provider => provider.GetRequiredService<NotificationQueue>());
builder.Services.AddHostedService<NotificationWorker>();

// ---------------------------------------------------------------- agentic AI
// The agents depend on ILlmClient, never on a concrete provider. Google AI
// (Gemini) is the one implementation — see docs/adr/0001-llm-provider.md for
// why, and for the deviation from the proposal that choice represents.
//
// An unconfigured or unreachable model is not a failure: the agent falls back
// to its deterministic rule engine and records that it did.
builder.Services.AddHttpClient<ILlmClient, GoogleAiClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddScoped<IncidentAnalysisTools>();
builder.Services.AddScoped<AIncidentAnalysisAgent, IncidentAnalysisAgent>();
builder.Services.AddScoped<IAgentRunService, AgentRunService>();

// New reports are scored in the background — the citizen filing one never
// waits on a model, and the coordinator's queue fills with proposals.
builder.Services.AddSingleton<IncidentAnalysisQueue>();
builder.Services.AddSingleton<IIncidentAnalysisQueue>(
    provider => provider.GetRequiredService<IncidentAnalysisQueue>());
builder.Services.AddHostedService<IncidentAnalysisWorker>();

// ---------------------------------------------------------------- clients
// React (Vite) and Flutter web during development. Tighten before deployment.
const string CorsPolicy = "ClientApps";
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Vite, Flutter web and any other local dev server pick their own
            // ports, so allow any localhost origin while developing. Production
            // gets an explicit list instead.
            policy
                .SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                    (uri.Host is "localhost" or "127.0.0.1" or "::1"))
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        var allowed = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        policy.WithOrigins(allowed).AllowAnyHeader().AllowAnyMethod();
    }));

// ---------------------------------------------------------------- api surface
builder.Services
    .AddControllers()
    // Enums travel as readable strings ("Critical", not 3) in both directions.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RescueSriLanka API",
        Version = "v1",
        Description = "Disaster response & resource coordination platform."
    });

    // "Authorize" button in Swagger so endpoints can be tried with a token.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by /api/auth/login."
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer"), new List<string>() }
    });
});

var app = builder.Build();

// Which photo backend is live should never be a guess when a demo misbehaves.
using (var startupScope = app.Services.CreateScope())
{
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    startupLogger.LogInformation(
        "Incident photos are stored via {Store}.",
        startupScope.ServiceProvider.GetRequiredService<IImageStore>().Name);

    if (cloudinaryHalfConfigured)
    {
        startupLogger.LogWarning(
            "Cloudinary is only partly configured — {Missing} is empty — so photos are "
            + "going to local disk instead. Fill it in to upload to Cloudinary.",
            string.Join(", ", missingCloudinary));
    }

    var llm = startupScope.ServiceProvider.GetRequiredService<ILlmClient>();
    startupLogger.LogInformation(
        "Agents use {Model}{Unconfigured}.",
        llm.ModelName,
        llm.IsConfigured ? string.Empty : " — NOT configured, rule engine will run");

    // Which way mail goes, and at what risk level, decides whether anyone is
    // warned at all — never leave that to be discovered mid-demo.
    startupLogger.LogInformation(
        "Email notifications {State} via {Transport}; district warnings at {Severity} and above.",
        emailOptions.Enabled ? "ON" : "OFF",
        startupScope.ServiceProvider.GetRequiredService<IEmailSender>().Name,
        emailOptions.MinimumWarningSeverity);
}

// ---------------------------------------------------------------- start-up
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "RescueSriLanka API v1");
        options.RoutePrefix = "swagger";
    });

    // Bring the schema up to date and make sure the demo accounts exist.
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(
            db,
            services.GetRequiredService<IPasswordHasher<User>>(),
            logger);

        // Component C's shelters, supplies and stock for the resource screens.
        await ResourceDataSeeder.SeedAsync(db);

        // Sample incidents for the map/dashboard — off via configuration.
        if (app.Configuration.GetValue("SeedSampleIncidents", false))
        {
            await IncidentSeeder.SeedAsync(db, logger);
            await services.GetRequiredService<ISafetyZoneService>().RecomputeAsync();
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration or seeding failed.");
    }
}

// Serves uploaded incident photos from wwwroot/uploads.
app.UseStaticFiles();

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
