using BC_Anbindung.Authentication;
using BC_Anbindung.BusinessCentral;
using BC_Anbindung.Common.Api;
using BC_Anbindung.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;

LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

        // Standard ist "geschlossen": Ohne eigene Angabe verlangt jede Route einen gültigen API-Schlüssel.
        options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder(ApiKeyDefaults.Scheme).RequireAuthenticatedUser().Build()));
    })
    .AddJsonOptions(options => ApiJson.Configure(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ValidationResponseFactory.Create);

builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.Configure<BcAuthOptions>(builder.Configuration.GetSection(BcAuthOptions.SectionName));
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(BcAuthService.HttpClientName);
builder.Services.AddHttpClient(BcCodeunitClient.HttpClientName);
builder.Services.AddSingleton<IBcAuthService, BcAuthService>();
builder.Services.AddScoped<IBcCodeunitClient, BcCodeunitClient>();
builder.Services.AddScoped<BcGateway>();

builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null);
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!(app.Configuration.GetSection($"{GatewayOptions.SectionName}:ApiKeys").GetChildren().Any(c => !string.IsNullOrEmpty(c.Value))))
    app.Logger.LogWarning("Es ist kein API-Schlüssel konfiguriert (Gateway__ApiKeys__<Aufrufer>): Alle Aufrufe werden mit 401 abgelehnt.");

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
