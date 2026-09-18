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
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Llm;
using RescueSriLanka.Api.Services.Storage;
using RescueSriLanka.Api.Agents.IncidentAnalysisAgent;

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
builder.Services.AddScoped<IIncidentAnalysisAgent, IncidentAnalysisAgent>();
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

    var llm = startupScope.ServiceProvider.GetRequiredService<ILlmClient>();
    startupLogger.LogInformation(
        "Agents use {Model}{Unconfigured}.",
        llm.ModelName,
        llm.IsConfigured ? string.Empty : " — NOT configured, rule engine will run");
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
