using BcIntegration.Api;
using BcIntegration.Infrastructure;
using Shared.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddBcIntegrationInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSharedAuthorization(options =>
{
    foreach (var role in BcIntegrationRoles.All)
    {
        options.ServiceRoles.Add(role);
    }
});

// Only other services call this API (no frontends): tokens signed with the caller's own key, accepted peers in ServiceAuth:Peers.
// Without configured peers (tests register the test auth handler instead) no service can authenticate.
builder.Services.AddServiceAuthentication(builder.Configuration);
if (!ServiceAuthenticationExtensions.AcceptsServices(builder.Configuration))
{
    builder.Services.AddAuthentication();
}

var app = builder.Build();

await app.Services.MigrateBcIntegrationDatabaseAsync(app.Environment);

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapInternalEndpoints();

app.Run();
