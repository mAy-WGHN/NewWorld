using System.Net;
using System.Text.RegularExpressions;
using Benachrichtigungen.Authentication;
using Benachrichtigungen.Common.Api;
using Benachrichtigungen.Data;
using Benachrichtigungen.Serialization;
using Benachrichtigungen.Services;
using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
           .UseSnakeCaseNamingConvention());

builder.Services
    .AddControllers(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

        // Standard ist "geschlossen": Eine Route ohne eigene Angabe verlangt eine Anmeldung (JWT oder API-Schlüssel);
        // die Routen legen ihre Art mit [Authorize(AuthenticationSchemes = ...)] bzw. [AllowAnonymous] fest.
        options.Filters.Add(new AuthorizeFilter(AuthSetup.DefaultPolicy));
    })
    .AddJsonOptions(options => ApiJson.Configure(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ValidationResponseFactory.Create);

builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddNotificationAuthentication(builder.Configuration);

builder.Services.Configure<AccessOptions>(builder.Configuration.GetSection(AccessOptions.SectionName));
builder.Services.AddHttpClient(HttpRecipientDirectory.HttpClientName);
builder.Services.AddScoped<IRecipientDirectory, HttpRecipientDirectory>();
builder.Services.AddHttpClient<PushServiceClient>();
builder.Services.AddScoped<IPushNotificationSender, PushNotificationSender>();
builder.Services.AddScoped<ReportCreatedHandler>();

// Hinter einem Reverse Proxy (Azure App Service): Client-IP und Schema/Host aus X-Forwarded-* übernehmen.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in knownProxies.Where(p => !string.IsNullOrWhiteSpace(p)))
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

// CORS: erlaubte Origins als Liste und/oder Regex (Cors__AllowedOriginPattern). Die Oberfläche ruft die Push-Endpunkte direkt auf.
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
        .SetPreflightMaxAge(TimeSpan.FromHours(1));
}));

builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Configuration.GetSection($"{SourcesOptions.SectionName}:Keys").GetChildren().Any(c => !string.IsNullOrEmpty(c.Value)))
    app.Logger.LogWarning("Es ist kein Absender-Schlüssel konfiguriert (Sources__Keys__<Absender>): Ereignisse werden mit 401 abgelehnt.");

app.UseForwardedHeaders();
app.UseCors();
app.UseExceptionHandler(_ => { });
app.UseStatusCodePages(context =>
    ApiErrorWriter.WriteAsync(context.HttpContext, context.HttpContext.Response.StatusCode, "Ein Fehler ist aufgetreten."));

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
