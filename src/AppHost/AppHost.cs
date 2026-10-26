using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

// Service-to-service authentication: every service has its own key pair (new at every start of the landscape). It signs short-lived
// tokens with its private key; the services it calls only get its public key and the roles it may use there (ServiceAuth section).
var accessKey = ServiceKey.Create();
var notificationsKey = ServiceKey.Create();
var workforceKey = ServiceKey.Create();

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
    // Only Access may call BC Integration (PIN check for the login).
    .WithServiceIdentity("bc-integration")
    .WithServicePeer("access", accessKey, "BcIntegration.Pin.Verify")
    .WaitFor(bcMock)
    .WithHttpHealthCheck("/health/ready");

var access = builder.AddProject<Projects.Access_Api>("access-api")
    .WithReference(accessDb).WaitFor(accessDb)
    .WithServiceIdentity("access", accessKey)
    // Workforce and Notifications register their privilege catalogue with Access and rebuild their permission copy from it.
    .WithServicePeer("workforce", workforceKey, "Access.Catalog.Register", "Access.Permissions.Read")
    .WithServicePeer("notifications", notificationsKey, "Access.Catalog.Register", "Access.Permissions.Read")
    // First administrator (receives the built-in administrator role once the employee copy arrives).
    .WithEnvironment("Access__BootstrapAdministrators__0", "10099")
    // Login: PIN check through BC Integration.
    .WithEnvironment("BcIntegration__BaseUrl", bcIntegration.GetEndpoint("http"))
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Notifications_Api>("notifications-api")
    .WithReference(notificationsDb).WaitFor(notificationsDb)
    .WithServiceIdentity("notifications", notificationsKey)
    .WithReference(serviceBus).WaitFor(serviceBus)
    .WithAccess(access)
    .WithHttpHealthCheck("/health/ready");

builder.AddProject<Projects.Workforce_Api>("workforce-api")
    .WithReference(workforceDb).WaitFor(workforceDb)
    .WithServiceIdentity("workforce", workforceKey)
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
    /// there with its signed service token (see <see cref="WithServiceIdentity{T}"/>).
    /// </summary>
    public static IResourceBuilder<T> WithAccess<T>(this IResourceBuilder<T> service, IResourceBuilder<ProjectResource> access)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport => service
            .WithEnvironment("Authentication__Authority", access.GetEndpoint("http"))
            .WithEnvironment("Authentication__Audience", "newworld")
            .WithEnvironment("Authentication__RequireHttpsMetadata", "false")
            .WithEnvironment("Access__BaseUrl", access.GetEndpoint("http"));

    /// <summary>This service's name (token audience) and, if it calls other services, its private key.</summary>
    public static IResourceBuilder<T> WithServiceIdentity<T>(this IResourceBuilder<T> service, string name, ServiceKey? key = null)
        where T : IResourceWithEnvironment
    {
        service.WithEnvironment("ServiceAuth__Name", name);
        return key is null ? service : service.WithEnvironment("ServiceAuth__PrivateKey", key.PrivateKey);
    }

    /// <summary>Lets the service <paramref name="peer"/> call this one: its public key and the roles it may use.</summary>
    public static IResourceBuilder<T> WithServicePeer<T>(this IResourceBuilder<T> service, string peer, ServiceKey key, params string[] roles)
        where T : IResourceWithEnvironment
    {
        service.WithEnvironment($"ServiceAuth__Peers__{peer}__PublicKeys__0", key.PublicKey);
        for (var i = 0; i < roles.Length; i++)
        {
            service.WithEnvironment($"ServiceAuth__Peers__{peer}__Roles__{i}", roles[i]);
        }

        return service;
    }
}

/// <summary>ECDSA P-256 key pair in the format of the ServiceAuth configuration (base64 PKCS#8 / SubjectPublicKeyInfo).</summary>
internal sealed record ServiceKey(string PrivateKey, string PublicKey)
{
    public static ServiceKey Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ServiceKey(Convert.ToBase64String(key.ExportPkcs8PrivateKey()), Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
    }
}
