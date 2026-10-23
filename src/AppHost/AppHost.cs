var builder = DistributedApplication.CreateBuilder(args);

// One PostgreSQL instance, one database per service (own schema inside, no cross-schema joins).
var postgres = builder.AddPostgres("postgres");

// Service Bus emulator (accepts the emulator EULA; decided by the project owner on 2026-10-21).
// Topics, queues and subscriptions follow the message catalogue.
// TODO(Phase 7): declare the topology here and in infra/servicebus.bicep, each subscription with a purpose comment.
var serviceBus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();

// WireMock.Net stands in for Business Central: the same mapping files as the tests (tests/Fixtures/Bc).
// The "master-data" scenario delivers one store and two employees.
var bcMock = builder.AddContainer("bc-mock", "sheyenrath/wiremock.net", "2.19.0")
    .WithBindMount(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tests", "Fixtures", "Bc", "master-data")), "/app/__admin/mappings", isReadOnly: true)
    .WithArgs("--ReadStaticMappings", "true")
    .WithHttpEndpoint(targetPort: 80, name: "http");

var bcIntegrationDb = postgres.AddDatabase("bcintegration");
var accessDb = postgres.AddDatabase("access");
var notificationsDb = postgres.AddDatabase("notifications");
var workforceDb = postgres.AddDatabase("workforce");
var portalDb = postgres.AddDatabase("portal");

builder.AddProject<Projects.BcIntegration_Api>("bc-integration-api")
    .WithReference(bcIntegrationDb).WaitFor(bcIntegrationDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    // BC fake: paths of the assumed custom API pages (TODO(BC-Modell)) and a dummy token (allowed in Development only).
    .WithEnvironment("Bc__BaseUrl", ReferenceExpression.Create($"{bcMock.GetEndpoint("http")}/"))
    .WithEnvironment("Bc__CompanyId", "test-company")
    .WithEnvironment("Bc__StoresPath", "test/stores/v1.0/companies({companyId})/stores")
    .WithEnvironment("Bc__AbsencesPath", "test/absences/v1.0/companies({companyId})/employeeAbsences")
    .WithEnvironment("Bc__Credential", "LocalFake")
    .WaitFor(bcMock)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Access_Api>("access-api")
    .WithReference(accessDb).WaitFor(accessDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Notifications_Api>("notifications-api")
    .WithReference(notificationsDb).WaitFor(notificationsDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Workforce_Api>("workforce-api")
    .WithReference(workforceDb).WaitFor(workforceDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Portal_Api>("portal-api")
    .WithReference(portalDb).WaitFor(portalDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();
