using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
using RescueSriLanka.Api.Services.Push;
using RescueSriLanka.Api.Services.Storage;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentAnalysisAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.IncidentEnrichmentAgent;
using RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;
using RescueSriLanka.Api.Features.ComponentA.Data;
using RescueSriLanka.Api.Features.ComponentA.Services;
using RescueSriLanka.Api.Features.ComponentA.Services.Notifications;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.Data;
using RescueSriLanka.Api.Features.ComponentB.Services;
using RescueSriLanka.Api.Features.ComponentC.Services;
using RescueSriLanka.Api.Features.ComponentC.Data;
using RescueSriLanka.Api.Features.ComponentC.Agents.ResourceAllocationAgent;
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

// Brute-force and cost protection. "auth" throttles the unauthenticated
// account endpoints per IP (login, register, password reset); "ai" throttles
// the endpoints that call Gemini per signed-in user.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // Sign-up has its own allowance: the app signs in straight after registering, and that
    // sign-in should not be refused because the registration used up the "auth" window.
    options.AddPolicy("register", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // Anonymous submission forms: a generous per-IP limit, enough for a family but not for a script.
    options.AddPolicy("public", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("ai", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Component A — incidents, map and safety zones.
builder.Services.AddScoped<SafetyZoneService>();
builder.Services.AddScoped<ISafetyZoneService>(provider => provider.GetRequiredService<SafetyZoneService>());
builder.Services.AddScoped<IManualZoneService>(provider => provider.GetRequiredService<SafetyZoneService>());
builder.Services.AddScoped<IIncidentService, IncidentService>();
builder.Services.AddScoped<IImageStorageService, ImageStorageService>();

// Component B — help requests, travel advisories and the Planner Agent.
builder.Services.AddScoped<IHelpRequestService, HelpRequestService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IHelpRequestResponseStatusService, HelpRequestResponseStatusService>();
builder.Services.AddScoped<ITravelAdvisoryService, TravelAdvisoryService>();
builder.Services.AddScoped<IEmergencyContactService, EmergencyContactService>();
builder.Services.AddScoped<IHelpRequestMessageService, HelpRequestMessageService>();
builder.Services.AddScoped<IPlannerAgentService, PlannerAgentService>();

// Component B — the assessment model calls read-only tools itself before it gives its verdict.
builder.Services.AddHttpClient<IPlanningModel, GeminiPlanningModel>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IRequestAssessmentAgent, RequestAssessmentAgent>();
builder.Services.AddScoped<IHelpRequestServiceForAgent, HelpRequestServiceForAgent>();

// New help requests are triaged by the Planner Agent in the background — the
// citizen filing one never waits on a model, and a manager's queue is
// already-assessed by the time they open it.
builder.Services.AddSingleton<HelpRequestAnalysisQueue>();
builder.Services.AddSingleton<IHelpRequestAnalysisQueue>(
    provider => provider.GetRequiredService<HelpRequestAnalysisQueue>());
builder.Services.AddHostedService<HelpRequestAnalysisWorker>();

// Component C — medical supplies, food/water stock and allocations.
builder.Services.AddScoped<IResourceManagementService, ResourceManagementService>();
builder.Services.AddScoped<IResourceAllocationAgent, ResourceAllocationAgent>();
builder.Services.AddScoped<IResourceForecastAgent, ResourceForecastAgent>();

// Component D — rescue teams, assignments, dispatch and its agents.
builder.Services.AddDbContext<ComponentDDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IIncidentReadService, IncidentReadService>();
builder.Services.AddScoped<IHelpRequestReadService, HelpRequestReadService>();
builder.Services.AddScoped<HelpRequestCandidateService>();
builder.Services.AddScoped<IRescueRecommendationExplanation, GeminiRescueRecommendationExplanation>();
builder.Services.AddScoped<HelpRequestRecommendationService>();
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
// Every photo — incident, profile and help-request — is uploaded to Cloudinary
// through the API, and nothing is kept on the API's own disk. The account is
// needed only to upload, so the API still starts without it.
builder.Services.Configure<CloudinaryOptions>(
    builder.Configuration.GetSection(CloudinaryOptions.SectionName));

builder.Services.AddHttpClient(nameof(CloudinaryImageStore), client =>
    client.Timeout = TimeSpan.FromSeconds(20));

builder.Services.AddScoped<IImageStore, CloudinaryImageStore>();

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
builder.Services.AddScoped<IActionEmailService, ActionEmailService>();

builder.Services.AddSingleton<EmailQueue>();
builder.Services.AddSingleton<IEmailQueue>(
    provider => provider.GetRequiredService<EmailQueue>());
builder.Services.AddHostedService(
    provider => provider.GetRequiredService<EmailQueue>());

// Mail goes out on a background worker: nobody filing a report or approving an
// assessment should wait on a mail server, or fail because one is down.
builder.Services.AddSingleton<NotificationQueue>();
builder.Services.AddSingleton<INotificationQueue>(
    provider => provider.GetRequiredService<NotificationQueue>());
builder.Services.AddHostedService<NotificationWorker>();

// ---------------------------------------------------------------- push notifications
// The same events that send email also push to the phone app, for citizens who
// have turned push on there. Firebase delivers them. Until a service account is
// configured they are logged instead, so the feature runs for anyone who clones
// the repository.
builder.Services.Configure<PushOptions>(builder.Configuration.GetSection(PushOptions.SectionName));

builder.Services.AddHttpClient(nameof(FcmPushSender), client =>
    client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<IFcmAccessTokens, FcmAccessTokens>();
builder.Services.AddScoped<FcmPushSender>();
builder.Services.AddScoped<LoggingPushSender>();
builder.Services.AddScoped<IPushSender>(provider =>
{
    var pushOptions = provider.GetRequiredService<IOptions<PushOptions>>().Value;

    return pushOptions.Fcm.IsConfigured
        ? provider.GetRequiredService<FcmPushSender>()
        : provider.GetRequiredService<LoggingPushSender>();
});
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();

// Sends run on a background worker, like email: no request waits on Firebase.
builder.Services.AddSingleton<PushQueue>();
builder.Services.AddSingleton<IPushQueue>(
    provider => provider.GetRequiredService<PushQueue>());
builder.Services.AddHostedService(
    provider => provider.GetRequiredService<PushQueue>());

// ---------------------------------------------------------------- agentic AI
// The agents depend on ILlmClient, never on a concrete provider. Google AI
// (Gemini) is the one implementation — see docs/adr/0001-llm-provider.md for
// why, and for the deviation from the proposal that choice represents.
//
// An unconfigured or unreachable model is not a failure: the agent falls back
// to its deterministic rule engine and records that it did.
builder.Services.AddHttpClient<ILlmClient, GoogleAiClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));

// The rainfall tool calls Open-Meteo. A short timeout keeps a slow weather
// service from holding up the analysis — the tool returns null instead.
builder.Services.AddHttpClient<IncidentAnalysisTools>(client =>
{
    client.BaseAddress = new Uri("https://api.open-meteo.com/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<AIncidentAnalysisAgent, IncidentAnalysisAgent>();
builder.Services.AddScoped<IIncidentEnrichmentAgent, IncidentEnrichmentAgent>();
builder.Services.AddScoped<IZonePlanningAgent, ZonePlanningAgent>();
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
                .AllowAnyMethod()
                // So the React dashboard can read the paged incident count.
                .WithExposedHeaders("X-Total-Count");
            return;
        }

        var allowed = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        policy.WithOrigins(allowed).AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("X-Total-Count");
    }));

// ---------------------------------------------------------------- api surface
builder.Services
    .AddControllers()
    // Enums travel as readable strings ("Critical", not 3) in both directions.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Catches anything a controller's own try/catch did not already turn into a
// response, so a bug never reaches a caller as a raw stack trace.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RescueSriLanka API",
        Version = "v1",
        Description = "Disaster response & resource coordination platform."
    });

    // Components B and D each declare types with the same short name (for
    // example WorkflowObjectiveType), so schemas are keyed by full name.
    options.CustomSchemaIds(type => type.FullName!.Replace('+', '.'));

    // "Authorize" button in Swagger so endpoints can be tried with a token.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token returned by /api/auth/login (citizens) or /api/auth/portal/login (staff)."
    });
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer"), new List<string>() }
    });
});

