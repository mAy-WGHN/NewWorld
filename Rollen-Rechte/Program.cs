using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Rollen_Rechte.Authentication;
using Rollen_Rechte.Commands;
using Rollen_Rechte.Common.Api;
using Rollen_Rechte.Common.RateLimiting;
using Rollen_Rechte.Data;
using Rollen_Rechte.Serialization;
using Rollen_Rechte.Services;

LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AccessDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
           .UseSnakeCaseNamingConvention());

builder.Services
    .AddControllers(options =>
    {
        // Pflichtfelder werden explizit per [Required] (mit deutscher Meldung) ausgewiesen.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

        // Standard ist "geschlossen": Eine Controller-Route ohne eigene Angabe ([HasPrivilege], [Authorize(Policy = Authenticated)]
        // oder [AllowAnonymous]) verlangt mindestens die Anmeldung.
        options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    })
    .AddJsonOptions(options => ApiJson.Configure(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ValidationResponseFactory.Create);

// Für Antworten außerhalb von MVC (Fehler-Handler, Rate-Limit, 401-Challenge).
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddAccessAuthentication(builder.Configuration);
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<AppsOptions>(builder.Configuration.GetSection(AppsOptions.SectionName));
builder.Services.Configure<BcGatewayOptions>(builder.Configuration.GetSection(BcGatewayOptions.SectionName));
builder.Services.Configure<LocationSyncOptions>(builder.Configuration.GetSection(LocationSyncOptions.SectionName));
builder.Services.AddHttpClient(HttpBcGateway.HttpClientName);
builder.Services.AddScoped<IBcGateway, HttpBcGateway>();
builder.Services.AddSingleton<PinProtector>();
builder.Services.AddScoped<BcLoginService>();
builder.Services.AddScoped<BcStoreSync>();
builder.Services.AddHostedService<BcStoreSyncStartup>();
builder.Services.AddScoped<RecipientResolver>();
builder.Services.AddAccessRateLimiting();

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
    document.Info = new OpenApiInfo { Title = "Rollen & Rechte", Version = "1.0.0" };
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

// CLI: "dotnet Rollen-Rechte.dll create-user <username>" legt einen Admin an und beendet sich.
if (args.Length > 0 && args[0] == CreateUserCommand.Name)
{
    Environment.ExitCode = await CreateUserCommand.RunAsync(app.Services, args[1..], Console.Out);
    return;
}

// CLI: "sync-stores" gleicht die Filialen mit BC ab, "decrypt-pin <username>" zeigt das gespeicherte BC-PIN.
if (args.Length > 0 && args[0] == SyncStoresCommand.Name)
{
    Environment.ExitCode = await SyncStoresCommand.RunAsync(app.Services, Console.Out);
    return;
}

if (args.Length > 0 && args[0] == AssignRoleCommand.Name)
{
    Environment.ExitCode = await AssignRoleCommand.RunAsync(app.Services, args[1..], Console.Out);
    return;
}

if (args.Length > 0 && args[0] == DecryptPinCommand.Name)
{
    Environment.ExitCode = await DecryptPinCommand.RunAsync(app.Services, args[1..], Console.Out);
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

await AclSeeder.SeedAsync(app.Services);
await AdminSeeder.SeedAsync(app.Services);

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
