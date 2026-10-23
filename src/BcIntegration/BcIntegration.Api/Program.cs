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

// Authority/audience/tenant come from configuration only. Without them (local runs, tests) no JWT scheme is added;
// tests register the test auth handler instead.
if (builder.Configuration.GetSection(SharedJwtAuthentication.ConfigurationSection).Exists())
{
    builder.Services.AddSharedJwtAuthentication(builder.Configuration);
}
else if (LocalServiceAuthentication.IsEnabled(builder.Configuration, builder.Environment))
{
    // Local landscape: the static fake token counts as a calling service (stands in for Entra service tokens).
    builder.Services.AddAuthentication(LocalServiceAuthentication.SchemeName).AddLocalServiceScheme();
}
else
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
