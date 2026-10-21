var builder = DistributedApplication.CreateBuilder(args);

// One PostgreSQL instance, one database per service (own schema inside, no cross-schema joins).
var postgres = builder.AddPostgres("postgres");

// Service Bus emulator. Topics, queues and subscriptions follow the message catalogue.
// TODO(Phase 2): declare the topology here and in infra/ (Bicep), each subscription with a purpose comment.
var serviceBus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();

// WireMock stands in for Business Central. Mappings are files under tests/Fixtures/Bc.
// TODO(Phase 3): mappings for the BC endpoints once the BC data model is known.
var bcMock = builder.AddContainer("bc-mock", "wiremock/wiremock", "3.13.1")
    .WithBindMount(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tests", "Fixtures", "Bc")), "/home/wiremock")
    .WithHttpEndpoint(targetPort: 8080, name: "http");

var bcIntegrationDb = postgres.AddDatabase("bcintegration");
var accessDb = postgres.AddDatabase("access");
var notificationsDb = postgres.AddDatabase("notifications");
var workforceDb = postgres.AddDatabase("workforce");
var portalDb = postgres.AddDatabase("portal");

builder.AddProject<Projects.BcIntegration_Api>("bc-integration-api")
    .WithReference(bcIntegrationDb).WaitFor(bcIntegrationDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithEnvironment("Bc__BaseUrl", bcMock.GetEndpoint("http"))
    .WaitFor(bcMock)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Access_Api>("access-api")
    .WithReference(accessDb).WaitFor(accessDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Notifications_Api>("notifications-api")
    .WithReference(notificationsDb).WaitFor(notificationsDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Workforce_Api>("workforce-api")
    .WithReference(workforceDb).WaitFor(workforceDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Portal_Api>("portal-api")
    .WithReference(portalDb).WaitFor(portalDb)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpEndpoint(name: "http")
    .WithHttpHealthCheck("/health/ready");

builder.Build().Run();
