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

var app = builder.Build();

await app.Services.MigrateAccessDatabaseAsync(app.Environment);

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapAccessEndpoints();

app.Run();