// Behind a hosting proxy every request arrives from the proxy's address, which would put all users in one
// rate-limit bucket. Only addresses listed in configuration may set forwarded headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var address in builder.Configuration.GetSection("Proxy:TrustedAddresses").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(address, out var ip)) options.KnownProxies.Add(ip);
    }
});

var app = builder.Build();

// First in the pipeline, so it can catch whatever happens downstream of it.
app.UseForwardedHeaders();
app.UseExceptionHandler();

// Which photo backend is live should never be a guess when a demo misbehaves.
using (var startupScope = app.Services.CreateScope())
{
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    startupLogger.LogInformation(
        "Photos are stored via {Store}.",
        startupScope.ServiceProvider.GetRequiredService<IImageStore>().Name);

    if (!startupScope.ServiceProvider.GetRequiredService<IOptions<CloudinaryOptions>>().Value.IsConfigured)
    {
        startupLogger.LogWarning(
            "Cloudinary is not configured — photo uploads will fail until "
            + "Cloudinary:CloudName, ApiKey and ApiSecret are set.");
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

    var pushOptions = startupScope.ServiceProvider.GetRequiredService<IOptions<PushOptions>>().Value;
    startupLogger.LogInformation(
        "Push notifications {State} via {Transport}.",
        pushOptions.Enabled ? "ON" : "OFF",
        startupScope.ServiceProvider.GetRequiredService<IPushSender>().Name);

    if (!pushOptions.Fcm.IsConfigured)
    {
        startupLogger.LogWarning(
            "Firebase is not configured, so push notifications are logged, not sent. "
            + "Set Push:Fcm:ProjectId and Push:Fcm:CredentialsFile (or CredentialsJson) to deliver them.");
    }
}

// ---------------------------------------------------------------- start-up
// Swagger is for development. Deployments can switch it on with Swagger:Enabled.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "RescueSriLanka API v1");
        options.RoutePrefix = "swagger";
    });
}

