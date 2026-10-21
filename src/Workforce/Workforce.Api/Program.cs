using Workforce.Infrastructure;
using Shared.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddWorkforceInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSharedAuthorization();

// Authority/audience/tenant come from configuration only. Without them (local runs, tests) no JWT scheme is added;
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

await app.Services.InitializeWorkforceDatabaseAsync(app.Environment);

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

app.Run();
