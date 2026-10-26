using Microsoft.AspNetCore.Authentication.JwtBearer;
using Workforce.Api;
using Workforce.Infrastructure;
using Shared.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWorkforceInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSharedAuthorization(options =>
{
    foreach (var role in WorkforceRoles.All)
    {
        options.ServiceRoles.Add(role);
    }
});

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

// Other services (later the HR API) may call the internal endpoints with tokens signed by their own key (ServiceAuth:Peers).
builder.Services.AddServiceAuthentication(
    builder.Configuration,
    builder.Configuration.GetSection(SharedJwtAuthentication.ConfigurationSection).Exists() ? JwtBearerDefaults.AuthenticationScheme : null);

var app = builder.Build();

await app.Services.MigrateWorkforceDatabaseAsync(app.Environment);

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapAbsenceEndpoints();

app.Run();
