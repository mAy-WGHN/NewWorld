var builder = DistributedApplication.CreateBuilder(args);

// One PostgreSQL instance, one database per service (own schema inside, no cross-schema joins).
var postgres = builder.AddPostgres("postgres");

// Service Bus emulator (accepts the emulator EULA; decided by the project owner on 2026-10-21).
// The topology (topics, queues, subscriptions with filters) is the same as in infra/servicebus.bicep; a test compares both and the
// message catalogue (tests/Architecture.Tests/ServiceBusTopologyTests.cs).
var serviceBus = builder.AddAzureServiceBus("servicebus").RunAsEmulator(emulator =>
    emulator.WithConfigurationFile(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "infra", "servicebus-emulator.json"))));

// WireMock.Net stands in for Business Central: the same mapping files as the tests (tests/Fixtures/Bc).
// The "e2e-landscape" scenario delivers one store, six employees and the PIN check; tests add absence scenarios through the admin API.
var bcMock = builder.AddContainer("bc-mock", "sheyenrath/wiremock.net", "2.19.0")
    .WithBindMount(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tests", "Fixtures", "Bc", "e2e-landscape")), "/app/__admin/mappings", isReadOnly: true)
    .WithArgs("--ReadStaticMappings", "true")
    .WithHttpEndpoint(targetPort: 80, name: "http");

var bcIntegrationDb = postgres.AddDatabase("bcintegration");
var accessDb = postgres.AddDatabase("access");
var notificationsDb = postgres.AddDatabase("notifications");
var workforceDb = postgres.AddDatabase("workforce");
var portalDb = postgres.AddDatabase("portal");

var bcIntegration = builder.AddProject<Projects.BcIntegration_Api>("bc-integration-api")
    .WithReference(bcIntegrationDb).WaitFor(bcIntegrationDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    // BC fake: paths of the assumed custom API pages (TODO(BC-Modell)) and a dummy token (allowed in Development only).
    .WithEnvironment("Bc__BaseUrl", ReferenceExpression.Create($"{bcMock.GetEndpoint("http")}/"))
    .WithEnvironment("Bc__CompanyId", "test-company")
    .WithEnvironment("Bc__StoresPath", "test/stores/v1.0/companies({companyId})/stores")
    .WithEnvironment("Bc__AbsencesPath", "test/absences/v1.0/companies({companyId})/employeeAbsences")
    .WithEnvironment("Bc__PinVerifyPath", "test/employeeauth/v1.0/companies({companyId})/verifyPin")
    .WithEnvironment("Bc__Credential", "LocalFake")
    // Stands in for Entra service tokens locally (the static fake token counts as a calling service). Development/Testing only.
    .WithEnvironment("LocalServiceAuth", "true")
    .WaitFor(bcMock)
    .WithHttpHealthCheck("/health/ready");

var access = builder.AddProject<Projects.Access_Api>("access-api")
    .WithReference(accessDb).WaitFor(accessDb)
    .WithEnvironment("LocalServiceAuth", "true")
    // First administrator (receives the built-in administrator role once the employee copy arrives).
    .WithEnvironment("Access__BootstrapAdministrators__0", "10099")
    // Login: PIN check through BC Integration (dummy token locally, allowed in Development only).
    .WithEnvironment("BcIntegration__BaseUrl", bcIntegration.GetEndpoint("http"))
    .WithEnvironment("BcIntegration__Credential", "LocalFake")
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Notifications_Api>("notifications-api")
    .WithReference(notificationsDb).WaitFor(notificationsDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithAccess(access)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Workforce_Api>("workforce-api")
    .WithReference(workforceDb).WaitFor(workforceDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithAccess(access)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Portal_Api>("portal-api")
    .WithReference(portalDb).WaitFor(portalDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithAccess(access)
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();

internal static class AppHostExtensions
{
    /// <summary>
    /// A service validates people's tokens against Access (discovery/JWKS, plain http locally) and registers its privilege catalogue
    /// there with the local fake service token.
    /// </summary>
    public static IResourceBuilder<T> WithAccess<T>(this IResourceBuilder<T> service, IResourceBuilder<ProjectResource> access)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport => service
            .WithEnvironment("Authentication__Authority", access.GetEndpoint("http"))
            .WithEnvironment("Authentication__Audience", "newworld")
            .WithEnvironment("Authentication__RequireHttpsMetadata", "false")
            .WithEnvironment("Access__BaseUrl", access.GetEndpoint("http"))
            .WithEnvironment("Access__Credential", "LocalFake");
}
