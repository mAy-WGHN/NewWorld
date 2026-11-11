using Access.Core.Acl;
using Shared.Infrastructure.Audit;
using Access.Api;
using Access.Infrastructure;
using Access.Infrastructure.Login;
using Shared.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddAccessInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSharedAuthorization(options =>
{
    foreach (var role in AccessRoles.All)
    {
        options.ServiceRoles.Add(role);
    }
});
builder.Services.AddScoped<IAuthorizationServiceAdapter, AuthorizationServiceAdapter>();

// People sign in through Access (own tokens, own key); other services call with tokens signed by their own key (ServiceAuth section).
builder.Services.AddAccessAuthentication(builder.Configuration);

// Behind a proxy: the client address for the login limit comes from X-Forwarded-For, but only from the configured proxies (ForwardedHeaders section).
builder.Services.AddConfiguredForwardedHeaders(builder.Configuration);

var app = builder.Build();

// `dotnet <dll> --migrate` applies the migrations and ends (deployment step); otherwise Development/Testing migrate at start.
if (await app.Services.MigrateAccessDatabaseAsync(app.Environment, args))
{
    return;
}

app.UseConfiguredForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapAccessEndpoints();
app.MapAuditEndpoints<AccessDbContext>("access", AccessPrivileges.AuditRead, AccessPrivileges.AuditReadValues);

app.Run();
