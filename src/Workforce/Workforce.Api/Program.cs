using Shared.Infrastructure.Audit;
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

// Certificate uploads stay in memory: no temporary file with a health document on disk, and only one file of the allowed size per request.
var certificateLimit = builder.Configuration.GetSection(Workforce.Core.Absences.WorkforceOptions.ConfigurationSection).Get<Workforce.Core.Absences.WorkforceOptions>()?.Certificates.MaxFileBytes
    ?? new Workforce.Core.Certificates.CertificateOptions().MaxFileBytes;
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MemoryBufferThreshold = certificateLimit + 256 * 1024;
    options.MultipartBodyLengthLimit = certificateLimit + 64 * 1024;
    options.ValueLengthLimit = 4096;
});

var app = builder.Build();

// `dotnet <dll> --migrate` applies the migrations and ends (deployment step); otherwise Development/Testing migrate at start.
if (await app.Services.MigrateWorkforceDatabaseAsync(app.Environment, args))
{
    return;
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapAbsenceEndpoints();
app.MapUnavailabilityEndpoints();
app.MapWorkingTimeEndpoints();
app.MapCertificateEndpoints();
app.MapEmployeeEndpoints();
app.MapPeopleEndpoints();
app.MapAuditEndpoints<WorkforceDbContext>("workforce", Workforce.Core.Absences.AbsencePrivileges.AuditRead, Workforce.Core.Absences.AbsencePrivileges.AuditReadValues);

app.Run();
