using System.Net;
using System.Text.RegularExpressions;
using HrTool_API.Authentication;
using HrTool_API.Common.Api;
using HrTool_API.Common.RateLimiting;
using HrTool_API.Data;
using HrTool_API.Serialization;
using HrTool_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi;

LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

// Retry fängt transiente Verbindungsabbrüche der Azure-DB ab; manuelle BeginTransaction()-Aufrufe sind damit
// nur noch innerhalb von db.Database.CreateExecutionStrategy().ExecuteAsync(...) erlaubt.
builder.Services.AddDbContext<HrToolDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"), npgsql => npgsql.EnableRetryOnFailure())
           .UseSnakeCaseNamingConvention()
           // Pläne, Mitarbeiter und Nicht-Verfügbarkeiten sind per Scope gefiltert, ihre Verbindungen (z. B. Schicht → Mitarbeiter) bewusst nicht: Ein sichtbarer Plan zeigt alle seine Schichten.
           .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

builder.Services
    .AddControllers(options =>
    {
        // Pflichtfelder werden explizit per [Required] (mit deutscher Meldung) ausgewiesen.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

        // Standard ist "geschlossen": Eine Controller-Route ohne eigene Angabe ([HasPrivilege], [Authorize(Policy = Authenticated)]
        // oder [AllowAnonymous]) verlangt mindestens die Anmeldung.
        options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    })
    .AddJsonOptions(options => HrToolJsonOptions.Configure(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ValidationResponseFactory.Create);

// Für Antworten außerhalb von MVC (Fehler-Handler, Rate-Limit, 401-Challenge).
builder.Services.ConfigureHttpJsonOptions(options => HrToolJsonOptions.Configure(options.SerializerOptions));

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHrAuthentication(builder.Configuration);
builder.Services.AddHrRateLimiting();
builder.Services.AddSingleton<ILocationDirectory, HttpLocationDirectory>();
builder.Services.Configure<ReferenceDataOptions>(builder.Configuration.GetSection(ReferenceDataOptions.SectionName));
builder.Services.AddHostedService<ReferenceDataMaintenance>();

// Hinter einem Reverse Proxy (Azure App Service): Client-IP und Schema/Host aus X-Forwarded-* übernehmen.
// Ohne Konfiguration wird dem Proxy vertraut (ForwardLimit 1: nur der zuletzt angehängte Eintrag zählt).
// ForwardedHeaders:KnownProxies (Liste) schränkt das auf bestimmte Proxy-IPs ein.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in knownProxies.Where(p => !string.IsNullOrWhiteSpace(p)))
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

// CORS: erlaubte Origins als Liste und/oder Regex (Cors__AllowedOriginPattern).
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    var pattern = builder.Configuration["Cors:AllowedOriginPattern"];
    var regex = string.IsNullOrWhiteSpace(pattern)
        ? null
        : new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    policy
        .SetIsOriginAllowed(origin =>
            origins.Contains(origin, StringComparer.OrdinalIgnoreCase) || (regex?.IsMatch(origin) ?? false))
        .WithMethods("GET", "OPTIONS", "POST", "PUT", "PATCH", "DELETE")
        .WithHeaders("Content-Type", "Authorization")
        .WithExposedHeaders("Link", "Retry-After")
        .SetPreflightMaxAge(TimeSpan.FromHours(1));
}));

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo { Title = "HR-Tool API", Version = "1.0.0" };
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    };
    document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearerAuth", document)] = [] }];
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks();

var app = builder.Build();

// Befehle für die Kommandozeile (z. B. make import): laufen ohne Webserver und beenden das Programm.
if (args.Length > 0 && args[0] == HrTool_API.Commands.ImportCommand.Name)
{
    Environment.ExitCode = await HrTool_API.Commands.ImportCommand.RunAsync(app.Services, args[1..], Console.Out);
    return;
}

// CORS steht vor Auth und Fehlerbehandlung, damit auch 401/404/422/429-Antworten die Allow-Origin-Header tragen.
app.UseForwardedHeaders();
app.UseCors();
app.UseExceptionHandler(_ => { });
app.UseStatusCodePages(context =>
    ApiErrorWriter.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode, "Ein Fehler ist aufgetreten."));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/api/doc.json");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Lädt KEY=VALUE-Zeilen aus .env; bereits gesetzte Umgebungsvariablen bleiben unangetastet.
static void LoadDotEnv(string path)
{
    if (!File.Exists(path)) return;

    foreach (var line in File.ReadAllLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

        var idx = trimmed.IndexOf('=');
        if (idx <= 0) continue;

        var key = trimmed[..idx].Trim();
        var value = trimmed[(idx + 1)..].Trim().Trim('"');
        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, value);
    }
}

// Für WebApplicationFactory<Program> in den Tests.
public partial class Program;
