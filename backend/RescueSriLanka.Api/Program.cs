using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Services;
using RescueSriLanka.Api.Services.Llm;
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

// Agentic AI — provider-agnostic client plus Component A's agent.
builder.Services.AddHttpClient<ILlmClient, GoogleAiClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IncidentAnalysisTools>();
builder.Services.AddScoped<IIncidentAnalysisAgent, IncidentAnalysisAgent>();
builder.Services.AddScoped<IAgentRunService, AgentRunService>();

// ---------------------------------------------------------------- clients
// React (Vite) and Flutter web during development. Tighten before deployment.
const string CorsPolicy = "ClientApps";
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy => policy
        .WithOrigins(
            "http://localhost:5173",
            "http://localhost:4173",
            "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod()));

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