// The deployed database starts empty, so migrations and the demo accounts run
// wherever the API starts. Set Database:MigrateOnStartup=false to skip them.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    // Bring the schema up to date and make sure the demo accounts exist.
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // Component D's rescue tables have their own migration history.
        await services.GetRequiredService<ComponentDDbContext>().Database.MigrateAsync();

        // Public emergency numbers are reference data, not demo data.
        await EmergencyContactSeeder.SeedAsync(db);

        // The placeholder staff logins share a password published in this repository,
        // so they are created only when a deployment asks for them.
        if (app.Configuration.GetValue("Database:SeedPlaceholderStaff", false))
        {
            await DbSeeder.SeedAsync(
                db,
                services.GetRequiredService<IPasswordHasher<User>>(),
                logger);
        }

        // Component B sample records are isolated to local development and are
        // explicitly marked as fixtures; production data stays operator-entered.
        // Off by default so a shared database holds only real accounts and requests.
        if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:SeedComponentBDemo", false))
        {
            await ComponentBDataSeeder.SeedAsync(
                db,
                services.GetRequiredService<IPasswordHasher<User>>());
        }

        // Component C supplies and stock for the resource screens.
        // Sample stock for the resource screens. Off by default so a shared database holds only real data.
        if (app.Configuration.GetValue("Database:SeedSampleStock", false))
        {
            await ResourceDataSeeder.SeedAsync(db);
        }

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

// Explicitly opted-in development fixtures do not depend on automatic migrations.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("ComponentD:SeedDemoData", false))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    await ComponentDDataSeeder.SeedAsync(
        services.GetRequiredService<ComponentDDbContext>(),
        services.GetRequiredService<AppDbContext>(),
        app.Environment, app.Configuration,
        services.GetRequiredService<ILoggerFactory>().CreateLogger("ComponentDDataSeeder"));
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

// Public liveness probe for the host and for evaluators: reports whether the
// API is up and can reach PostgreSQL. It exposes no data.
app.MapHealthChecks("/health");

app.Run();
