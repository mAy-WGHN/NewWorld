using BcIntegration.Infrastructure.BusinessCentral;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>
/// Runs only with <c>BC_SANDBOX_TESTS=1</c>; otherwise skipped (visible in the test output). Never part of the default run.
/// </summary>
public sealed class BcSandboxFactAttribute : FactAttribute
{
    public BcSandboxFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BC_SANDBOX_TESTS") != "1")
        {
            Skip = "Real BC sandbox: set BC_SANDBOX_TESTS=1 and Bc__BaseUrl, Bc__CompanyId, Bc__SoapBaseUrl, Bc__CompanyName, Bc__Credential (plus AZURE_* for the credential).";
        }
    }
}

/// <summary>
/// Against a real BC sandbox, read-only. Verifies the assumptions of the BC client (TODO(BC-Modell)) without writing anything.
/// Configuration comes from environment variables only, never from files in the repository.
/// </summary>
[Trait("Category", "BcSandbox")]
public class BcSandboxTests
{
    private static BcClient CreateClient()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBcClient(configuration);
        return services.BuildServiceProvider().GetRequiredService<BcClient>();
    }

    [BcSandboxFact]
    public async Task Employees_endpoint_answers_with_the_assumed_shape()
    {
        var employees = await CreateClient().GetEmployeesModifiedSinceAsync(TimeProvider.System.GetUtcNow().AddDays(-1), CancellationToken.None);

        employees.ShouldAllBe(e => e.BcSystemId != Guid.Empty && e.PersonnelNumber.Length > 0);
    }

    /// <summary>
    /// The PIN check against the real codeunit <c>Zeiterfassung.GetStatus</c> (settings <c>Bc__SoapBaseUrl</c>, <c>Bc__CompanyName</c>). Needs a test
    /// employee: <c>BC_SANDBOX_EMPLOYEE_NO</c> and <c>BC_SANDBOX_EMPLOYEE_PIN</c> (environment only, never in the repository). Sends a wrong PIN too.
    /// </summary>
    [BcSandboxFact]
    public async Task Pin_check_answers_like_the_hr_tool_expects()
    {
        var number = Environment.GetEnvironmentVariable("BC_SANDBOX_EMPLOYEE_NO");
        var pin = Environment.GetEnvironmentVariable("BC_SANDBOX_EMPLOYEE_PIN");
        if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(pin))
        {
            Assert.Fail("Set BC_SANDBOX_EMPLOYEE_NO and BC_SANDBOX_EMPLOYEE_PIN for the PIN check.");
        }

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBcClient(configuration);
        using var provider = services.BuildServiceProvider();
        var verifier = provider.GetRequiredService<BcIntegration.Core.Login.IBcPinVerifier>();

        var right = await verifier.VerifyAsync(number!, pin!, CancellationToken.None);
        var wrong = await verifier.VerifyAsync(number!, pin + "x", CancellationToken.None);

        right.Authenticated.ShouldBeTrue();
        right.EmployeeName.ShouldNotBeNullOrWhiteSpace();
        wrong.Authenticated.ShouldBeFalse();
    }
}
