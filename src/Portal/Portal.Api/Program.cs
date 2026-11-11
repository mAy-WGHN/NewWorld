using Portal.Core;
using Shared.Infrastructure.Audit;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Portal.Api.Faqs;
using Portal.Api.Http;
using Portal.Api.Reports;
using Portal.Core.Faqs;
using Portal.Infrastructure;
using Shared.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddPortalInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSharedAuthorization();
builder.Services.AddPortalJson();
builder.Services.AddPortalRateLimiting(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddScoped<ReportAccess>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, EnvelopeAuthorizationResultHandler>();

// A body that cannot be read reaches the exception handler (422 in the envelope) instead of a bare 400.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = FaqAttachmentFormat.MaxBytes + 3 * 1024 * 1024);

// Behind a reverse proxy: client address and scheme/host from X-Forwarded-*. Without ForwardedHeaders:KnownProxies/KnownNetworks every proxy is
// trusted here (the public forms are rate limited per client; ForwardLimit 1: only the last entry counts).
builder.Services.AddConfiguredForwardedHeaders(builder.Configuration, trustAnyProxyWhenUnconfigured: true);

// CORS: allowed origins as a list and/or a regular expression; the frontend runs on another origin.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    var pattern = builder.Configuration["Cors:AllowedOriginPattern"];
    var regex = string.IsNullOrWhiteSpace(pattern)
        ? null
        : new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    policy
        .SetIsOriginAllowed(origin => origins.Contains(origin, StringComparer.OrdinalIgnoreCase) || (regex?.IsMatch(origin) ?? false))
        .WithMethods("GET", "OPTIONS", "POST", "PUT", "PATCH", "DELETE")
        .WithHeaders("Content-Type", "Authorization")
        .WithExposedHeaders("Link", "Retry-After")
        .SetPreflightMaxAge(TimeSpan.FromHours(1));
}));

// Authority (the address of Access) and audience come from configuration only. Without them (local runs, tests) no JWT scheme is added;
// tests register the test auth handler instead.
if (builder.Configuration.GetSection(SharedJwtAuthentication.ConfigurationSection).Exists())
{
    builder.Services.AddSharedJwtAuthentication(builder.Configuration);
}
else
{
    builder.Services.AddAuthentication();
}

var app = builder.Build();

// `dotnet <dll> --migrate` applies the migrations and ends (deployment step); otherwise Development/Testing migrate at start.
if (await app.Services.MigratePortalDatabaseAsync(app.Environment, args))
{
    return;
}

app.UseConfiguredForwardedHeaders();
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapFaqEndpoints();
app.MapReportEndpoints();
app.MapLocationEndpoints();
app.MapReportRecipientEndpoints();
app.MapStatisticsEndpoints();
app.MapAuditEndpoints<PortalDbContext>("portal", PortalPrivileges.AuditRead, PortalPrivileges.AuditReadValues);

app.Run();
